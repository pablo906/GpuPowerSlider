using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace GpuPowerSlider {
    public sealed class MainForm : Form {
        readonly Controller controller;
        readonly Timer timer=new Timer { Interval=2000 };
        public readonly TrackBar Slider=new TrackBar { Minimum=120,Maximum=175,Value=150,TickFrequency=5,SmallChange=1,LargeChange=5 };
        public readonly NumericUpDown Watts=new NumericUpDown { Minimum=120,Maximum=175,Value=150,DecimalPlaces=0,Increment=1 };
        readonly Button apply,restore;
        readonly Label enforced,maximum,telemetry,status,range,rangeMax;
        readonly TableLayoutPanel layout;
        bool layoutReady,fitting,fitQueued;
        bool ready,busy,refreshing,editing,manual;
        int? pending,lastApplied;
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd,int attribute,ref int value,int size);
        public MainForm(Controller controller,bool automatic=true) {
            this.controller=controller;
            SuspendLayout();
            Text="GPU Power Slider";ClientSize=new Size(560,500);FormBorderStyle=FormBorderStyle.FixedSingle;
            MaximizeBox=false;StartPosition=FormStartPosition.CenterScreen;
            Font=new Font("Segoe UI",11);
            AutoScaleDimensions=new SizeF(96,96);AutoScaleMode=AutoScaleMode.Dpi;
            layout=new TableLayoutPanel { Dock=DockStyle.Top,AutoSize=true,ColumnCount=1,Padding=new Padding(24),Margin=Padding.Empty };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));Controls.Add(layout);
            AddRow(layout,MakeLabel("GPU power",22,null));
            AddRow(layout,MakeLabel("GA403WW  /  RTX 5080 Laptop",10,"muted"));
            var card=Columns(2);card.Padding=new Padding(14);card.Tag="card";
            card.Controls.Add(MakeLabel("ENFORCED",10,"muted"),0,0);
            card.Controls.Add(MakeLabel("AVAILABLE MAX",10,"muted"),1,0);
            enforced=MakeLabel("-- W",22,"accent");maximum=MakeLabel("-- W",22,null);
            card.Controls.Add(enforced,0,1);card.Controls.Add(maximum,1,1);AddRow(layout,card);
            telemetry=MakeLabel("Reading GPU...",10,"muted");AddRow(layout,telemetry);
            var target=Columns(3);target.ColumnStyles.Clear();
            target.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            target.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));target.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var targetLabel=MakeLabel("Target power",12,null);targetLabel.Anchor=AnchorStyles.Left;target.Controls.Add(targetLabel,0,0);
            Watts.Font=new Font("Segoe UI",17);Watts.Width=115;Watts.TextAlign=HorizontalAlignment.Center;Watts.Anchor=AnchorStyles.Right;Watts.Margin=new Padding(6,0,6,0);target.Controls.Add(Watts,1,0);
            var unit=MakeLabel("W",12,"muted");unit.Anchor=AnchorStyles.Right;target.Controls.Add(unit,2,0);AddRow(layout,target);
            Slider.Dock=DockStyle.Fill;Slider.Margin=Padding.Empty;AddRow(layout,Slider);
            var ranges=Columns(2);range=MakeLabel("120 W",10,"muted");rangeMax=MakeLabel("175 W",10,"muted");rangeMax.TextAlign=ContentAlignment.MiddleRight;
            ranges.Controls.Add(range,0,0);ranges.Controls.Add(rangeMax,1,0);AddRow(layout,ranges);
            var buttons=Columns(2);apply=MakeButton("Apply limit");restore=MakeButton("Restore 25 W boost");
            apply.Margin=new Padding(0,0,6,0);restore.Margin=new Padding(6,0,0,0);
            buttons.Controls.Add(apply,0,0);buttons.Controls.Add(restore,1,0);AddRow(layout,buttons);
            status=MakeLabel("Checking compatibility...",11,null);AddRow(layout,status);
            AddRow(layout,MakeLabel("Pause GPU workloads to apply. G-Helper mode or boost changes can replace this runtime limit. Closing this app leaves the applied limit active.",10,"muted"));
            Slider.ValueChanged+=delegate { if(editing)return;editing=true;Watts.Value=Slider.Value;editing=false; };
            Watts.ValueChanged+=delegate { if(editing)return;editing=true;int value=(int)Math.Round(Watts.Value);Watts.Value=value;Slider.Value=value;editing=false; };
            apply.Click+=async delegate {
                if(pending.HasValue) { pending=null;status.Text="Pending change cancelled.";SetControls();return; }
                await ApplyTarget((int)Watts.Value);
            };
            restore.Click+=async delegate { await Restore(); };
            AcceptButton=apply;
            timer.Tick+=async delegate { await RefreshReadback(); };
            FormClosed+=delegate { SystemEvents.UserPreferenceChanged-=ThemeChanged;timer.Stop();timer.Dispose(); };
            SystemEvents.UserPreferenceChanged+=ThemeChanged;
            HandleCreated+=delegate { ApplyWindowsTheme(); };
            ApplyWindowsTheme();ResumeLayout(true);layoutReady=true;
            SetControls();
            if(automatic) Shown+=async delegate { await Initialize(); };
        }
        protected override void OnLayout(LayoutEventArgs e) {
            base.OnLayout(e);
            if(!layoutReady||fitting||fitQueued||!IsHandleCreated||IsDisposed||Disposing)return;
            fitQueued=true;
            BeginInvoke((Action)delegate { fitQueued=false;FitContent(); });
        }
        protected override void OnShown(EventArgs e) {
            FitContent();base.OnShown(e);
        }
        void FitContent() {
            if(!layoutReady||fitting||layout==null||IsDisposed||Disposing)return;
            fitting=true;
            try {
                layout.PerformLayout();
                int height=layout.Height;
                if(ClientSize.Height!=height)ClientSize=new Size(ClientSize.Width,height);
            } finally { fitting=false; }
        }
        static Label MakeLabel(string text,float size,string role) {
            return new Label { Text=text,AutoSize=true,Dock=DockStyle.Fill,Font=new Font("Segoe UI",size),Tag=role,Margin=Padding.Empty };
        }
        static TableLayoutPanel Columns(int count) {
            var table=new TableLayoutPanel { AutoSize=true,Dock=DockStyle.Fill,ColumnCount=count,Margin=Padding.Empty };
            for(int i=0;i<count;i++)table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100f/count));
            return table;
        }
        static void AddRow(TableLayoutPanel layout,Control control) {
            int row=layout.RowCount++;layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            control.Margin=new Padding(0,0,0,12);layout.Controls.Add(control,0,row);
        }
        static Button MakeButton(string text) {
            var button=new Button { Text=text,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,Dock=DockStyle.Fill,Padding=new Padding(10,8,10,8),FlatStyle=FlatStyle.Flat,UseVisualStyleBackColor=false };
            button.FlatAppearance.BorderSize=1;return button;
        }
        void ThemeChanged(object sender,UserPreferenceChangedEventArgs e) {
            if(IsDisposed||!IsHandleCreated)return;
            try { BeginInvoke((Action)ApplyWindowsTheme); } catch(InvalidOperationException) { }
        }
        public void ApplyWindowsTheme() {
            bool dark=false;
            using(var key=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize")) {
                if(key!=null)dark=Convert.ToInt32(key.GetValue("AppsUseLightTheme",1))==0;
            }
            SetTheme(dark);
        }
        public void SetTheme(bool dark) {
            Color bg=dark?Color.FromArgb(24,24,24):Color.FromArgb(240,243,245);
            Color fg=dark?Color.FromArgb(245,245,245):Color.FromArgb(20,29,35);
            Color muted=dark?Color.FromArgb(210,210,210):Color.FromArgb(65,75,83);
            Color surface=dark?Color.FromArgb(42,42,42):Color.White;
            Color accent=dark?Color.FromArgb(114,235,206):Color.FromArgb(0,91,72);
            PaintTheme(this,bg,fg,muted,surface,accent);
            Watts.BackColor=surface;Watts.ForeColor=fg;
            apply.BackColor=dark?Color.FromArgb(0,112,88):Color.FromArgb(0,91,72);apply.ForeColor=Color.White;
            restore.BackColor=surface;restore.ForeColor=fg;
            apply.FlatAppearance.BorderColor=restore.FlatAppearance.BorderColor=dark?Color.FromArgb(115,115,115):Color.FromArgb(170,180,185);
            if(IsHandleCreated) {
                int value=dark?1:0;
                if(DwmSetWindowAttribute(Handle,20,ref value,4)!=0)DwmSetWindowAttribute(Handle,19,ref value,4);
            }
        }
        static void PaintTheme(Control control,Color bg,Color fg,Color muted,Color surface,Color accent) {
            string role=control.Tag as string;
            control.BackColor=role=="card"?surface:bg;
            control.ForeColor=role=="muted"?muted:role=="accent"?accent:fg;
            foreach(Control child in control.Controls)PaintTheme(child,control.BackColor,fg,muted,surface,accent);
        }
        public void SetRange(int min,int max,int selected) {
            editing=true;
            Slider.Minimum=0;Slider.Maximum=max;Slider.Minimum=min;
            Watts.Minimum=0;Watts.Maximum=max;Watts.Minimum=min;
            selected=Math.Max(min,Math.Min(max,selected));Slider.Value=selected;Watts.Value=selected;
            range.Text=min+" W";rangeMax.Text=max+" W";
            editing=false;
        }
        async Task Initialize() {
            busy=true;SetControls();
            try {
                Capabilities cap=await Task.Run(()=>controller.Check());
                Snapshot read=await Task.Run(()=>controller.Read());
                ready=cap.Ready;
                if(ready)SetRange(cap.Minimum,cap.Maximum,(int)Math.Round(read.Enforced));
                ShowReadback(read);status.Text=ready?"Ready. Choose a target and apply.":cap.Reason;
                timer.Start();
            } catch(Exception e) { status.Text=e.Message; }
            finally { busy=false;SetControls(); }
        }
        void SetControls() {
            Slider.Enabled=Watts.Enabled=ready&&!busy&&!pending.HasValue;
            apply.Enabled=ready&&!busy;restore.Enabled=ready&&!busy&&!pending.HasValue;
            apply.Text=pending.HasValue?"Cancel pending "+pending.Value+" W":"Apply limit";
        }
        public void ShowReadback(Snapshot read) {
            enforced.Text=read.Enforced.ToString("0.0")+" W";maximum.Text=read.Maximum.ToString("0")+" W";
            telemetry.Text=read.Draw.ToString("0.0")+" W draw   /   "+read.Usage.ToString("0")+"% GPU usage   /   "+read.Temperature.ToString("0")+" °C";
            if(!busy&&manual&&lastApplied.HasValue&&!pending.HasValue&&Math.Abs(read.Enforced-lastApplied.Value)>0.51)
                status.Text="Enforced limit changed to "+read.Enforced.ToString("0.0")+" W. Reapply when idle.";
        }
        async Task ApplyTarget(int target) {
            if(busy||!ready)return;
            busy=true;pending=null;SetControls();status.Text="Applying "+target+" W...";
            try {
                Outcome result=await Task.Run(()=>controller.Apply(target));
                if(IsDisposed)return;
                if(result.Busy)pending=target;
                if(result.Success){manual=true;lastApplied=target;}
                status.Text=result.Message;Controller.Log("apply",target,result);
                ShowReadback(await Task.Run(()=>controller.Read()));
            } catch(Exception e) { if(!IsDisposed)status.Text=e.Message; }
            finally { if(!IsDisposed){busy=false;SetControls();} }
        }
        async Task Restore() {
            if(busy)return;
            busy=true;SetControls();status.Text="Restoring Dynamic Boost...";
            try {
                Outcome result=await Task.Run(()=>controller.RestoreBoost());
                if(IsDisposed)return;
                if(result.Success){manual=false;lastApplied=null;}
                status.Text=result.Message;Controller.Log("restore boost",25,result);
                ShowReadback(await Task.Run(()=>controller.Read()));
            } catch(Exception e) { if(!IsDisposed)status.Text=e.Message; }
            finally { if(!IsDisposed){busy=false;SetControls();} }
        }
        async Task RefreshReadback() {
            if(busy||refreshing||IsDisposed)return;
            refreshing=true;
            try {
                Snapshot read=await Task.Run(()=>controller.Read());
                if(IsDisposed||busy)return;
                ShowReadback(read);
                if(pending.HasValue&&read.Usage<=30) await ApplyTarget(pending.Value);
            } catch(Exception e) { if(!IsDisposed)status.Text="Readback unavailable: "+e.Message; }
            finally { refreshing=false; }
        }
        protected override void OnFormClosing(FormClosingEventArgs e) {
            if(busy){e.Cancel=true;status.Text="Wait for the current operation to finish.";}
            else pending=null;
            base.OnFormClosing(e);
        }
    }
}

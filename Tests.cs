using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace GpuPowerSlider {
    public sealed class FakeDevice : IDevice {
        public double Current=150,Max=175,Usage=2;
        public bool Trusted=true,RejectNext,RejectAll,MismatchNext;
        bool mismatch;
        public int Writes,BoostWrites;
        public Snapshot Read() {
            double value=mismatch?Current-10:Current;mismatch=false;
            return new Snapshot { Enforced=value,Maximum=Max,Draw=27,Usage=Usage,Temperature=47 };
        }
        public Capabilities Check() { return new Capabilities { Ready=Trusted,Minimum=120,Maximum=(int)Max,Reason="Untrusted driver" }; }
        public Outcome SetCurrent(int watts) {
            Writes++;
            if(RejectNext||RejectAll){RejectNext=false;return Outcome.Fail("Driver refused the write.");}
            Current=watts;
            if(MismatchNext){MismatchNext=false;mismatch=true;}
            return new Outcome { Success=true,Message="Applied" };
        }
        public void SetBoost(int watts) { BoostWrites++;Current=watts==0?95:120; }
    }
    public static class Tests {
        static int passed;
        static void Assert(bool condition,string name) { if(!condition)throw new Exception(name);passed++;Console.WriteLine("PASS "+name); }
        static System.Collections.Generic.IEnumerable<Control> Descendants(Control parent) {
            foreach(Control child in parent.Controls) {
                yield return child;
                foreach(Control nested in Descendants(child))yield return nested;
            }
        }
        static bool LabelsFit(Form form) {
            return Descendants(form).OfType<Label>().All(label=>label.Height>=label.GetPreferredSize(new Size(label.Width,0)).Height
                &&label.Parent.ClientRectangle.Contains(label.Bounds));
        }
        static void PumpUntil(Func<bool> condition,int timeout=20000) {
            var until=DateTime.UtcNow.AddMilliseconds(timeout);
            while(!condition()&&DateTime.UtcNow<until){Application.DoEvents();Thread.Sleep(20);}
            if(!condition())throw new Exception("UI operation timed out");
        }
        [STAThread] public static int Main() {
            try {
                Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
                Control.CheckForIllegalCrossThreadCalls=true;
                int exitCode=1;
                // Keep a real message loop alive while nested DoEvents pumps run.
                // Without it, async UI continuations can lose their STA context.
                using(var host=new Form { ShowInTaskbar=false,StartPosition=FormStartPosition.Manual,Location=new Point(-3000,-3000),Size=new Size(1,1) }) {
                    host.Shown+=delegate {
                        host.BeginInvoke((Action)delegate {
                            try { RunChecks();exitCode=0; }
                            catch(Exception e) { Console.Error.WriteLine(e); }
                            finally { host.Close(); }
                        });
                    };
                    Application.Run(host);
                }
                return exitCode;
            } catch(Exception e) { Console.Error.WriteLine(e);return 1; }
        }
        static void RunChecks() {
                var f=new FakeDevice { Usage=99 };var c=new Controller(f);
                Assert(c.Apply(160).Busy&&f.Writes==0&&f.BoostWrites==0,"Busy GPU changes no hardware");
                f=new FakeDevice();c=new Controller(f);
                Assert(!c.Apply(176).Success&&!c.Apply(119).Success&&f.Writes==0&&f.BoostWrites==0,"Out-of-range requests change no hardware");
                f=new FakeDevice { Trusted=false };c=new Controller(f);
                Assert(!c.Apply(150).Success&&f.Writes==0&&f.BoostWrites==0,"Untrusted driver changes no hardware");
                f=new FakeDevice();c=new Controller(f);
                Assert(c.Apply(150).Success&&f.Current==150&&f.Max==175&&f.BoostWrites==1,"Successful transaction retains MAX and verifies CURRENT");
                f=new FakeDevice { RejectNext=true };c=new Controller(f);
                var outcome=c.Apply(160);
                Assert(!outcome.Success&&f.Current==150&&outcome.Message.Contains("Restored 150"),"Rejected write restores previous CURRENT");
                f=new FakeDevice { MismatchNext=true };c=new Controller(f);
                Assert(!c.Apply(160).Success&&f.Current==150,"False-success readback mismatch restores CURRENT");
                f=new FakeDevice { RejectAll=true };c=new Controller(f);
                outcome=c.Apply(160);
                Assert(!outcome.Success&&outcome.Message.Contains("could not be confirmed"),"Failed rollback is reported honestly");
                f=new FakeDevice { Current=115,RejectNext=true };c=new Controller(f);
                outcome=c.Apply(150);
                Assert(!outcome.Success&&f.Current==120&&f.BoostWrites==2,"Prior dynamic policy restores 25 W boost on failure");
                f=new FakeDevice();
                using(var form=new MainForm(new Controller(f))) {
                    form.StartPosition=FormStartPosition.Manual;form.Location=new Point(-2000,-2000);form.ShowInTaskbar=false;form.Show();
                    PumpUntil(()=>form.Slider.Enabled);
                    Assert(f.Writes==0&&f.BoostWrites==0,"Opening GUI does not change settings");
                    form.Slider.Value=145;Assert(form.Watts.Value==145,"Slider updates wattage box");
                    form.Watts.Value=160;Assert(form.Slider.Value==160,"Wattage box updates slider");
                    form.SetRange(120,125,150);Assert(form.Slider.Value==125&&form.Watts.Value==125,"Range shrinks to live MAX");
                    form.SetRange(120,175,150);
                    Assert(LabelsFit(form),"Text fits its layout at the current display scale");
                    form.SetTheme(false);
                    Assert(form.BackColor.GetBrightness()>0.8f&&form.Watts.ForeColor.GetBrightness()<0.2f,"Light theme has readable native input text");
                    form.SetTheme(true);
                    Assert(form.BackColor.GetBrightness()<0.2f&&form.Watts.ForeColor.GetBrightness()>0.8f,"Dark theme has readable native input text");
                    form.ApplyWindowsTheme();
                    using(var bitmap=new Bitmap(form.Width,form.Height)) { form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"preview.png")); }
                    form.Scale(new SizeF(1.5f,1.5f));form.PerformLayout();Application.DoEvents();
                    Assert(LabelsFit(form),"Text fits at 150 percent layout scaling");
                    using(var bitmap=new Bitmap(form.Width,form.Height)) { form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"preview-scaled.png")); }
                    f.Usage=99;form.Watts.Value=160;
                    var apply=Descendants(form).OfType<Button>().First(b=>b.Text=="Apply limit");
                    apply.PerformClick();
                    PumpUntil(()=>apply.Text.StartsWith("Cancel pending"));
                    Assert(f.Writes==0&&f.BoostWrites==0,"Busy GUI request is queued without hardware writes");
                    apply.PerformClick();Assert(apply.Text=="Apply limit"&&f.Writes==0,"Queued change can be cancelled");
                    apply.PerformClick();PumpUntil(()=>apply.Text.StartsWith("Cancel pending"));f.Usage=2;
                    PumpUntil(()=>f.Current==160&&apply.Enabled&&apply.Text=="Apply limit",25000);
                    Assert(f.Writes==1&&f.BoostWrites==1,"Queued request applies once after GPU becomes idle");
                    f.Current=120;
                    var until=DateTime.UtcNow.AddMilliseconds(2400);
                    while(DateTime.UtcNow<until){Application.DoEvents();Thread.Sleep(20);}
                    Assert(f.Writes==1,"External limit changes are not fought by an automatic write loop");
                    form.Close();
                }
                File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"test-results.txt"),passed+" checks passed"+Environment.NewLine);
                Console.WriteLine(passed+" checks passed");
        }
    }
}

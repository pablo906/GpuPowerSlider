using System;
using System.IO;
using System.Security.Principal;
using System.Threading;
using System.Windows.Forms;

namespace GpuPowerSlider {
    static class Program {
        [STAThread] static int Main(string[] args) {
            Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
            bool owner;
            using(var mutex=new Mutex(true,"Local\\GA403WW-GpuPowerSlider",out owner)) {
                if(!owner){MessageBox.Show("GPU Power Slider is already open.");return 1;}
                try {
                    var principal=new WindowsPrincipal(WindowsIdentity.GetCurrent());
                    if(!principal.IsInRole(WindowsBuiltInRole.Administrator))throw new InvalidOperationException("Run this app as administrator to change GPU power.");
                    var controller=new Controller(new InstalledBackend());
                    if(args.Length>0&&args[0]=="--verify-live") {
                        int target=args.Length>1?int.Parse(args[1]):(int)Math.Round(controller.Read().Enforced);
                        Outcome result=controller.Apply(target);
                        Controller.Log("live verification",target,result);
                        File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"live-verification.txt"),result.Success+" "+result.Message+Environment.NewLine);
                        return result.Success?0:1;
                    }
                    Application.Run(new MainForm(controller));return 0;
                } catch(Exception e) { MessageBox.Show(e.Message,"GPU Power Slider",MessageBoxButtons.OK,MessageBoxIcon.Error);return 1; }
            }
        }
    }
}

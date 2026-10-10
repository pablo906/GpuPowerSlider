using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace GpuPowerSlider {
    public sealed class Snapshot { public double Enforced, Maximum, Draw, Usage, Temperature; }
    public sealed class Capabilities { public int Minimum=120, Maximum=175; public bool Ready; public string Reason; }
    public sealed class Outcome {
        public bool Success, Busy; public string Message;
        public static Outcome Fail(string message) { return new Outcome { Message=message }; }
    }
    public interface IDevice {
        Snapshot Read(); Capabilities Check(); Outcome SetCurrent(int watts); void SetBoost(int watts);
    }
    public sealed class InstalledBackend : IDevice {
        const string ReviewedHash="B12885BD6A2BB9B193758E67AF574ABC6566404E8786206D5E5B749CE950648A";
        readonly object backend;
        readonly Type type;
        public InstalledBackend() {
            string folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Programs","NvpwrControl 2.4.6");
            string exe=Path.Combine(folder,"NvpwrControl.exe");
            if(!File.Exists(exe)) throw new FileNotFoundException("Install the verified NvpwrControl 2.4.6 backend first.",exe);
            using(var stream=File.OpenRead(exe)) using(var sha=SHA256.Create())
                if(BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","")!=ReviewedHash)
                    throw new InvalidOperationException("NvpwrControl has changed. Verify the updated backend before using this app.");
            string state=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"state");
            Directory.CreateDirectory(state);
            foreach(string file in new[]{"driver-resolver-cache.txt","vbios-resolver-cache-v4.txt"}) {
                string source=Path.Combine(folder,"state",file);
                string dest=Path.Combine(state,file);
                if(File.Exists(source)) File.Copy(source,dest,true);
                else if(File.Exists(dest)) File.Delete(dest);
            }
            type=Assembly.LoadFrom(exe).GetType("NvpwrControlBlackwell.PowerBackend",true);
            backend=Activator.CreateInstance(type,true);
        }
        object Invoke(string method,params object[] args) {
            try { return type.GetMethod(method).Invoke(backend,args); }
            catch(TargetInvocationException e) { throw e.InnerException ?? e; }
        }
        static object Field(object obj,string name) { return obj.GetType().GetField(name).GetValue(obj); }
        public Capabilities Check() {
            object state=Invoke("CheckCompatibility");
            var cap=new Capabilities { Reason=(string)Field(state,"Reason") };
            string identity=(string)Field(state,"GpuPnpId");
            if(!identity.Contains("DEV_2C59&SUBSYS_39C81043")) { cap.Reason="This build supports the verified GA403WW RTX 5080."; return cap; }
            cap.Ready=(bool)Field(state,"CurrentWritesReady");
            object profile=Field(state,"Profile");
            if(profile!=null) cap.Minimum=(int)Field(profile,"StockPowerW");
            cap.Maximum=Math.Min(175,(int)Math.Floor(Read().Maximum+0.01));
            if(cap.Maximum<cap.Minimum) { cap.Ready=false; cap.Reason="Live MAX is below this backend's supported range."; }
            if(cap.Ready) cap.Reason="Validated driver; runtime changes available.";
            return cap;
        }
        public Snapshot Read() {
            string smi=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"nvidia-smi.exe");
            var start=new ProcessStartInfo(smi,"--query-gpu=enforced.power.limit,power.max_limit,power.draw,utilization.gpu,temperature.gpu --format=csv,noheader,nounits") {
                UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true
            };
            using(var proc=Process.Start(start)) {
                if(!proc.WaitForExit(5000)) { proc.Kill(); throw new TimeoutException("GPU readback timed out."); }
                string output=proc.StandardOutput.ReadToEnd().Trim();
                if(proc.ExitCode!=0) throw new InvalidOperationException("GPU readback unavailable: "+proc.StandardError.ReadToEnd().Trim());
                if(output.Contains("\n")) throw new InvalidOperationException("Expected one NVIDIA GPU.");
                string[] values=output.Split(',');
                if(values.Length!=5) throw new InvalidOperationException("Incomplete GPU readback.");
                return new Snapshot { Enforced=Number(values[0]),Maximum=Number(values[1]),Draw=Number(values[2]),Usage=Number(values[3]),Temperature=Number(values[4]) };
            }
        }
        static double Number(string value) { return double.Parse(value.Trim(),CultureInfo.InvariantCulture); }
        public Outcome SetCurrent(int watts) {
            object result=Invoke("SetCurrent",watts);
            return new Outcome { Success=(bool)Field(result,"Success"),Message=(string)Field(result,"Message") };
        }
        public void SetBoost(int watts) {
            using(var handle=CreateFile(@"\\.\ATKACPI",0xC0000000,3,IntPtr.Zero,3,0,IntPtr.Zero)) {
                if(handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
                byte[] input=new byte[16],output=new byte[16];
                Buffer.BlockCopy(new uint[]{0x53564544,8,0x001200C0,(uint)watts},0,input,0,16);
                uint count;
                if(!DeviceIoControl(handle,0x22240C,input,16,output,16,out count,IntPtr.Zero)) throw new Win32Exception(Marshal.GetLastWin32Error());
                if(count<4||BitConverter.ToUInt32(output,0)!=1) throw new InvalidOperationException("ASUS rejected the Dynamic Boost request.");
            }
        }
        [DllImport("kernel32.dll",EntryPoint="CreateFileW",CharSet=CharSet.Unicode,SetLastError=true)]
        static extern SafeFileHandle CreateFile(string name,uint access,uint share,IntPtr security,uint creation,uint flags,IntPtr template);
        [DllImport("kernel32.dll",SetLastError=true)]
        static extern bool DeviceIoControl(SafeFileHandle handle,uint code,byte[] input,uint inputSize,byte[] output,uint outputSize,out uint returned,IntPtr overlap);
    }
    public sealed class Controller {
        readonly IDevice device;
        public Controller(IDevice device) { this.device=device; }
        public Capabilities Check() { return device.Check(); }
        public Snapshot Read() { return device.Read(); }
        public Outcome Apply(int watts) {
            Snapshot previous=null; Capabilities cap=null; bool changed=false;
            try {
                cap=device.Check();
                if(!cap.Ready) return Outcome.Fail(cap.Reason);
                previous=device.Read();
                if(watts<cap.Minimum||watts>cap.Maximum||watts>previous.Maximum+0.01) return Outcome.Fail("Choose a limit inside the live supported range.");
                if(previous.Usage>30) return new Outcome { Busy=true,Message="Waiting for GPU usage below 30%. Pause the workload to apply." };
                changed=true;
                device.SetBoost(0);
                Thread.Sleep(250);
                Outcome write=device.SetCurrent(watts);
                if(!write.Success) return Rollback(previous,cap,write.Message);
                for(int i=0;i<3;i++) {
                    Thread.Sleep(250);
                    Snapshot read=device.Read();
                    if(Math.Abs(read.Enforced-watts)>0.51||Math.Abs(read.Maximum-previous.Maximum)>0.51)
                        return Rollback(previous,cap,"The requested limit did not hold.");
                }
                return new Outcome { Success=true,Message=watts+" W applied and verified. Dynamic Boost is off." };
            } catch(Exception e) { return changed&&previous!=null ? Rollback(previous,cap,e.Message) : Outcome.Fail(e.Message); }
        }
        Outcome Rollback(Snapshot before,Capabilities cap,string reason) {
            try {
                int previous=(int)Math.Round(before.Enforced);
                if(cap!=null&&previous>=cap.Minimum&&previous<=cap.Maximum) {
                    Outcome restore=device.SetCurrent(previous);
                    Snapshot current=device.Read();
                    if(restore.Success&&Math.Abs(current.Enforced-before.Enforced)<=0.51)
                        return Outcome.Fail(reason+" Restored "+previous+" W.");
                    return Outcome.Fail(reason+" Restore could not be confirmed; enforced "+current.Enforced.ToString("0.0")+" W.");
                }
                device.SetBoost(25);
                return Outcome.Fail(reason+" Restored 25 W Dynamic Boost; enforced "+device.Read().Enforced.ToString("0.0")+" W.");
            } catch(Exception e) { return Outcome.Fail(reason+" Restore failed: "+e.Message); }
        }
        public Outcome RestoreBoost() {
            try {
                if(device.Read().Usage>30) return new Outcome { Busy=true,Message="Pause the workload before restoring Dynamic Boost." };
                device.SetBoost(25);
                Thread.Sleep(500);
                return new Outcome { Success=true,Message="25 W Dynamic Boost restored. Enforced "+device.Read().Enforced.ToString("0.0")+" W." };
            } catch(Exception e) { return Outcome.Fail(e.Message); }
        }
        public static void Log(string action,int target,Outcome result) {
            try { File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"actions.log"),DateTime.Now.ToString("o")+" "+action+" target="+target+" success="+result.Success+" "+result.Message+Environment.NewLine); } catch { }
        }
    }
}

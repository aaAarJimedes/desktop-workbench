using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Security.Principal;
using System.Threading;
using System.Windows.Forms;

namespace Workbench {
    public static class RuntimeLog {
        static readonly object gate=new object();
        internal static string TestDirectory {get;set;}
        public static string DirectoryPath {get{return TestDirectory??Path.Combine(DataLocation.Root,"logs");}}
        public static void Write(string area,Exception error) {
            // Never persist exception messages, network responses, account names or credentials.
            try {lock(gate){Directory.CreateDirectory(DirectoryPath);string path=Path.Combine(DirectoryPath,"runtime.log");
                if(File.Exists(path)&&new FileInfo(path).Length>262144){string old=path+".1";if(File.Exists(old))File.Delete(old);File.Move(path,old);}
                string trace=error==null?"":new StackTrace(error,false).ToString();
                File.AppendAllText(path,DateTime.UtcNow.ToString("o")+" "+area+" "+(error==null?"":error.GetType().FullName)+Environment.NewLine+trace+Environment.NewLine);
            }}catch{}
        }
        public static void Guard(string area,Action action){try{action();}catch(Exception e){Write(area,e);}}
    }
    public static class Branding {
        public static Icon Icon(int size){using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("Workbench.Icon.ico"))using(var icon=new Icon(stream,size,size))return (Icon)icon.Clone();}
    }
    public sealed class RestartPolicy {
        int failures;
        public int DelaySeconds(TimeSpan lifetime){if(lifetime.TotalMinutes>=5)failures=0;failures++;return (int)Math.Min(300,Math.Pow(2,Math.Min(failures,9)));}
    }
    public static class Resident {
        public const int AlreadyRunning = 42;
        static string Identity {get{return WindowsIdentity.GetCurrent().User.Value;}}
        public static string Name(string suffix){return @"Local\DesktopWorkbench."+Identity+"."+suffix;}
        public static void Signal(string kind){try{using(var e=EventWaitHandle.OpenExisting(Name(kind)))e.Set();}catch(WaitHandleCannotBeOpenedException){} }
        public static int Supervise(string exe,bool hidden,string scope) {
            bool created;
            using(var singleton=new Mutex(true,scope+".supervisor",out created)){
                if(!created){try{using(var show=EventWaitHandle.OpenExisting(scope+".show"))show.Set();}catch{}return 0;}
                using(var stop=new EventWaitHandle(false,EventResetMode.ManualReset,scope+".stop"))
                using(var show=new EventWaitHandle(false,EventResetMode.AutoReset,scope+".show")){
                    stop.Reset();var policy=new RestartPolicy();bool first=true;
                    RuntimeLog.Write("supervisor-start-pid-"+Process.GetCurrentProcess().Id,null);
                    using(var heartbeat=new System.Threading.Timer(_=>RuntimeLog.Write("supervisor-alive",null),null,60000,60000)){
                    while(!stop.WaitOne(0)) {
                        DateTime started=DateTime.UtcNow;
                        try{
                            var info=new ProcessStartInfo(exe,"--worker"+((hidden||!first)?" --tray":"")){UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=Path.GetDirectoryName(exe)};
                            using(var child=Process.Start(info)){
                                RuntimeLog.Write("worker-start-pid-"+child.Id,null);
                                while(!child.WaitForExit(500))if(stop.WaitOne(0)){RuntimeLog.Write("supervisor-stop-requested",null);if(!child.WaitForExit(7000))child.Kill();return 0;}
                                if(stop.WaitOne(0)){RuntimeLog.Write("supervisor-stop-confirmed",null);return 0;}
                                if(child.ExitCode==AlreadyRunning){RuntimeLog.Write("worker-already-running",null);return 0;}
                                RuntimeLog.Write("worker-exited-"+child.ExitCode,null);
                            }
                        }catch(Exception e){RuntimeLog.Write("supervisor-launch",e);}
                        first=false;if(stop.WaitOne(policy.DelaySeconds(DateTime.UtcNow-started)*1000))return 0;
                    }
                    }
                }
            }
            return 0;
        }
        public static string Scope {get{return Name("resident");}}
        public static void Stop(){try{using(var e=EventWaitHandle.OpenExisting(Scope+".stop"))e.Set();}catch(WaitHandleCannotBeOpenedException){} }
        public static void Show(){try{using(var e=EventWaitHandle.OpenExisting(Scope+".show"))e.Set();}catch(WaitHandleCannotBeOpenedException){} }
    }
}

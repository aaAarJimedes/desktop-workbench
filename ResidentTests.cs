using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Workbench;
class ResidentTests {
    static int count;
    static void Check(bool ok,string name){if(!ok)throw new Exception(name);count++;Console.WriteLine("PASS "+name);}
    static void Until(Func<bool> done){var until=DateTime.UtcNow.AddSeconds(20);while(DateTime.UtcNow<until){if(done())return;Thread.Sleep(100);}throw new Exception("fixture timeout");}
    static int Main(string[] args){
        string state=Environment.GetEnvironmentVariable("WB_TEST_STATE"),scope=Environment.GetEnvironmentVariable("WB_TEST_SCOPE");
        if(args.Length>0&&args[0]=="--fixture"){RuntimeLog.TestDirectory=state+".logs";return Resident.Supervise(Process.GetCurrentProcess().MainModule.FileName,false,scope);}
        if(Array.IndexOf(args,"--worker")>=0){
            int runs=File.Exists(state)?Int32.Parse(File.ReadAllText(state))+1:1;File.WriteAllText(state,runs.ToString());
            string mode=Environment.GetEnvironmentVariable("WB_TEST_MODE");
            if(mode=="clean"){using(var stop=EventWaitHandle.OpenExisting(scope+".stop"))stop.Set();return 0;}
            if(runs==1)return mode=="zero"?0:7;
            using(var stop=EventWaitHandle.OpenExisting(scope+".stop"))using(var show=EventWaitHandle.OpenExisting(scope+".show")){
                while(!stop.WaitOne(100))if(show.WaitOne(0))File.WriteAllText(state+".shown","yes");
            }return 0;
        }
        if(args.Length>0)throw new ArgumentException("Unknown test command");
        string root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"resident-test-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try{
            var policy=new RestartPolicy();Check(policy.DelaySeconds(TimeSpan.Zero)==2,"first crash delayed two seconds");int delay=0;for(int i=0;i<20;i++)delay=policy.DelaySeconds(TimeSpan.Zero);Check(delay==300,"crash loop backoff capped at five minutes");Check(policy.DelaySeconds(TimeSpan.FromMinutes(6))==2,"stable runtime resets backoff");
            RuntimeLog.TestDirectory=root;RuntimeLog.Guard("test-guard",()=>{throw new IOException("PRIVATE-SENTINEL-DO-NOT-LOG");});string log=File.ReadAllText(Path.Combine(root,"runtime.log"));Check(log.Contains("System.IO.IOException")&&!log.Contains("PRIVATE-SENTINEL"),"callback survives and excludes exception message");
            File.WriteAllText(Path.Combine(root,"runtime.log"),new string('x',262145));RuntimeLog.Write("rotation",null);Check(new FileInfo(Path.Combine(root,"runtime.log")).Length<1024&&File.Exists(Path.Combine(root,"runtime.log.1")),"diagnostics rotate with bounded storage");
            foreach(string mode in new[]{"crash","zero","clean"}){
                string file=Path.Combine(root,mode),key=@"Local\DesktopWorkbench.Tests."+Guid.NewGuid().ToString("N");
                var info=new ProcessStartInfo(Process.GetCurrentProcess().MainModule.FileName,"--fixture"){UseShellExecute=false,CreateNoWindow=true};Environment.SetEnvironmentVariable("WB_TEST_STATE",file);Environment.SetEnvironmentVariable("WB_TEST_SCOPE",key);Environment.SetEnvironmentVariable("WB_TEST_MODE",mode);
                using(var fixture=Process.Start(info)){
                    try{
                        if(mode=="clean"){Check(fixture.WaitForExit(15000)&&fixture.ExitCode==0&&File.ReadAllText(file)=="1","explicit stop is never restarted");continue;}
                        Until(()=>File.Exists(file)&&File.ReadAllText(file)=="2");Check(!fixture.HasExited,"supervisor recovers worker: "+mode);
                        using(var duplicate=Process.Start(info))Check(duplicate.WaitForExit(5000)&&duplicate.ExitCode==0,"second launcher does not duplicate workers");
                        Until(()=>File.Exists(file+".shown"));Check(true,"second launcher signals existing window");
                        using(var stop=EventWaitHandle.OpenExisting(key+".stop"))stop.Set();Check(fixture.WaitForExit(15000)&&fixture.ExitCode==0,"intentional stop shuts down guardian and worker");Check(File.ReadAllText(file)=="2","intentional stop stays stopped");
                    }finally{if(!fixture.HasExited){try{using(var stop=EventWaitHandle.OpenExisting(key+".stop"))stop.Set();}catch{}if(!fixture.WaitForExit(8000))fixture.Kill();}}
                }
            }
            Console.WriteLine(count+" residency checks passed");return 0;
        }catch(Exception e){Console.WriteLine("FAIL "+e.Message);return 1;}
        finally{RuntimeLog.TestDirectory=null;foreach(var file in Directory.GetFiles(root,"*",SearchOption.AllDirectories))File.Delete(file);foreach(var dir in Directory.GetDirectories(root))Directory.Delete(dir);Directory.Delete(root);}
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Workbench;
class MemoryTests {
    static int count;
    static void Check(bool ok,string name){if(!ok)throw new Exception(name);count++;Console.WriteLine("PASS "+name);}
    static MemoryProcess P(int id,int parent=0,string name="background.exe"){return new MemoryProcess{Id=id,Parent=parent,Name=name,Safe=true,Created=1,WorkingSet=100L*1024*1024};}
    class Fake : IMemoryBackend {
        public int SelfId{get{return 100;}} public int Focus=200;public int ForegroundId{get{return Focus;}}
        public void ObserveForeground(){}
        public List<MemoryProcess> Items=new List<MemoryProcess>{P(100,0,"workbench.exe"),P(200,0,"editor.exe"),P(300),P(301,0,"keep.exe")};
        public List<int> Trimmed=new List<int>();public bool Fail,Denied,CancelOnWait,SwitchFocus;public ManualResetEvent Block;public int StatusReads;public CancellationTokenSource Source;
        public MemoryStatus Status(){return new MemoryStatus{Total=8L*1024*1024*1024,Available=++StatusReads==1?1000000000:900000000};}
        public List<MemoryProcess> Processes(){return Items;}
        public bool Trim(MemoryProcess p){if(Fail)throw new IOException("private detail");Trimmed.Add(p.Id);if(SwitchFocus)Focus=301;return !Denied;}
        public void Wait(int ms,CancellationToken token){if(ms==1200)Thread.Sleep(1);if(CancelOnWait){Source.Cancel();token.ThrowIfCancellationRequested();}if(Block!=null)WaitHandle.WaitAny(new[]{Block,token.WaitHandle});token.ThrowIfCancellationRequested();}
    }
    static void Until(Func<bool> done){var until=DateTime.UtcNow.AddSeconds(12);while(true){try{if(done())return;}catch(IOException){}if(DateTime.UtcNow>=until)throw new Exception("timeout");Thread.Sleep(30);}}
    static int Fixture(string root){
        byte[] data=new byte[96*1024*1024];for(int i=0;i<data.Length;i+=4096)data[i]=123;File.WriteAllText(Path.Combine(root,"ready"),"ready");
        while(!File.Exists(Path.Combine(root,"stop"))){File.WriteAllText(Path.Combine(root,"heartbeat"),DateTime.UtcNow.Ticks.ToString());if(File.Exists(Path.Combine(root,"verify"))){for(int i=0;i<data.Length;i+=4096)if(data[i]!=123)return 9;File.WriteAllText(Path.Combine(root,"verified"),"intact");}Thread.Sleep(100);}GC.KeepAlive(data);return 0;
    }
    [STAThread] static int Main(string[] args) {
        if(args.Length==2&&args[0]=="--fixture")return Fixture(args[1]);if(args.Length>0)return 2;
        string root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"memory-test-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);RuntimeLog.TestDirectory=root;
        try {
            var before=P(1);var now=P(1);var ids=new HashSet<int>();var excluded=new HashSet<string>();
            Check(MemoryCleanup.Eligible(before,now,1,ids,excluded),"idle background is eligible");
            now.Cpu=500000;Check(!MemoryCleanup.Eligible(before,now,1,ids,excluded),"busy CPU is protected");now.Cpu=0;
            now.Io=2000000;Check(!MemoryCleanup.Eligible(before,now,1,ids,excluded),"busy I/O is protected");now.Io=0;
            now.Created=2;Check(!MemoryCleanup.Eligible(before,now,1,ids,excluded),"reused PID is protected");now.Created=1;
            now.Safe=false;Check(!MemoryCleanup.Eligible(before,now,1,ids,excluded),"system other-owner and inaccessible processes are excluded");now.Safe=true;
            now.WorkingSet=1024;Check(!MemoryCleanup.Eligible(before,now,1,ids,excluded),"small processes are left alone");now.WorkingSet=100L*1024*1024;
            ids.Add(1);Check(!MemoryCleanup.Eligible(before,now,1,ids,excluded),"foreground identity is protected");ids.Clear();excluded.Add("background");Check(!MemoryCleanup.Eligible(before,now,1,ids,excluded),"saved exclusion is honored");
            var family=MemoryCleanup.Protected(new List<MemoryProcess>{P(1,0,"browser.exe"),P(2,1,"browser.exe"),P(3,1,"helper.exe"),P(10,0,"workbench.exe"),P(11,10,"ssh.exe"),P(20,0,"other.exe")},10,2);
            Check(family.Contains(1)&&family.Contains(2)&&family.Contains(3),"foreground app siblings parent and descendants protected");Check(family.Contains(10)&&family.Contains(11)&&!family.Contains(20),"workbench and SSH protected without protecting unrelated apps");
            var fake=new Fake();var r=MemoryCleanup.Run(fake,new[]{"KEEP.EXE"},CancellationToken.None,_=>{});Check(fake.Trimmed.SequenceEqual(new[]{300}),"cleanup only touches allowed background processes");Check(r.After.Available<r.Before.Available&&r.Summary.Contains("减少"),"reports negative available-memory change honestly");
            fake=new Fake{Denied=true};r=MemoryCleanup.Run(fake,new string[0],CancellationToken.None,_=>{});Check(r.Trimmed==0&&r.Unavailable==2,"denied or exited process does not count as cleaned");
            fake=new Fake{SwitchFocus=true};r=MemoryCleanup.Run(fake,new string[0],CancellationToken.None,_=>{});Check(fake.Trimmed.SequenceEqual(new[]{300}),"foreground switches are protected during cleanup");
            using(var source=new CancellationTokenSource()){fake=new Fake{Source=source,CancelOnWait=true};r=MemoryCleanup.Run(fake,new string[0],source.Token,_=>{});Check(r.Cancelled&&fake.Trimmed.Count==0,"cancel during sampling leaves processes untouched");}
            var clock=DateTime.UtcNow;fake=new Fake{Block=new ManualResetEvent(false)};
            using(var cleaner=new MemoryCleaner(fake,()=>clock)){Task<MemoryCleanupResult> job,other;Check(cleaner.TryStart(new string[0],out job)&&cleaner.Busy,"cleanup starts asynchronously");Check(!cleaner.TryStart(new string[0],out other),"tray and page cannot start concurrent cleanup");cleaner.Cancel();Check(job.Wait(5000)&&job.Result.Cancelled,"cancel wakes background wait");Check(!cleaner.TryStart(new string[0],out other)&&cleaner.Cooldown==60,"cooldown prevents immediate repeat");clock=clock.AddSeconds(61);fake.Block.Set();Check(cleaner.TryStart(new string[0],out other)&&other.Wait(5000),"cleanup available again after cooldown");}fake.Block.Dispose();
            fake=new Fake{Fail=true};using(var cleaner=new MemoryCleaner(fake)){Task<MemoryCleanupResult> job;cleaner.TryStart(new string[0],out job);try{job.Wait();}catch(AggregateException){}Check(!cleaner.Busy&&cleaner.Cooldown>0,"native failure releases busy state with cooldown");}
            string config=Path.Combine(root,"optimization.json");new OptimizationSettings{ExcludedProcesses=new[]{"blender.exe","javaw.exe"}}.Save(config);Check(OptimizationSettings.Load(config).ExcludedProcesses.Length==2,"exclusions survive save and reload");
            using(var tool=new OptimizationTool(new Fake(),config))using(var view=tool.CreateView()){Check(tool.Title=="系统优化"&&tool.CanClean,"third tool view exposes manual cleanup");}
            fake=new Fake{Block=new ManualResetEvent(false)};using(var tool=new OptimizationTool(fake,config))using(var view=tool.CreateView())using(var menu=new ContextMenuStrip()){
                bool opened=false,completed=false;tool.AttachTrayMenu(menu,()=>opened=true);tool.Completed+=message=>completed=true;
                Check(menu.Items[0].Text=="系统优化"&&menu.Items[1].Text=="清理内存","real tray wiring exposes direct cleanup and page entries");
                menu.Items[0].PerformClick();Check(opened,"tray page entry opens the optimization page");
                menu.Items[1].PerformClick();Check(!tool.CanClean&&tool.TrayText.Contains("正在"),"tray click starts the same asynchronous cleanup task");
                menu.Items[1].PerformClick();fake.Block.Set();Until(()=>{Application.DoEvents();return completed;});
                Check(fake.Trimmed.SequenceEqual(new[]{300,301}),"repeated tray clicks run only one cleanup");Check(!tool.CanClean&&tool.TrayText.Contains("秒"),"tray enters cooldown after completion");
            }fake.Block.Dispose();
            File.WriteAllText(config,"corrupt");using(var tool=new OptimizationTool(new Fake(),config))using(var view=tool.CreateView()){Check(!tool.CanClean&&File.ReadAllText(config)=="corrupt","invalid exclusions disable cleanup without overwriting data");}
            // Real OS check only touches an owned fixture, never the user's applications.
            if(Process.GetCurrentProcess().SessionId!=0){
                var start=new ProcessStartInfo(Process.GetCurrentProcess().MainModule.FileName,"--fixture \""+root+"\""){UseShellExecute=false,CreateNoWindow=true};
                using(var child=Process.Start(start))try{Until(()=>File.Exists(Path.Combine(root,"ready")));var backend=new WindowsMemoryBackend();var p=backend.Processes().Find(x=>x.Id==child.Id);Check(p!=null&&p.Safe&&p.WorkingSet>64L*1024*1024,"native fixture has reclaimable working set");Check(backend.Trim(p),"Windows EmptyWorkingSet succeeds on owned fixture");var trimmed=backend.Processes().Find(x=>x.Id==child.Id);Check(trimmed!=null&&trimmed.WorkingSet<p.WorkingSet,"native working set actually decreases");string heartbeat=File.ReadAllText(Path.Combine(root,"heartbeat"));Until(()=>File.ReadAllText(Path.Combine(root,"heartbeat"))!=heartbeat);Check(!child.HasExited,"trimmed program continues running");File.WriteAllText(Path.Combine(root,"verify"),"verify");Until(()=>File.Exists(Path.Combine(root,"verified")));Check(File.ReadAllText(Path.Combine(root,"verified"))=="intact","trimmed process memory contents remain intact");}
                finally{File.WriteAllText(Path.Combine(root,"stop"),"stop");if(!child.WaitForExit(5000))child.Kill();}
            }else Console.WriteLine("SKIP native fixture in service session; run on interactive Windows desktop");
            Console.WriteLine(count+" memory optimization checks passed");return 0;
        }catch(Exception error){Console.WriteLine("FAIL "+error.Message);return 1;}
        finally{RuntimeLog.TestDirectory=null;Directory.Delete(root,true);}
    }
}

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;

namespace Workbench {
    public sealed class MemoryStatus {
        public long Total,Available;
        public static string Size(long bytes){return (bytes/1073741824.0).ToString("0.00")+" GB";}
    }
    public sealed class MemoryProcess {
        public int Id,Parent;
        public string Name="";
        public bool Safe;
        public long Created,Cpu,Io,WorkingSet;
    }
    public interface IMemoryBackend {
        int SelfId {get;}
        int ForegroundId {get;}
        void ObserveForeground();
        MemoryStatus Status();
        List<MemoryProcess> Processes();
        bool Trim(MemoryProcess expected);
        void Wait(int milliseconds,CancellationToken token);
    }
    public sealed class MemoryCleanupResult {
        public MemoryStatus Before,After;
        public int Examined,Trimmed,Skipped,Unavailable;
        public bool Cancelled;
        public TimeSpan Elapsed;
        public string Summary {
            get {
                long delta=After.Available-Before.Available;
                return (Cancelled?"已停止清理":"清理完成")+" · 已回收 "+Trimmed+" 个后台进程的工作集\n可用内存 "+MemoryStatus.Size(Before.Available)+" → "+MemoryStatus.Size(After.Available)+
                    "（"+(delta>=0?"增加 ":"减少 ")+MemoryStatus.Size(Math.Abs(delta))+"）\n跳过 "+Skipped+" 个；已退出或无权限 "+Unavailable+" 个 · "+Elapsed.TotalSeconds.ToString("0.0")+" 秒";
            }
        }
    }
    public static class MemoryCleanup {
        public const long MinimumWorkingSet=64L*1024*1024;
        public static string Normalize(string name){return (name??"").Trim().ToLowerInvariant().Replace(".exe","");}
        // Protect the foreground application family, including browser child processes.
        public static HashSet<int> Protected(List<MemoryProcess> all,int self,int foreground) {
            var ids=new HashSet<int>();var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach(var p in all)if(p.Id==self||p.Id==foreground){ids.Add(p.Id);names.Add(p.Name);}
            ids.Add(self);ids.Add(foreground);
            bool changed;
            do {changed=false;foreach(var p in all){
                if(names.Contains(p.Name)||ids.Contains(p.Parent))changed|=ids.Add(p.Id);
                // Walk through same-application parents without including Explorer and all its children.
                if(ids.Contains(p.Id)){var parent=all.Find(x=>x.Id==p.Parent);if(parent!=null&&String.Equals(parent.Name,p.Name,StringComparison.OrdinalIgnoreCase))changed|=ids.Add(parent.Id);}
            }}while(changed);
            return ids;
        }
        public static bool Eligible(MemoryProcess before,MemoryProcess now,double seconds,HashSet<int> protectedIds,HashSet<string> excluded) {
            if(!before.Safe||!now.Safe||before.Created!=now.Created||before.Id!=now.Id||seconds<=0||now.WorkingSet<MinimumWorkingSet||protectedIds.Contains(now.Id)||excluded.Contains(Normalize(now.Name)))return false;
            long cpu=now.Cpu-before.Cpu,io=now.Io-before.Io;
            return cpu>=0&&io>=0&&cpu/10000000.0/seconds<=0.02&&io/seconds<=1024*1024;
        }
        public static MemoryCleanupResult Run(IMemoryBackend backend,IEnumerable<string> exclusions,CancellationToken token,Action<string> progress) {
            var result=new MemoryCleanupResult{Before=backend.Status()};var watch=Stopwatch.StartNew();
            var excluded=new HashSet<string>(exclusions.Select(Normalize),StringComparer.OrdinalIgnoreCase);
            try {
                progress("正在观察后台活动…");token.ThrowIfCancellationRequested();
                var first=backend.Processes();var protectedIds=Protected(first,backend.SelfId,backend.ForegroundId);
                var baseline=first.ToDictionary(p=>p.Id);var sampling=Stopwatch.StartNew();backend.Wait(1200,token);
                var current=backend.Processes();double seconds=sampling.Elapsed.TotalSeconds;
                protectedIds.UnionWith(Protected(current,backend.SelfId,backend.ForegroundId));
                foreach(var now in current.OrderByDescending(p=>p.WorkingSet)) {
                    token.ThrowIfCancellationRequested();result.Examined++;
                    // Recheck the foreground before each trim; users may switch applications mid-run.
                    protectedIds.UnionWith(Protected(current,backend.SelfId,backend.ForegroundId));
                    MemoryProcess before;
                    if(!baseline.TryGetValue(now.Id,out before)||!Eligible(before,now,seconds,protectedIds,excluded)){result.Skipped++;continue;}
                    progress("正在回收空闲后台内存… 已处理 "+result.Trimmed+" 个");
                    if(backend.Trim(now))result.Trimmed++;else result.Unavailable++;
                    backend.Wait(25,token);
                }
                progress("正在测量清理后的可用内存…");backend.Wait(1500,token);
            }catch(OperationCanceledException){result.Cancelled=true;}
            result.After=backend.Status();result.Elapsed=watch.Elapsed;return result;
        }
    }
    public sealed class MemoryCleaner : IDisposable {
        readonly object gate=new object();readonly IMemoryBackend backend;readonly Func<DateTime> clock;
        CancellationTokenSource cancellation;DateTime finished=DateTime.MinValue;bool disposed;
        public volatile string Progress="";
        public MemoryCleaner(IMemoryBackend backend):this(backend,()=>DateTime.UtcNow){}
        internal MemoryCleaner(IMemoryBackend backend,Func<DateTime> clock){this.backend=backend;this.clock=clock;}
        public bool Busy {get{lock(gate)return cancellation!=null;}}
        public int Cooldown {get{lock(gate)return (int)Math.Max(0,Math.Min(60,Math.Ceiling(60-(clock()-finished).TotalSeconds)));}}
        public bool TryStart(string[] excluded,out Task<MemoryCleanupResult> task) {
            lock(gate){
                task=null;if(disposed||cancellation!=null||Cooldown>0)return false;
                var copy=(string[])excluded.Clone();var source=new CancellationTokenSource();cancellation=source;
                task=Task.Run(()=>{try{return MemoryCleanup.Run(backend,copy,source.Token,s=>Progress=s);}finally{lock(gate){finished=clock();cancellation=null;source.Dispose();}}});return true;
            }
        }
        public void Cancel(){lock(gate){if(cancellation!=null)cancellation.Cancel();}}
        public void Dispose(){lock(gate){disposed=true;if(cancellation!=null)cancellation.Cancel();}}
    }
    public sealed class WindowsMemoryBackend : IMemoryBackend {
        // Only QUERY_LIMITED_INFORMATION and SET_QUOTA. Never terminate, suspend, or write memory.
        const uint Query=0x1000,SetQuota=0x0100;
        readonly string owner=WindowsIdentity.GetCurrent().User.Value;
        readonly string windows=Environment.GetFolderPath(Environment.SpecialFolder.Windows)+Path.DirectorySeparatorChar;
        readonly int self=Process.GetCurrentProcess().Id,session=Process.GetCurrentProcess().SessionId;
        volatile int lastForeground;
        public int SelfId {get{return self;}}
        public int ForegroundId {get{ObserveForeground();return lastForeground>0?lastForeground:self;}}
        public void ObserveForeground(){uint id;GetWindowThreadProcessId(GetForegroundWindow(),out id);if(id==0||id==self)return;
            try{using(var process=Process.GetProcessById((int)id)){string name=process.ProcessName;
                if(new[]{"explorer","SearchHost","StartMenuExperienceHost","ShellExperienceHost","dwm"}.Contains(name,StringComparer.OrdinalIgnoreCase))return;
                lastForeground=(int)id;
            }}catch(ArgumentException){}catch(InvalidOperationException){}catch(Win32Exception){}
        }
        [StructLayout(LayoutKind.Sequential)] struct MemoryInfo {public uint Length,Load;public ulong Total,Available,TotalPage,AvailablePage,TotalVirtual,AvailableVirtual,Extended;}
        [StructLayout(LayoutKind.Sequential)] struct Counters {public uint Size,Faults;public UIntPtr Peak,Working,PeakPaged,Paged,PeakNonPaged,NonPaged,Page,PeakPage;}
        [StructLayout(LayoutKind.Sequential)] struct IoCounters {public ulong ReadOps,WriteOps,OtherOps,ReadBytes,WriteBytes,OtherBytes;}
        [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] struct Entry {public uint Size,Usage,Id;public UIntPtr Heap;public uint Module,Threads,Parent;public int Priority;public uint Flags;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=260)]public string Name;}
        [DllImport("kernel32.dll",SetLastError=true)] static extern bool GlobalMemoryStatusEx(ref MemoryInfo info);
        [DllImport("kernel32.dll",SetLastError=true)] static extern IntPtr OpenProcess(uint access,bool inherit,int id);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
        [DllImport("kernel32.dll")] static extern bool ProcessIdToSessionId(uint id,out uint session);
        [DllImport("kernel32.dll")] static extern bool IsProcessCritical(IntPtr handle,out bool critical);
        [DllImport("kernel32.dll")] static extern bool GetProcessTimes(IntPtr handle,out long created,out long exit,out long kernel,out long user);
        [DllImport("kernel32.dll")] static extern bool GetProcessIoCounters(IntPtr handle,out IoCounters io);
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] static extern bool QueryFullProcessImageName(IntPtr handle,uint flags,System.Text.StringBuilder path,ref int size);
        [DllImport("psapi.dll",SetLastError=true)] static extern bool GetProcessMemoryInfo(IntPtr handle,ref Counters counters,uint size);
        [DllImport("psapi.dll",SetLastError=true)] static extern bool EmptyWorkingSet(IntPtr handle);
        [DllImport("advapi32.dll")] static extern bool OpenProcessToken(IntPtr process,uint access,out IntPtr token);
        [DllImport("advapi32.dll")] static extern bool GetTokenInformation(IntPtr token,int kind,IntPtr value,int length,out int needed);
        [DllImport("kernel32.dll",SetLastError=true)] static extern IntPtr CreateToolhelp32Snapshot(uint flags,uint id);
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] static extern bool Process32First(IntPtr snapshot,ref Entry entry);
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] static extern bool Process32Next(IntPtr snapshot,ref Entry entry);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window,out uint id);
        public MemoryStatus Status(){var info=new MemoryInfo{Length=(uint)Marshal.SizeOf(typeof(MemoryInfo))};if(!GlobalMemoryStatusEx(ref info))throw new Win32Exception();return new MemoryStatus{Total=(long)info.Total,Available=(long)info.Available};}
        bool SameOwner(IntPtr process) {
            IntPtr token;if(!OpenProcessToken(process,8,out token))return false;
            try{int needed;GetTokenInformation(token,1,IntPtr.Zero,0,out needed);if(needed<=0)return false;IntPtr data=Marshal.AllocHGlobal(needed);
                try{return GetTokenInformation(token,1,data,needed,out needed)&&new SecurityIdentifier(Marshal.ReadIntPtr(data)).Value==owner;}finally{Marshal.FreeHGlobal(data);}
            }finally{CloseHandle(token);}
        }
        bool Read(IntPtr handle,MemoryProcess p) {
            uint sid;bool critical;long created,exit,kernel,user;IoCounters io;var counts=new Counters{Size=(uint)Marshal.SizeOf(typeof(Counters))};
            if(!ProcessIdToSessionId((uint)p.Id,out sid)||sid==0||sid!=session||!IsProcessCritical(handle,out critical)||critical||!SameOwner(handle))return false;
            int length=32768;var path=new System.Text.StringBuilder(length);
            if(!QueryFullProcessImageName(handle,0,path,ref length)||path.ToString().StartsWith(windows,StringComparison.OrdinalIgnoreCase))return false;
            if(!GetProcessTimes(handle,out created,out exit,out kernel,out user)||!GetProcessIoCounters(handle,out io)||!GetProcessMemoryInfo(handle,ref counts,counts.Size))return false;
            p.Created=created;p.Cpu=kernel+user;p.Io=checked((long)(io.ReadBytes+io.WriteBytes));p.WorkingSet=(long)counts.Working.ToUInt64();p.Safe=true;return true;
        }
        public List<MemoryProcess> Processes() {
            var result=new List<MemoryProcess>();IntPtr snapshot=CreateToolhelp32Snapshot(2,0);if(snapshot==new IntPtr(-1))throw new Win32Exception();
            try{var entry=new Entry{Size=(uint)Marshal.SizeOf(typeof(Entry))};if(!Process32First(snapshot,ref entry))throw new Win32Exception();
                do {var p=new MemoryProcess{Id=(int)entry.Id,Parent=(int)entry.Parent,Name=entry.Name};IntPtr handle=OpenProcess(Query,false,p.Id);
                    if(handle!=IntPtr.Zero)try{Read(handle,p);}finally{CloseHandle(handle);}result.Add(p);
                }while(Process32Next(snapshot,ref entry));
            }finally{CloseHandle(snapshot);}return result;
        }
        public bool Trim(MemoryProcess expected) {
            IntPtr handle=OpenProcess(Query|SetQuota,false,expected.Id);if(handle==IntPtr.Zero)return false;
            try{var now=new MemoryProcess{Id=expected.Id};
                // Keep the identity and activity checks on the same handle as the trim (PID reuse safe).
                return Read(handle,now)&&now.Created==expected.Created&&now.Cpu>=expected.Cpu&&now.Cpu-expected.Cpu<=200000&&now.Io>=expected.Io&&now.Io-expected.Io<=1048576&&EmptyWorkingSet(handle);
            }finally{CloseHandle(handle);}
        }
        public void Wait(int milliseconds,CancellationToken token){if(token.WaitHandle.WaitOne(milliseconds))token.ThrowIfCancellationRequested();}
    }
}

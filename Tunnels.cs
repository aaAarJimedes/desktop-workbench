using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace Workbench {
    public sealed class TunnelProfile {
        public string Id = Guid.NewGuid().ToString("N"), Name = "新连接", Host = "", User = "", IdentityFile = "";
        public int SshPort = 22, RemotePort = 18080, LocalPort = 7897;
        public bool AutoStart;
        public TunnelProfile Copy() { return (TunnelProfile)MemberwiseClone(); }
        public void Validate() {
            if (!Regex.IsMatch(Id ?? "", @"\A[a-zA-Z0-9_-]{1,64}\z")) throw new ArgumentException("连接标识无效");
            if (String.IsNullOrWhiteSpace(Name) || Name.Length > 80) throw new ArgumentException("请填写连接名称（最多 80 字）");
            if (String.IsNullOrWhiteSpace(Host) || Host.StartsWith("-") || Uri.CheckHostName(Host) == UriHostNameType.Unknown) throw new ArgumentException("请填写服务器 IP 或域名");
            if (!Regex.IsMatch(User ?? "", @"\A[a-zA-Z0-9_][a-zA-Z0-9_.-]{0,63}\z")) throw new ArgumentException("SSH 用户名无效");
            if (SshPort < 1 || SshPort > 65535 || RemotePort < 1024 || RemotePort > 65535 || LocalPort < 1 || LocalPort > 65535) throw new ArgumentException("端口超出范围；服务器代理端口须为 1024–65535");
            if (IdentityFile == null || IdentityFile.IndexOfAny(new char[]{'\r','\n','\0','"'}) >= 0) throw new ArgumentException("密钥路径无效");
            if (IdentityFile.Length > 0 && !Path.IsPathRooted(IdentityFile)) throw new ArgumentException("密钥路径须为绝对路径；也可留空使用 ssh-agent");
        }
        public string Endpoint { get { return Host.ToLowerInvariant()+":"+SshPort+":"+RemotePort; } }
    }
    public sealed class TunnelConfig {
        public int Version = 1;
        public List<TunnelProfile> Profiles = new List<TunnelProfile>();
        public void Validate() {
            if (Version != 1 || Profiles == null || Profiles.Count > 50) throw new InvalidDataException("隧道配置版本或连接数无效");
            var ids = new HashSet<string>(); var endpoints = new HashSet<string>();
            foreach (var p in Profiles) {
                if (p == null) throw new InvalidDataException("空连接配置"); p.Validate();
                if (!ids.Add(p.Id) || !endpoints.Add(p.Endpoint)) throw new ArgumentException("同一服务器端口只能配置一条连接");
            }
        }
        public static string DefaultPath { get { return Path.Combine(Storage.Root,"tunnels.json"); } }
        public static TunnelConfig Load(string path) {
            if (!File.Exists(path)) return new TunnelConfig();
            var c = new JavaScriptSerializer().Deserialize<TunnelConfig>(File.ReadAllText(path));
            if (c == null) throw new InvalidDataException("隧道配置为空"); c.Validate(); return c;
        }
        public void Save(string path) {
            Validate(); Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            string stage = path+"."+Guid.NewGuid().ToString("N")+".tmp";
            try {
                File.WriteAllText(stage,new JavaScriptSerializer().Serialize(this),new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(stage,path,null); else File.Move(stage,path);
            } finally { if (File.Exists(stage)) File.Delete(stage); }
        }
    }
    public enum TunnelFailure { None, Authentication, HostKey, PortBusy, Transport }
    public static class TunnelCommand {
        public static string SshPath { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),@"OpenSSH\ssh.exe"); } }
        // Win32 argv quoting; never invoke cmd.exe or interpolate a local shell.
        public static string Quote(string value) {
            var b=new StringBuilder("\""); int slashes=0;
            foreach(char c in value) {
                if(c=='\\'){slashes++;continue;}
                if(c=='"'){b.Append('\\',slashes*2+1);b.Append(c);slashes=0;continue;}
                b.Append('\\',slashes);slashes=0;b.Append(c);
            }
            b.Append('\\',slashes*2);return b.Append('"').ToString();
        }
        public static string Arguments(TunnelProfile p,bool forward) {
            p.Validate();
            var a=new List<string>{"-F","none","-T","-o","BatchMode=yes","-o","StrictHostKeyChecking=yes","-o","UpdateHostKeys=no","-o","ConnectTimeout=10","-o","ConnectionAttempts=1","-o","ServerAliveInterval=15","-o","ServerAliveCountMax=3","-o","ForwardAgent=no","-o","ForwardX11=no","-o","ControlMaster=no","-o","ControlPath=none","-p",p.SshPort.ToString(),"-l",p.User};
            if(p.IdentityFile.Length>0){a.Add("-i");a.Add(p.IdentityFile);a.Add("-o");a.Add("IdentitiesOnly=yes");}
            if(forward) a.AddRange(new string[]{"-v","-N","-n","-o","ExitOnForwardFailure=yes","-R","127.0.0.1:"+p.RemotePort+":127.0.0.1:"+p.LocalPort});
            a.Add(p.Host);
            if(!forward)a.AddRange(new string[]{"sh","-s"});
            return String.Join(" ",a.ConvertAll(Quote).ToArray());
        }
        public static TunnelFailure Failure(string line) {
            if(line==null)return TunnelFailure.None;
            string s=line.ToLowerInvariant();
            if(s.Contains("host key verification failed")||s.Contains("remote host identification has changed")||s.Contains("no matching host key"))return TunnelFailure.HostKey;
            if(s.Contains("permission denied")||s.Contains("sign_and_send_pubkey: signing failed")||s.Contains("load key"))return TunnelFailure.Authentication;
            if(s.Contains("remote port forwarding failed")||s.Contains("remote forward failure"))return TunnelFailure.PortBusy;
            return TunnelFailure.None;
        }
        public static bool ForwardConfirmed(string line,int port) {
            return line != null && line.Contains("remote forward success for: listen 127.0.0.1:"+port+",");
        }
        public static bool LocalProxy(int port) {
            try { using(var tcp=new TcpClient()) {var task=tcp.ConnectAsync(IPAddress.Loopback,port);return task.Wait(500)&&tcp.Connected;} } catch{return false;}
        }
    }
    // Each owned SSH process has a kill-on-close job, so a crashed workbench cannot leave an orphan tunnel.
    sealed class ProcessJob : IDisposable {
        IntPtr handle;
        [StructLayout(LayoutKind.Sequential)] struct Basic { public long ProcessTime,JobTime; public uint Flags; public UIntPtr Min,Max; public uint Active; public UIntPtr Affinity; public uint Priority,Scheduling; }
        [StructLayout(LayoutKind.Sequential)] struct Io { public ulong ReadOps,WriteOps,OtherOps,ReadBytes,WriteBytes,OtherBytes; }
        [StructLayout(LayoutKind.Sequential)] struct Extended { public Basic Basic; public Io Io; public UIntPtr ProcessMemory,JobMemory,PeakProcess,PeakJob; }
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern IntPtr CreateJobObject(IntPtr attr,string name);
        [DllImport("kernel32.dll",SetLastError=true)] static extern bool SetInformationJobObject(IntPtr job,int cls,ref Extended info,uint length);
        [DllImport("kernel32.dll",SetLastError=true)] static extern bool AssignProcessToJobObject(IntPtr job,IntPtr process);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
        public ProcessJob(Process process) {
            handle=CreateJobObject(IntPtr.Zero,null);var e=new Extended();e.Basic.Flags=0x2000;
            if(handle==IntPtr.Zero||!SetInformationJobObject(handle,9,ref e,(uint)Marshal.SizeOf(typeof(Extended)))||!AssignProcessToJobObject(handle,process.Handle)) {
                Dispose();try{process.Kill();}catch{}throw new IOException("无法托管 SSH 子进程");
            }
        }
        public void Dispose(){if(handle!=IntPtr.Zero){CloseHandle(handle);handle=IntPtr.Zero;}}
    }
    public interface ITunnelProcess : IDisposable {
        bool Alive { get; } bool Confirmed { get; } TunnelFailure Failure { get; } int Id { get; }
    }
    public sealed class SshTunnelProcess : ITunnelProcess {
        readonly Process process;readonly ProcessJob job;
        volatile bool confirmed;volatile TunnelFailure failure;
        public bool Alive {get{try{return !process.HasExited;}catch{return false;}}}
        public bool Confirmed {get{return confirmed;}}
        public TunnelFailure Failure {get{return failure;}}
        public int Id {get{return process.Id;}}
        public SshTunnelProcess(TunnelProfile p) {
            var psi=new ProcessStartInfo(TunnelCommand.SshPath,TunnelCommand.Arguments(p,true)){UseShellExecute=false,CreateNoWindow=true,RedirectStandardError=true,RedirectStandardOutput=true};
            process=new Process{StartInfo=psi};
            process.ErrorDataReceived+=(s,e)=>{var f=TunnelCommand.Failure(e.Data);if(f!=TunnelFailure.None)failure=f;if(TunnelCommand.ForwardConfirmed(e.Data,p.RemotePort))confirmed=true;};
            process.OutputDataReceived+=(s,e)=>{};
            try {process.Start();job=new ProcessJob(process);process.BeginErrorReadLine();process.BeginOutputReadLine();}
            catch {try{if(!process.HasExited)process.Kill();}catch{}process.Dispose();throw;}
        }
        public void Dispose(){job.Dispose();try{if(!process.HasExited)process.Kill();process.WaitForExit(2000);}catch{}process.Dispose();}
    }
    public sealed class TunnelSnapshot {
        public string Status, Detail;public bool Desired,Connected;public int ProcessId;public DateTime RetryAt;
    }
    public sealed class TunnelSession : IDisposable {
        readonly object gate=new object();readonly TunnelProfile profile;
        readonly Func<TunnelProfile,ITunnelProcess> launch;readonly Func<int,bool> proxy;
        ITunnelProcess child;bool desired,disposed;int failures;DateTime started,retry;
        string status="已停止",detail="点击启动，为服务器提供本机代理出口。";
        public TunnelSession(TunnelProfile p,Func<TunnelProfile,ITunnelProcess> factory,Func<int,bool> probe) {profile=p.Copy();profile.Validate();launch=factory;proxy=probe;}
        public TunnelSession(TunnelProfile p):this(p,x=>new SshTunnelProcess(x),TunnelCommand.LocalProxy){}
        public void Start(){lock(gate){if(disposed)return;if(desired)return;desired=true;failures=0;retry=DateTime.MinValue;status="等待连接";detail="正在准备 SSH 反向隧道。";}}
        public void Stop(){lock(gate){desired=false;Close();status="已停止";detail="已释放本工具建立的隧道。";}}
        void Close(){if(child!=null){child.Dispose();child=null;}}
        void Failed(TunnelFailure f,DateTime now) {
            Close();
            if(f==TunnelFailure.Authentication||f==TunnelFailure.HostKey||f==TunnelFailure.PortBusy) {
                desired=false;status="需要处理";
                detail=f==TunnelFailure.Authentication?"SSH 认证失败：请确认密钥已加入 ssh-agent，并已获服务器授权。":f==TunnelFailure.HostKey?"主机密钥未受信任或发生变化：请先在可信终端核验；工具不会跳过验证。":"远端端口被占用或转发被禁止：请停止相同端口的 MobaXterm 通道，或检查服务器转发权限。";
            } else {failures++;retry=now.AddSeconds(Math.Min(300,5*Math.Pow(2,Math.Min(failures,6))));status="等待重连";detail="SSH 连接中断；将于 "+retry.ToLocalTime().ToString("HH:mm:ss")+" 重试。";}
        }
        public void Tick(DateTime now) {
            lock(gate) {
                if(disposed||!desired)return;
                if(child!=null) {
                    if(child.Failure!=TunnelFailure.None){Failed(child.Failure,now);return;}
                    if(!child.Alive){Failed(TunnelFailure.Transport,now);return;}
                    if(child.Confirmed){if((now-started).TotalSeconds>60)failures=0;status="隧道已建立";detail="服务器回环端口 → 本机代理；可点击外网测试验证出口。";return;}
                    if((now-started).TotalSeconds>25){Failed(TunnelFailure.Transport,now);return;}
                    return;
                }
                if(now<retry)return;
                if(!proxy(profile.LocalPort)){status="等待本机代理";detail="127.0.0.1:"+profile.LocalPort+" 未监听；请启动 Clash 或配置对应代理。";retry=now.AddSeconds(10);return;}
                try {child=launch(profile);started=now;status="正在连接";detail="严格验证服务器身份并建立远端转发。";}
                catch {Failed(TunnelFailure.Transport,now);}
            }
        }
        public TunnelSnapshot Snapshot(){lock(gate){return new TunnelSnapshot{Status=status,Detail=detail,Desired=desired,Connected=child!=null&&child.Alive&&child.Confirmed,ProcessId=child==null?0:child.Id,RetryAt=retry};}}
        public void Dispose(){lock(gate){disposed=true;desired=false;Close();}}
    }
    public sealed class TunnelProbeResult {public string Summary,Details;public bool ExternalOk,LoopbackOnly;}
    public static class TunnelProbe {
        public static string Script(int port) {
            if(port<1024||port>65535)throw new ArgumentOutOfRangeException("port");
            string s="printf 'WB_LISTEN_BEGIN\\n'\ncat /proc/net/tcp /proc/net/tcp6 2>/dev/null\nprintf 'WB_LISTEN_END\\n'\n";
            string[] names={"Example","OpenAI","GitHub"},urls={"https://example.com/","https://api.openai.com/v1/models","https://api.github.com/"};
            for(int i=0;i<names.Length;i++)s+="printf 'WB_"+names[i]+" '\ncurl --proxy http://127.0.0.1:"+port+" --noproxy '' --connect-timeout 5 --max-time 12 --silent --output /dev/null --write-out '%{http_code} %{ssl_verify_result}' "+urls[i]+"\nprintf ' exit=%s\\n' \"$?\"\n";
            return s;
        }
        public static TunnelProbeResult Parse(string output,int port) {
            string hex=port.ToString("X4"); bool loop=false,wild=false;
            var listeners=Regex.Match(output,@"WB_LISTEN_BEGIN\r?\n([\s\S]*?)WB_LISTEN_END");
            foreach(Match m in Regex.Matches(listeners.Groups[1].Value,@"\s([0-9A-Fa-f]+):"+hex+@"\s+\S+\s+0A\s")) {
                string address=m.Groups[1].Value.ToUpperInvariant();
                if(address=="0100007F"||address=="00000000000000000000000001000000")loop=true;else wild=true;
            }
            bool ok=Regex.IsMatch(output,@"WB_Example 200 0 exit=0");var b=new StringBuilder();
            b.AppendLine(loop&&!wild?"远端监听：仅回环地址":wild?"远端监听超出回环地址，请检查 sshd GatewayPorts":"未确认远端回环监听");
            foreach(string name in new[]{"Example","OpenAI","GitHub"}) {
                Match m=Regex.Match(output,"WB_"+name+@" (\d{3}) (\d+) exit=(\d+)");
                if(!m.Success){b.AppendLine(name+"：未取得检测结果（远端需安装 curl）");continue;}
                string code=m.Groups[1].Value;
                b.AppendLine(name+"：HTTP "+code+"，TLS 校验 "+m.Groups[2].Value+"，curl 退出码 "+m.Groups[3].Value+(code=="401"?"（未带凭据；仅验证可达）":code=="403"?"（站点拒绝；检查代理出口）":""));
            }
            return new TunnelProbeResult{ExternalOk=ok,LoopbackOnly=loop&&!wild,Summary=ok?(loop&&!wild?"服务器外网可用":"外网可达，监听范围需检查"):"外网检测未通过",Details=b.ToString()};
        }
        public static TunnelProbeResult Run(TunnelProfile p) {
            var psi=new ProcessStartInfo(TunnelCommand.SshPath,TunnelCommand.Arguments(p,false)){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true};
            using(var process=Process.Start(psi))using(var job=new ProcessJob(process)) {
                Task<string> output=process.StandardOutput.ReadToEndAsync(),error=process.StandardError.ReadToEndAsync();
                process.StandardInput.Write(Script(p.RemotePort));process.StandardInput.Close();
                if(!process.WaitForExit(50000)){process.Kill();throw new TimeoutException("远端测试超时");}
                Task.WaitAll(output,error);
                if(process.ExitCode!=0)throw new IOException("远端测试无法执行；请检查 SSH 连接与认证。");
                return Parse(output.Result,p.RemotePort);
            }
        }
    }
}

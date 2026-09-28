using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Workbench;

class FakeTunnel : ITunnelProcess {
    public bool Running=true,Ready,Disposed;public TunnelFailure Error;
    public bool Alive{get{return Running;}}public bool Confirmed{get{return Ready;}}public TunnelFailure Failure{get{return Error;}}public int Id{get{return 123;}}
    public void Dispose(){Disposed=true;Running=false;}
}
class TunnelTests {
    static int count;
    static void Check(bool ok,string name){if(!ok)throw new Exception(name);count++;Console.WriteLine("PASS "+name);}
    static void Reject(Action action,string name){try{action();}catch(ArgumentException){Check(true,name);return;}throw new Exception(name);}
    static TunnelProfile Profile(){return new TunnelProfile{Name="Test",Host="server.example",User="test",IdentityFile=@"C:\Keys\test key"};}
    static int Main(string[] args){try{if(args.Length>0&&args[0]=="--live")Live(args[1]);else Unit();Console.WriteLine(count+" tunnel checks passed");return 0;}catch(Exception e){Console.WriteLine("FAIL "+e.Message);return 1;}}
    static void Unit(){
        var p=Profile();p.Validate();
        string cmd=TunnelCommand.Arguments(p,true);
        Check(cmd.Contains("\"StrictHostKeyChecking=yes\"")&&cmd.Contains("\"BatchMode=yes\"")&&cmd.Contains("\"ExitOnForwardFailure=yes\""),"strict noninteractive SSH with forwarding failure detection");
        Check(cmd.Contains("\"-F\" \"none\"")&&cmd.Contains("\"127.0.0.1:18080:127.0.0.1:7897\""),"explicit loopback-only route without inherited forwards");
        Check(cmd.Contains("\"C:\\Keys\\test key\""),"identity path remains one argument");
        Check(TunnelCommand.Quote("x\\\"y\\")=="\"x\\\\\\\"y\\\\\"","Windows quoting handles quotes and trailing backslashes");
        p.Host="-oProxyCommand=bad";Reject(()=>p.Validate(),"option injection rejected");p=Profile();p.User="user;touch x";Reject(()=>p.Validate(),"shell metacharacters rejected");p=Profile();p.RemotePort=80;Reject(()=>p.Validate(),"privileged remote port rejected");p=Profile();
        var duplicate=new TunnelConfig();duplicate.Profiles.Add(p);var second=p.Copy();second.Id="second";duplicate.Profiles.Add(second);Reject(()=>duplicate.Validate(),"duplicate remote route rejected");
        Check(TunnelCommand.Failure("Permission denied (publickey).") == TunnelFailure.Authentication,"authentication failure classified");
        Check(TunnelCommand.Failure("Host key verification failed.") == TunnelFailure.HostKey,"host identity failure classified");
        Check(TunnelCommand.Failure("Error: remote port forwarding failed for listen port 18080") == TunnelFailure.PortBusy,"port collision classified");
        Check(TunnelCommand.ForwardConfirmed("debug1: remote forward success for: listen 127.0.0.1:18080, connect 127.0.0.1:7897",18080),"server confirmation parsed");
        Check(!TunnelCommand.ForwardConfirmed("debug1: remote forward success for: listen 127.0.0.1:18081, connect 127.0.0.1:7897",18080),"unrelated port cannot confirm connection");
        DateTime now=DateTime.UtcNow;FakeTunnel fake=null;int launches=0;bool proxy=false;
        using(var s=new TunnelSession(p,x=>{launches++;return fake=new FakeTunnel();},port=>proxy)){
            s.Start();s.Tick(now);Check(launches==0&&s.Snapshot().Status=="等待本机代理","missing local proxy waits without SSH churn");
            proxy=true;s.Tick(now.AddSeconds(11));Check(launches==1&&!s.Snapshot().Connected,"live process alone is not a ready tunnel");
            fake.Ready=true;s.Tick(now.AddSeconds(12));Check(s.Snapshot().Connected,"server acknowledgment marks tunnel ready");
            fake.Running=false;s.Tick(now.AddSeconds(13));Check(s.Snapshot().Status=="等待重连"&&fake.Disposed,"dead child disposed and retry scheduled");
            s.Tick(now.AddSeconds(14));Check(launches==1,"reconnect obeys backoff");s.Tick(now.AddSeconds(24));Check(launches==2,"transport failure reconnects");
            fake.Error=TunnelFailure.PortBusy;s.Tick(now.AddSeconds(25));Check(!s.Snapshot().Desired&&fake.Disposed,"port collision suspends retries without killing external process");
            s.Start();s.Tick(now.AddSeconds(26));s.Stop();Check(fake.Disposed&&!s.Snapshot().Desired,"manual stop releases owned process");s.Tick(now.AddSeconds(100));Check(launches==3,"manual stop remains stopped");
        }
        using(var s=new TunnelSession(p,x=>fake=new FakeTunnel(),port=>true)){s.Start();s.Tick(now);s.Tick(now.AddSeconds(26));Check(fake.Disposed&&s.Snapshot().Status=="等待重连","unconfirmed startup times out");}
        using(var s=new TunnelSession(p,x=>fake=new FakeTunnel{Error=TunnelFailure.HostKey},port=>true)){s.Start();s.Tick(now);s.Tick(now.AddSeconds(1));Check(!s.Snapshot().Desired,"host key mismatch requires intervention");}
        string response="WB_LISTEN_BEGIN\n 1: 0100007F:46A0 00000000:0000 0A rest\nWB_LISTEN_END\nWB_Example 200 0 exit=0\nWB_OpenAI 401 0 exit=0\nWB_GitHub 403 0 exit=0\n";
        var r=TunnelProbe.Parse(response,18080);Check(r.ExternalOk&&r.LoopbackOnly&&r.Details.Contains("未带凭据")&&r.Details.Contains("站点拒绝"),"end-to-end probe distinguishes success, auth, and site denial");
        r=TunnelProbe.Parse(response.Replace("0100007F","00000000"),18080);Check(!r.LoopbackOnly,"wildcard listeners are not reported as loopback");
        r=TunnelProbe.Parse(response.Replace("200 0 exit=0","200 60 exit=60"),18080);Check(!r.ExternalOk,"TLS failure is not connectivity success");
        r=TunnelProbe.Parse("unexpected output",18080);Check(!r.ExternalOk&&!r.LoopbackOnly,"missing evidence never passes");
        string path=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"tunnel-test-"+Guid.NewGuid().ToString("N")+".json");
        try{var c=new TunnelConfig();c.Profiles.Add(p);c.Save(path);c.Profiles[0].AutoStart=true;c.Save(path);Check(TunnelConfig.Load(path).Profiles[0].AutoStart,"atomic settings survive reload");}finally{if(File.Exists(path))File.Delete(path);}
    }
    static void WaitReady(TunnelSession session,string name){var until=DateTime.UtcNow.AddSeconds(30);while(DateTime.UtcNow<until){session.Tick(DateTime.UtcNow);var s=session.Snapshot();if(s.Connected){Check(true,name+" SSH forwarding confirmed; PID="+s.ProcessId);return;}if(!s.Desired)throw new Exception(name+": "+s.Detail);Thread.Sleep(200);}throw new Exception(name+" connection timeout: "+session.Snapshot().Detail);}
    static void Live(string path){
        var c=TunnelConfig.Load(path);Check(c.Profiles.Count>=2,"two server profiles available");var sessions=new List<TunnelSession>();
        try{
            foreach(var p in c.Profiles){var s=new TunnelSession(p);sessions.Add(s);s.Start();WaitReady(s,p.Name);}
            for(int i=0;i<c.Profiles.Count;i++){var r=TunnelProbe.Run(c.Profiles[i]);Console.WriteLine(c.Profiles[i].Name+": "+r.Details.Replace('\n',' '));Check(r.ExternalOk&&r.LoopbackOnly,c.Profiles[i].Name+" external HTTPS and loopback-only listener");}
            using(var collision=new TunnelSession(c.Profiles[0])){collision.Start();var until=DateTime.UtcNow.AddSeconds(15);while(DateTime.UtcNow<until&&collision.Snapshot().Desired){collision.Tick(DateTime.UtcNow);Thread.Sleep(200);}Check(!collision.Snapshot().Desired&&collision.Snapshot().Status=="需要处理","real duplicate forward rejected");Check(sessions[0].Snapshot().Connected,"existing owned tunnel remains connected after collision");}
            int prior=sessions[0].Snapshot().ProcessId;using(var child=Process.GetProcessById(prior)){child.Kill();child.WaitForExit(3000);}sessions[0].Tick(DateTime.UtcNow);Check(sessions[0].Snapshot().Status=="等待重连","real terminated SSH detected");WaitReady(sessions[0],"reconnect");Check(sessions[0].Snapshot().ProcessId!=prior,"reconnect uses a new owned process");var recovered=TunnelProbe.Run(c.Profiles[0]);Check(recovered.ExternalOk,"HTTPS works after reconnect");
        }finally{foreach(var s in sessions)s.Dispose();}
        foreach(var p in c.Profiles){var r=TunnelProbe.Run(p);Check(!r.ExternalOk&&!r.LoopbackOnly,p.Name+" stopped tunnel no longer listens or proxies");}
    }
}

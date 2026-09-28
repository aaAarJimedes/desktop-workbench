using System;
using System.Collections.Generic;
using System.IO;
using Workbench;
class FakePortal : IPortal {
    public AuthState State=AuthState.Offline;
    public int Calls;
    public bool Reject,Fail,Reachable=true,UnknownAfterLogin;
    public PortalStatus Status(){return new PortalStatus {State=State,User="test"};}
    public bool Internet(){return Reachable;}
    public void Login(string u,string p){Calls++;if(Reject)throw new AuthRejected();if(Fail)throw new IOException();State=UnknownAfterLogin?AuthState.Unknown:AuthState.Online;}
}
class Tests {
    static int count;
    static void Assert(bool ok,string name){if(!ok)throw new Exception(name);count++;Console.WriteLine("PASS "+name);}
    static NetworkMonitor Monitor(FakePortal p,Settings s){var m=new NetworkMonitor(p,s,()=>{});m.ReadPassword=()=>"test-password";return m;}
    static void Main(){
        Assert(SrunCrypto.Hmac("password","token")=="fd07c716fa2b6a86413916082c8bfdc3","HMAC reference");
        Assert(SrunCrypto.Parameters("test","password","10.0.0.1","60","0123456789abcdef0123456789abcdef")["info"]=="{SRBX1}u7bY7lo7pcuLUcH+HK/WqYL30PJN/qk+Eb/FILL2Mzz31ig8pz50kXpN1A8WK9e+TBmuwo7J9fpx9wOPfAc2Y95SayidkU/xW1IBj3UQ9/qQWK251qU5c6yGGNvzJhxo","official portal encryption reference");
        var p=new FakePortal();var s=new Settings {User="test"};var m=Monitor(p,s);
        m.Check(false);Assert(p.Calls==0,"require two offline observations");m.Check(false);Assert(p.Calls==1&&p.State==AuthState.Online,"offline recovery verified");m.Check(false);Assert(p.Calls==1,"online never reauthenticates");
        p.Reachable=false;m.Check(false);Assert(p.Calls==1,"external outage alone does not reauthenticate");
        p.State=AuthState.Unknown;m.Check(true);Assert(p.Calls==1,"unknown portal never receives credentials");
        p=new FakePortal();s=new Settings{User="test",Enabled=false};m=Monitor(p,s);m.Check(false);m.Check(false);Assert(p.Calls==0,"paused does not authenticate");m.Check(true);Assert(p.Calls==1,"manual recovery when paused");
        p=new FakePortal{Reject=true};s=new Settings{User="test"};m=Monitor(p,s);m.Check(true);Assert(!s.Enabled&&p.Calls==1,"rejection pauses retries");
        p=new FakePortal{Fail=true};s=new Settings{User="test"};m=Monitor(p,s);DateTime now=DateTime.UtcNow;m.Now=()=>now;m.Check(true);m.Check(true);Assert(p.Calls==1,"manual obeys cooldown");now=now.AddMinutes(3);m.Check(true);now=now.AddMinutes(5);m.Check(true);now=now.AddMinutes(1);m.Check(true);Assert(p.Calls==3,"at most three attempts per fifteen minutes");
        var restarted=Monitor(p,s);restarted.Now=()=>now;restarted.Check(true);Assert(p.Calls==3,"cooldown survives monitor restart");
        p=new FakePortal{UnknownAfterLogin=true};s=new Settings{User="test"};m=Monitor(p,s);Assert(m.Check(true).Title=="恢复尚未确认","login response needs status verification");
        p=new FakePortal();s=new Settings{User="test"};m=Monitor(p,s);m.ReadPassword=()=>"";m.Check(true);Assert(p.Calls==0,"missing credentials no request");
        Assert(ZjuPortal.Classify(ZjuPortal.Parse("workbench({\"error\":\"not_online_error\"})")).State==AuthState.Offline,"JSONP offline classification");
        Assert(ZjuPortal.Classify(ZjuPortal.Parse("{\"error\":\"ok\"}")).State==AuthState.Unknown,"malformed online response rejected");
        Console.WriteLine(count+" tests passed");
    }
}

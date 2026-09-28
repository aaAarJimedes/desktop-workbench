// Developer diagnostic entry point. Not included in the distribution.
using System;
using System.Net;
using Workbench;
class Diagnostics {
    static int Main(string[] args) {
        ServicePointManager.SecurityProtocol=SecurityProtocolType.Tls12;
        try {
            if(Array.IndexOf(args,"--provision")>=0) {
                string user=Console.ReadLine(),password=Console.ReadLine();
                if(String.IsNullOrEmpty(user)||String.IsNullOrEmpty(password))return 2;
                Storage.Password(password);var s=Storage.Load();s.User=user;s.Enabled=true;Storage.Save(s);
                if(Storage.Password()!=password)throw new Exception("DPAPI verification failed");
                Console.WriteLine("Credentials stored and DPAPI round-trip verified for current Windows user.");
            }
            var portal=new ZjuPortal();
            var before=portal.Status();Console.WriteLine("Before: "+before.State+"; devices="+before.Devices);
            if(Array.IndexOf(args,"--login")>=0) {
                var s=Storage.Load();portal.Login(s.User,Storage.Password());
                Console.WriteLine("Official authentication request accepted.");
            }
            var after=portal.Status();Console.WriteLine("After: "+after.State+"; devices="+after.Devices+"; internet="+portal.Internet());
            return after.State==AuthState.Online?0:3;
        } catch(Exception e) {Console.WriteLine("Diagnostic failed: "+e.GetType().Name);return 1;}
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace Workbench {
    public sealed class Settings {
        public string User = "";
        public bool Enabled = true;
        public DateTime RetryAfter = DateTime.MinValue;
        public List<DateTime> Attempts = new List<DateTime>();
    }
    public static class Storage {
        public static readonly string Root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"DesktopWorkbench");
        static readonly object Gate = new object();
        public static Settings Load() {
            string path = Path.Combine(Root,"settings.json");
            if (!File.Exists(path)) return new Settings();
            var s = new JavaScriptSerializer().Deserialize<Settings>(File.ReadAllText(path));
            if (s == null || s.Attempts == null) throw new InvalidDataException("配置无效");
            return s;
        }
        static void Atomic(string path,byte[] bytes) {
            Directory.CreateDirectory(Root); string temp = path + ".tmp";
            File.WriteAllBytes(temp,bytes);
            if (File.Exists(path)) File.Replace(temp,path,null); else File.Move(temp,path);
        }
        public static void Save(Settings s) { lock(Gate) Atomic(Path.Combine(Root,"settings.json"),Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(s))); }
        public static void Password(string password) {
            byte[] plain = Encoding.UTF8.GetBytes(password);
            try { lock(Gate) Atomic(Path.Combine(Root,"credential.dat"),ProtectedData.Protect(plain,null,DataProtectionScope.CurrentUser)); }
            finally { Array.Clear(plain,0,plain.Length); }
        }
        public static string Password() {
            string path = Path.Combine(Root,"credential.dat"); if (!File.Exists(path)) return "";
            byte[] bytes = ProtectedData.Unprotect(File.ReadAllBytes(path),null,DataProtectionScope.CurrentUser);
            try { return Encoding.UTF8.GetString(bytes); } finally { Array.Clear(bytes,0,bytes.Length); }
        }
        public static void Forget() { lock(Gate) { string p = Path.Combine(Root,"credential.dat"); if (File.Exists(p)) File.Delete(p); } }
        public static bool Startup {
            get { using (var k=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run")) return k != null && k.GetValue("DesktopWorkbench") != null; }
        }
        public static void SetStartup(bool enabled,string exe) {
            using (var k = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run")) {
                if (enabled) k.SetValue("DesktopWorkbench","\""+exe+"\" --tray"); else k.DeleteValue("DesktopWorkbench",false);
            }
        }
    }
    public sealed class MonitorResult {
        public string Title, Detail;
        public PortalStatus Portal;
        public bool? Internet;
        public DateTime CheckedAt = DateTime.Now;
    }
    public sealed class NetworkMonitor {
        readonly IPortal portal;
        readonly Settings settings;
        readonly Action save;
        public Func<string> ReadPassword;
        public Func<DateTime> Now = () => DateTime.UtcNow;
        public Func<bool> Active;
        int misses, failures;
        public NetworkMonitor(IPortal p,Settings s,Action persist) { portal=p; settings=s; save=persist; Active=()=>settings.Enabled; }
        MonitorResult Result(string title,string detail,PortalStatus s,bool? internet) { return new MonitorResult {Title=title,Detail=detail,Portal=s,Internet=internet}; }
        public MonitorResult Check(bool manual) {
            PortalStatus state;
            try { state=portal.Status(); } catch { misses=0; return Result("无法访问认证服务器","可能未连接校园网、DNS 异常或服务器暂不可用；稍后重试。",null,null); }
            bool internet=portal.Internet();
            if (state.State==AuthState.Online) {
                misses=0; failures=0;
                return Result(internet ? "网络已连接" : "已认证，外网检测未通过",internet ? "校园网认证有效，无需重复登录。" : "保留现有认证；检查上游网络、VPN 或检测站点可用性。",state,internet);
            }
            if (state.State==AuthState.Unknown) { misses=0; return Result("认证状态未知","服务器响应无法识别，暂不提交凭据。",state,internet); }
            if (!Active() && !manual) return Result("自动恢复已暂停","校园网未认证，可手动检查并恢复。",state,internet);
            misses++;
            if (!manual && misses<2) return Result("检测到认证失效","30 秒后复核，排除短暂波动。",state,internet);
            DateTime now=Now();
            settings.Attempts.RemoveAll(t=>t<now.AddMinutes(-15));
            if (now<settings.RetryAfter || settings.Attempts.Count>=3) return Result("等待重试","认证频率受限，下次不早于 "+settings.RetryAfter.ToLocalTime().ToString("HH:mm:ss")+"。",state,internet);
            string password=ReadPassword();
            if (String.IsNullOrWhiteSpace(settings.User) || String.IsNullOrEmpty(password)) return Result("请设置校园网账户","填写账号和密码后保存，即可自动恢复认证。",state,internet);
            if (!Active() && !manual) return Result("自动恢复已暂停","等待你重新启用。",state,internet);
            settings.Attempts.Add(now); settings.RetryAfter=now.AddSeconds(60);
            if (settings.Attempts.Count>=3) settings.RetryAfter=now.AddMinutes(15);
            save(); // Persist the rate limit before sending credentials.
            try {
                portal.Login(settings.User,password);
                var verified=portal.Status(); bool connected=portal.Internet();
                if (verified.State!=AuthState.Online || !String.Equals(verified.User.Split('@')[0],settings.User.Split('@')[0],StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("认证未确认");
                misses=0; failures=0;
                return Result(connected ? "连接已恢复" : "认证已恢复，外网待确认",connected ? "网页登录完成，已复核认证与外网连接。" : "认证已确认，外网探测未通过；不会反复重登。",verified,connected);
            } catch (AuthRejected) {
                settings.Enabled=false; save();
                return Result("认证被拒绝，自动恢复已暂停","请检查密码或打开官网处理验证码、账户限制；更新后重新启用。",state,internet);
            } catch {
                failures++;
                DateTime retry=now.AddSeconds(Math.Min(900,60*Math.Pow(2,Math.Min(failures,4))));
                if (retry>settings.RetryAfter) settings.RetryAfter=retry;
                save();
                return Result("恢复尚未确认","网络请求失败或认证未确认；已降低重试频率。",state,internet);
            }
        }
    }
}

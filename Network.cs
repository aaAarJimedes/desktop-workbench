using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace Workbench {
    public enum AuthState { Online, Offline, Unknown }
    public sealed class PortalStatus {
        public AuthState State;
        public string User = "", IP = "", Devices = "";
    }
    public interface IPortal {
        PortalStatus Status();
        bool Internet();
        void Login(string user, string password);
    }
    public sealed class AuthRejected : Exception {
        public AuthRejected() : base("认证被拒绝；请检查账号、密码或通过官网完成验证") { }
    }
    public static class SrunCrypto {
        static uint[] Words(string s, bool length) {
            uint[] v = new uint[(s.Length + 3) / 4 + (length ? 1 : 0)];
            for (int i = 0; i < s.Length; i++) v[i >> 2] |= (uint)s[i] << ((i & 3) * 8);
            if (length) v[v.Length - 1] = (uint)s.Length;
            return v;
        }
        public static string Encode(string s, string token) {
            if (s.Length == 0) return "";
            uint[] v = Words(s, true), k = Words(token, false);
            if (k.Length < 4) Array.Resize(ref k, 4);
            unchecked {
                int n = v.Length - 1; uint z = v[n], y, sum = 0;
                for (int q = 6 + 52 / v.Length; q > 0; q--) {
                    sum += 0x9e3779b9; uint e = (sum >> 2) & 3;
                    for (int p = 0; p <= n; p++) {
                        y = v[p == n ? 0 : p + 1];
                        uint m = (z >> 5) ^ (y << 2);
                        m += (y >> 3) ^ (z << 4) ^ (sum ^ y);
                        m += k[(p & 3) ^ (int)e] ^ z;
                        z = v[p] += m;
                    }
                }
            }
            byte[] bytes = new byte[v.Length * 4];
            for (int i = 0; i < v.Length; i++) for (int j = 0; j < 4; j++) bytes[i*4+j] = (byte)(v[i] >> (8*j));
            const string normal = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";
            const string custom = "LVoJPiCN2R8G90yg+hmFHuacZ1OWMnrsSTXkYpUq/3dlbfKwv6xztjI7DeBE45QA";
            string encoded = Convert.ToBase64String(bytes); var b = new StringBuilder("{SRBX1}");
            foreach (char c in encoded) b.Append(c == '=' ? '=' : custom[normal.IndexOf(c)]);
            return b.ToString();
        }
        static string Hex(byte[] bytes) { return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant(); }
        public static string Hmac(string password, string token) {
            using (var h = new HMACMD5(Encoding.UTF8.GetBytes(token))) return Hex(h.ComputeHash(Encoding.UTF8.GetBytes(password)));
        }
        public static Dictionary<string,string> Parameters(string user, string password, string ip, string acid, string token) {
            var info = new Dictionary<string,string> { {"username",user}, {"password",password}, {"ip",ip}, {"acid",acid}, {"enc_ver","srun_bx1"} };
            string encoded = Encode(new JavaScriptSerializer().Serialize(info), token), hmac = Hmac(password, token), checksum;
            using (var h = SHA1.Create()) checksum = Hex(h.ComputeHash(Encoding.UTF8.GetBytes(token + user + token + hmac + token + acid + token + ip + token + "200" + token + "1" + token + encoded)));
            return new Dictionary<string,string> { {"action","login"}, {"username",user}, {"password","{MD5}"+hmac}, {"ac_id",acid}, {"ip",ip}, {"info",encoded}, {"chksum",checksum}, {"n","200"}, {"type","1"}, {"os","Windows 10"}, {"name","Windows"}, {"double_stack","0"} };
        }
    }
    public sealed class ZjuPortal : IPortal {
        public const string Origin = "https://net3.zju.edu.cn";
        public static string Get(string url, bool portal) {
            Uri uri = new Uri(url);
            for (int i = 0; i < 5; i++) {
                if (portal && (uri.Scheme != "https" || uri.Host != "net3.zju.edu.cn" || !uri.IsDefaultPort)) throw new InvalidDataException("拒绝离开受信任认证站点");
                var r = (HttpWebRequest)WebRequest.Create(uri);
                r.Proxy = null; r.AllowAutoRedirect = false; r.Timeout = 8000; r.ReadWriteTimeout = 8000;
                r.UserAgent = "DesktopWorkbench/1.0";
                using (var response = (HttpWebResponse)r.GetResponse()) {
                    int code = (int)response.StatusCode;
                    if (code >= 300 && code < 400) {
                        if (!portal) throw new InvalidDataException("连通性检测被重定向");
                        uri = new Uri(uri, response.Headers["Location"]); continue;
                    }
                    using (var reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8)) {
                        char[] buffer = new char[262144]; int count = 0, n;
                        while (count < buffer.Length && (n = reader.Read(buffer,count,buffer.Length-count)) > 0) count += n;
                        if (count == buffer.Length) throw new InvalidDataException("响应超出限制");
                        return new string(buffer,0,count);
                    }
                }
            }
            throw new InvalidDataException("跳转次数过多");
        }
        public static Dictionary<string,object> Parse(string raw) {
            raw = raw.Trim();
            if (raw.StartsWith("workbench(")) {
                raw = raw.Substring(10).TrimEnd(';');
                if (!raw.EndsWith(")")) throw new InvalidDataException("JSONP 格式无效");
                raw = raw.Substring(0,raw.Length-1);
            }
            return new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(raw);
        }
        public static string Field(Dictionary<string,object> data,string key) { object v; return data.TryGetValue(key,out v) ? Convert.ToString(v) : ""; }
        Dictionary<string,object> Api(string path, Dictionary<string,string> args) {
            var query = new StringBuilder("?callback=workbench");
            foreach (var pair in args) query.Append("&").Append(Uri.EscapeDataString(pair.Key)).Append("=").Append(Uri.EscapeDataString(pair.Value));
            return Parse(Get(Origin + path + query, true));
        }
        public static PortalStatus Classify(Dictionary<string,object> d) {
            string error = Field(d,"error");
            if (error == "ok" && Field(d,"user_name") != "") return new PortalStatus { State = AuthState.Online, User = Field(d,"user_name"), IP = Field(d,"online_ip"), Devices = Field(d,"online_device_total") };
            if (error == "not_online_error" || error == "ip_not_online_error") return new PortalStatus { State = AuthState.Offline };
            return new PortalStatus { State = AuthState.Unknown };
        }
        public PortalStatus Status() { return Classify(Api("/cgi-bin/rad_user_info",new Dictionary<string,string>())); }
        public bool Internet() {
            try { if (Get("https://www.msftconnecttest.com/connecttest.txt",false).Trim() == "Microsoft Connect Test") return true; } catch { }
            try { return Get("https://www.baidu.com/",false).IndexOf("baidu",StringComparison.OrdinalIgnoreCase) >= 0; } catch { return false; }
        }
        public void Login(string user, string password) {
            string html = Get(Origin+"/",true);
            // net3 uses an HTML refresh after its HTTP redirect.
            for (int i=0;i<3;i++) {
                var refresh = Regex.Match(html,@"url=([^""'>]+)",RegexOptions.IgnoreCase);
                if (!refresh.Success) break;
                Uri target = new Uri(new Uri(Origin),WebUtility.HtmlDecode(refresh.Groups[1].Value));
                html = Get(target.AbsoluteUri,true);
            }
            var acid = Regex.Match(html,"acid\\s*:\\s*\"(\\d+)\"");
            var ip = Regex.Match(html,"ip\\s*:\\s*\"([0-9.]+)\"");
            if (!acid.Success || !ip.Success) throw new InvalidDataException("认证页面配置已变化，请打开官网检查");
            var challenge = Api("/cgi-bin/get_challenge",new Dictionary<string,string>{{"username",user},{"ip",ip.Groups[1].Value}});
            string token = Field(challenge,"challenge");
            if (Field(challenge,"error") != "ok" || !Regex.IsMatch(token,@"\A[0-9a-fA-F]{32,128}\z")) throw new InvalidDataException("获取认证挑战失败");
            var result = Api("/cgi-bin/srun_portal",SrunCrypto.Parameters(user,password,ip.Groups[1].Value,acid.Groups[1].Value,token));
            if (Field(result,"error") != "ok") throw new AuthRejected();
        }
    }
}

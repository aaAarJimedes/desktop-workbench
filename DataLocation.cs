using System;
using System.IO;
namespace Workbench {
    public static class DataLocation {
        // AppData may be redirected when launched from a packaged development host.
        // A profile-relative directory resolves identically for desktop and development launches.
        public static readonly string Root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),".desktop-workbench");
        public static void MigrateLegacy(){Migrate(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"DesktopWorkbench"),Root);}
        internal static void Migrate(string legacy,string target){
            Directory.CreateDirectory(target);
            // Keep the account and credential from the same source; never replace existing data.
            if(!File.Exists(Path.Combine(target,"settings.json"))&&!File.Exists(Path.Combine(target,"credential.dat"))){
                CopyMissing(legacy,target,"credential.dat");CopyMissing(legacy,target,"settings.json");
            }
            CopyMissing(legacy,target,"tunnels.json");CopyMissing(legacy,target,"tunnels.json.bak");
        }
        static void CopyMissing(string source,string target,string name){
            string from=Path.Combine(source,name),to=Path.Combine(target,name);
            if(!File.Exists(to)&&File.Exists(from))File.Copy(from,to,false);
        }
    }
}

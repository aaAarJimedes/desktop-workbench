using System;
using System.IO;
namespace Workbench {
    public static class DataLocation {
        // Independent of the portable executable's directory; accessed only by the native worker.
        public static readonly string Root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"DesktopWorkbench");
        public static void MigrateLegacy(){Migrate(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),".desktop-workbench"),Root);}
        internal static void Migrate(string legacy,string target){
            Directory.CreateDirectory(target);
            // Keep the account and credential from the same source; never replace existing data.
            if(!File.Exists(Path.Combine(target,"settings.json"))&&!File.Exists(Path.Combine(target,"credential.dat"))){
                CopyMissing(legacy,target,"credential.dat");CopyMissing(legacy,target,"settings.json");
            }
            CopyMissing(legacy,target,"tunnels.json");CopyMissing(legacy,target,"tunnels.json.bak");
            string logs=Path.Combine(legacy,"logs");
            if(Directory.Exists(logs))foreach(string name in new[]{"runtime.log","runtime.log.1"}){
                if(File.Exists(Path.Combine(logs,name))){Directory.CreateDirectory(Path.Combine(target,"logs"));CopyMissing(logs,Path.Combine(target,"logs"),name);}
            }
        }
        static void CopyMissing(string source,string target,string name){
            string from=Path.Combine(source,name),to=Path.Combine(target,name);
            if(!File.Exists(to)&&File.Exists(from))File.Copy(from,to,false);
        }
    }
}

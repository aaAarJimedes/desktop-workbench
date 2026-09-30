using System;
using System.IO;
using System.Windows.Forms;
using Workbench;
class ToolViewTests {
    static int count;
    static void Check(bool ok,string name){if(!ok)throw new Exception(name);count++;Console.WriteLine("PASS "+name);}
    static DataGridView Grid(Control c){if(c is DataGridView)return (DataGridView)c;foreach(Control child in c.Controls){var g=Grid(child);if(g!=null)return g;}return null;}
    [STAThread] static int Main(){
        string root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"view-test-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);RuntimeLog.TestDirectory=root;
        try {
            string legacy=Path.Combine(root,"legacy"),target=Path.Combine(root,"canonical"),partial=Path.Combine(root,"partial");Directory.CreateDirectory(legacy);
            File.WriteAllText(Path.Combine(legacy,"credential.dat"),"encrypted-fixture");File.WriteAllText(Path.Combine(legacy,"settings.json"),"account-fixture");File.WriteAllText(Path.Combine(legacy,"tunnels.json"),"tunnel-fixture");
            Directory.CreateDirectory(Path.Combine(legacy,"logs"));File.WriteAllText(Path.Combine(legacy,"logs","runtime.log"),"legacy-log");
            DataLocation.Migrate(legacy,target);Check(File.ReadAllText(Path.Combine(target,"credential.dat"))=="encrypted-fixture"&&File.ReadAllText(Path.Combine(target,"tunnels.json"))=="tunnel-fixture","legacy migration preserves credential and tunnel bytes");
            Check(File.ReadAllText(Path.Combine(target,"logs","runtime.log"))=="legacy-log","migration preserves diagnostics");
            File.WriteAllText(Path.Combine(target,"logs","runtime.log"),"current-log");DataLocation.Migrate(legacy,target);Check(File.ReadAllText(Path.Combine(target,"logs","runtime.log"))=="current-log","migration does not replace current diagnostics");
            File.WriteAllText(Path.Combine(legacy,"settings.json"),"different-account");DataLocation.Migrate(legacy,target);Check(File.ReadAllText(Path.Combine(target,"settings.json"))=="account-fixture","migration never overwrites existing account");
            Directory.CreateDirectory(partial);File.WriteAllText(Path.Combine(partial,"settings.json"),"existing-account");DataLocation.Migrate(legacy,partial);Check(!File.Exists(Path.Combine(partial,"credential.dat")),"migration never mixes existing account with another credential");
            Check(DataLocation.Root==Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"DesktopWorkbench"),"portable data root uses the Windows LocalAppData known folder");
            string path=Path.Combine(root,"tunnels.json");var cfg=new TunnelConfig();cfg.Profiles.Add(new TunnelProfile{Name="Saved one",Host="one.example",User="test"});cfg.Profiles.Add(new TunnelProfile{Name="Saved two",Host="two.example",User="test"});cfg.Save(path);
            Check(File.Exists(path+".bak"),"first save creates persistent backup");
            for(int i=0;i<2;i++)using(var tool=new TunnelTool(path))using(var f=new Form()){
                var view=tool.CreateView();f.Controls.Add(view);f.CreateControl();Check(Grid(view).Rows.Count==2,"saved tunnel rows survive fresh tool instance "+i);
                f.Controls.Remove(view);f.Controls.Add(view);Check(Grid(view).Rows.Count==2,"switching views preserves saved rows "+i);
            }
            string delayed=Path.Combine(root,"late.json");using(var tool=new TunnelTool(delayed)){
                var view=tool.CreateView();Check(Grid(view).Rows.Count==0,"initially absent configuration is empty");cfg.Save(delayed);tool.Start();Check(Grid(view).Rows.Count==2,"startup rechecks configuration that became available");view.Dispose();
            }
            cfg.Profiles[0].Name="Updated";cfg.Save(path);Check(TunnelConfig.Load(path).Profiles[0].Name=="Updated","edited common tunnel survives reload");
            File.WriteAllText(path,"broken");var recovered=TunnelConfig.Load(path);Check(recovered.RecoveredFromBackup&&recovered.Profiles.Count==2,"corrupt primary recovers saved tunnels from backup");
            recovered.Save(path);Check(Directory.GetFiles(root,"tunnels.json.unreadable-*").Length==1&&TunnelConfig.Load(path).Profiles.Count==2,"saving recovered configuration preserves damaged original");
            File.Delete(path);Check(TunnelConfig.Load(path).RecoveredFromBackup,"missing primary recovers backup instead of empty list");
            File.WriteAllText(path,"bad-primary");File.WriteAllText(path+".bak","bad-backup");bool threw=false;try{TunnelConfig.Load(path);}catch{threw=true;}Check(threw&&File.ReadAllText(path)=="bad-primary","invalid primary and backup never silently become empty");
            Console.WriteLine(count+" saved-tunnel view checks passed");return 0;
        }catch(Exception e){Console.WriteLine("FAIL "+e.Message);return 1;}
        finally{RuntimeLog.TestDirectory=null;Directory.Delete(root,true);}
    }
}

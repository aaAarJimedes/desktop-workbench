using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
namespace Workbench {
    public sealed class OptimizationSettings {
        public string[] ExcludedProcesses=new string[0];
        public static string PathName {get{return Path.Combine(DataLocation.Root,"optimization.json");}}
        public static OptimizationSettings Load(string path){try{var settings=new JavaScriptSerializer().Deserialize<OptimizationSettings>(File.ReadAllText(path));if(settings==null||settings.ExcludedProcesses==null)throw new InvalidDataException();return settings;}catch(FileNotFoundException){return new OptimizationSettings();}catch(DirectoryNotFoundException){return new OptimizationSettings();}}
        public void Save(string path){Directory.CreateDirectory(Path.GetDirectoryName(path));string temp=path+".tmp";File.WriteAllText(temp,new JavaScriptSerializer().Serialize(this));if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);}
    }
    public sealed class OptimizationTool : IWorkbenchTool {
        public string Id {get{return "system-optimization";}}
        public string Title {get{return "系统优化";}}
        public string Description {get{return "内存状态与后台回收";}}
        readonly IMemoryBackend backend;readonly MemoryCleaner cleaner;readonly string configPath;OptimizationSettings settings;
        readonly Timer timer=new Timer{Interval=1000};bool disposed,settingsReadable=true;
        Panel view;Label memory,status,result;ProgressBar bar;Button clean,cancel;TextBox excluded;
        public event Action<string> Completed;
        public OptimizationTool():this(new WindowsMemoryBackend(),OptimizationSettings.PathName){}
        internal OptimizationTool(IMemoryBackend backend,string path){this.backend=backend;configPath=path;cleaner=new MemoryCleaner(backend);try{settings=OptimizationSettings.Load(path);}catch{settings=new OptimizationSettings();settingsReadable=false;}timer.Tick+=(s,e)=>Refresh();}
        public bool CanClean {get{return !disposed&&settingsReadable&&!cleaner.Busy&&cleaner.Cooldown==0;}}
        public string TrayText {get{return cleaner.Busy?"正在清理内存…":cleaner.Cooldown>0?"清理内存（"+cleaner.Cooldown+" 秒后可用）":"清理内存";}}
        public void AttachTrayMenu(ContextMenuStrip menu,Action open){
            menu.Items.Add("系统优化",null,(s,e)=>open());
            var item=menu.Items.Add("清理内存",null,(s,e)=>RequestClean());
            menu.Opening+=(s,e)=>{item.Enabled=CanClean;item.Text=TrayText;};
        }
        public Control CreateView() {
            view=new Panel{Dock=DockStyle.Fill,AutoScroll=true,BackColor=Color.FromArgb(246,248,252),Padding=new Padding(30)};
            var layout=new TableLayoutPanel{Dock=DockStyle.Top,AutoSize=true,ColumnCount=1};view.Controls.Add(layout);
            layout.Controls.Add(Label("系统优化",26,60));layout.Controls.Add(Label("回收空闲后台内存，保留正在进行的工作。",11,40));
            memory=Label("正在读取内存状态…",19,58);memory.ForeColor=Color.FromArgb(30,105,104);layout.Controls.Add(memory);
            bar=new ProgressBar{Dock=DockStyle.Top,Height=12,Margin=new Padding(0,4,0,18)};layout.Controls.Add(bar);
            var actions=new FlowLayoutPanel{Dock=DockStyle.Top,AutoSize=true};clean=Button("清理内存",RequestClean);cancel=Button("停止本次清理",()=>cleaner.Cancel());cancel.Enabled=false;actions.Controls.Add(clean);actions.Controls.Add(cancel);layout.Controls.Add(actions);
            status=Label("手动运行 · 保护前台应用、系统进程和繁忙后台 · 每次间隔 60 秒",10,58);layout.Controls.Add(status);
            result=Label(settingsReadable?"尚未清理。也可右键托盘图标，直接选择“清理内存”。":"排除列表读取失败，清理已禁用。修复配置后重启工作台。",11,100);layout.Controls.Add(result);
            layout.Controls.Add(Label("保留这些程序的内存",13,46));layout.Controls.Add(Label("输入进程文件名，以逗号或换行分隔，例如：javaw.exe, blender.exe",10,40));
            excluded=new TextBox{Multiline=true,Height=76,Dock=DockStyle.Top,Text=String.Join(Environment.NewLine,settings.ExcludedProcesses),ScrollBars=ScrollBars.Vertical};layout.Controls.Add(excluded);
            var save=Button("保存排除列表",Save);save.Enabled=settingsReadable;layout.Controls.Add(save);
            layout.Controls.Add(Label("不关闭、暂停或重启程序，不删除文件、不清空剪贴板。\n回收的是程序的驻留内存；重新切回应用时可能短暂加载。\n可用内存包含 Windows 缓存，清理效果随当前负载变化，并不保证提速。",10,106));
            view.VisibleChanged+=(s,e)=>{if(view.Visible)Refresh();};Refresh();return view;
        }
        static Label Label(string text,float size,int height){return new Label{Text=text,Font=new Font("Microsoft YaHei UI",size,size>=13?FontStyle.Bold:FontStyle.Regular),Dock=DockStyle.Top,Height=height,Margin=new Padding(0),AutoEllipsis=true,TextAlign=ContentAlignment.MiddleLeft};}
        static Button Button(string text,Action action){var b=new Button{Text=text,AutoSize=true,MinimumSize=new Size(140,38),FlatStyle=FlatStyle.Flat,BackColor=Color.White,Margin=new Padding(0,4,12,4)};b.Click+=(s,e)=>action();return b;}
        void Save(){try{var names=excluded.Text.Split(new[]{',','，',';','；','\r','\n'},StringSplitOptions.RemoveEmptyEntries).Select(x=>x.Trim()).Where(x=>x.Length>0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();if(names.Length>128||names.Any(x=>x.Length>260||x.IndexOfAny(new[]{'\\','/','*','?'})>=0))throw new ArgumentException();var next=new OptimizationSettings{ExcludedProcesses=names};next.Save(configPath);settings=next;result.Text="排除列表已保存，下次清理生效。";}catch{result.Text="保存失败。请仅输入程序文件名，并检查配置目录权限。";}}
        public async void RequestClean() {
            if(!CanClean)return;Task<MemoryCleanupResult> task;if(!cleaner.TryStart(settings.ExcludedProcesses,out task))return;Refresh();
            try{var value=await task;if(disposed)return;result.Text=value.Summary;RuntimeLog.Write("memory-clean-complete-count-"+value.Trimmed,null);var callback=Completed;if(callback!=null)callback(value.Summary);}
            catch(Exception e){RuntimeLog.Write("memory-clean",e);if(!disposed){result.Text="清理未完成，程序继续运行。请查看诊断日志。";var callback=Completed;if(callback!=null)callback(result.Text);}}
            finally{if(!disposed)Refresh();}
        }
        void Refresh(){if(disposed||view==null)return;backend.ObserveForeground();clean.Enabled=CanClean;cancel.Enabled=cleaner.Busy;if(cleaner.Busy)status.Text=cleaner.Progress;else status.Text=cleaner.Cooldown>0?"下次清理可在 "+cleaner.Cooldown+" 秒后运行。":"手动运行 · 保护前台应用、系统进程和繁忙后台 · 每次间隔 60 秒";
            if(!view.Visible)return;try{var s=backend.Status();memory.Text="可用 "+MemoryStatus.Size(s.Available)+" / "+MemoryStatus.Size(s.Total);bar.Value=s.Total>0?(int)Math.Max(0,Math.Min(100,100.0*(s.Total-s.Available)/s.Total)):0;}catch{memory.Text="暂时无法读取内存状态";}}
        public void Start(){timer.Start();}
        public void Dispose(){disposed=true;timer.Stop();timer.Dispose();cleaner.Dispose();}
    }
}

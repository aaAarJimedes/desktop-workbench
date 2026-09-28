using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Workbench {
    public sealed class Shell : Form {
        readonly List<IWorkbenchTool> tools=new List<IWorkbenchTool>();
        readonly Dictionary<string,Control> views=new Dictionary<string,Control>();
        readonly Panel content=new Panel {Dock=DockStyle.Fill};
        readonly FlowLayoutPanel nav=new FlowLayoutPanel {Dock=DockStyle.Left,Width=210,FlowDirection=FlowDirection.TopDown,WrapContents=false,Padding=new Padding(18,24,12,18),BackColor=Color.FromArgb(24,37,56)};
        readonly NotifyIcon tray;
        readonly System.Windows.Forms.Timer residentTimer=new System.Windows.Forms.Timer{Interval=500};
        EventWaitHandle showEvent,stopEvent;
        static readonly int TaskbarCreated=RegisterWindowMessage("TaskbarCreated");
        [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int RegisterWindowMessage(string message);
        bool exiting;
        public Shell(bool hidden) {
            Text="工具工作台";Icon=Branding.Icon(64);Font=new Font("Microsoft YaHei UI",10);ClientSize=new Size(1030,870);MinimumSize=new Size(850,650);StartPosition=FormStartPosition.CenterScreen;AutoScaleMode=AutoScaleMode.Dpi;
            Controls.Add(content);Controls.Add(nav);
            nav.Controls.Add(new Label {Text="WORKBENCH",ForeColor=Color.FromArgb(102,201,184),Font=new Font("Segoe UI",12,FontStyle.Bold),Width=175,Height=40});
            nav.Controls.Add(new Label {Text="工具工作台",ForeColor=Color.White,Font=new Font("Microsoft YaHei UI",18,FontStyle.Bold),Width=175,Height=64});
            TryRegister(()=>new CampusTool(),"自动联网");
            TryRegister(()=>new TunnelTool(),"服务器隧道");
            string plugins=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"plugins");
            if(Directory.Exists(plugins))foreach(string file in Directory.GetFiles(plugins,"*.dll")){
                try {foreach(Type t in Assembly.LoadFrom(file).GetTypes())if(typeof(IWorkbenchTool).IsAssignableFrom(t)&&!t.IsAbstract&&t.GetConstructor(Type.EmptyTypes)!=null)Register((IWorkbenchTool)Activator.CreateInstance(t));}
                catch(Exception error) {RuntimeLog.Write("plugin-load",error);nav.Controls.Add(new Label {Text="有扩展加载失败",ForeColor=Color.Orange,Width=175,Height=32});}
            }
            nav.Controls.Add(new Label {Text="本地运行 · v1.2.0\n\n已安装 "+tools.Count+" 个工具\n\n后台守护已启用",ForeColor=Color.FromArgb(162,181,203),Width=175,Height=140,Margin=new Padding(0,35,0,0)});
            var menu=new ContextMenuStrip();menu.Items.Add("打开工作台",null,(s,e)=>ShowWindow());menu.Items.Add("打开诊断日志目录",null,(s,e)=>{Directory.CreateDirectory(RuntimeLog.DirectoryPath);System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(RuntimeLog.DirectoryPath){UseShellExecute=true});});menu.Items.Add("停止常驻并退出",null,(s,e)=>Exit());
            tray=new NotifyIcon {Icon=Branding.Icon(32),Text="工具工作台 · 校园网与服务器隧道",ContextMenuStrip=menu,Visible=true};tray.DoubleClick+=(s,e)=>ShowWindow();
            showEvent=new EventWaitHandle(false,EventResetMode.AutoReset,Resident.Scope+".show");stopEvent=new EventWaitHandle(false,EventResetMode.ManualReset,Resident.Scope+".stop");
            residentTimer.Tick+=(s,e)=>RuntimeLog.Guard("resident-ui",()=>{if(stopEvent.WaitOne(0)){Exit();return;}if(showEvent.WaitOne(0))ShowWindow();});
            if(tools.Count>0)ShowTool(tools[0].Id);
            Shown+=(s,e)=>{foreach(var tool in tools)RuntimeLog.Guard("tool-start",tool.Start);residentTimer.Start();if(hidden)Hide();};
            FormClosing+=(s,e)=>{if(!exiting&&e.CloseReason==CloseReason.UserClosing){e.Cancel=true;Hide();tray.ShowBalloonTip(1800,"工具工作台","已在托盘继续运行；右键托盘图标可停止常驻。",ToolTipIcon.Info);}else Resident.Stop();};
            FormClosed+=(s,e)=>{residentTimer.Stop();residentTimer.Dispose();foreach(var tool in tools)RuntimeLog.Guard("tool-stop",tool.Dispose);tray.Visible=false;var icon=tray.Icon;tray.Dispose();icon.Dispose();showEvent.Dispose();stopEvent.Dispose();};
        }
        void TryRegister(Func<IWorkbenchTool> factory,string name){try{Register(factory());}catch(Exception e){RuntimeLog.Write("tool-load",e);nav.Controls.Add(new Label{Text=name+"加载失败\n查看托盘诊断日志",ForeColor=Color.Orange,Width=175,Height=60});}}
        void Exit(){exiting=true;Resident.Stop();Close();}
        protected override void WndProc(ref Message m){base.WndProc(ref m);if(m.Msg==TaskbarCreated&&tray!=null&&!exiting)RuntimeLog.Guard("tray-restore",()=>{tray.Visible=false;tray.Visible=true;});}
        void Register(IWorkbenchTool tool) {
            if(views.ContainsKey(tool.Id))throw new InvalidOperationException("重复工具标识");
            Control view=tool.CreateView();views.Add(tool.Id,view);tools.Add(tool);
            var button=new Button {Text=tool.Title+"\n"+tool.Description,Width=175,Height=76,FlatStyle=FlatStyle.Flat,ForeColor=Color.White,BackColor=Color.FromArgb(40,59,81),TextAlign=ContentAlignment.MiddleLeft,Margin=new Padding(0,0,0,10)};
            button.FlatAppearance.BorderSize=0;button.Click+=(s,e)=>ShowTool(tool.Id);nav.Controls.Add(button);
        }
        void ShowTool(string id) {content.Controls.Clear();var c=views[id];c.Dock=DockStyle.Fill;content.Controls.Add(c);}
        void ShowWindow(){Show();WindowState=FormWindowState.Normal;Activate();}
    }
    static class Program {
        [STAThread] static int Main(string[] args) {
            if(Array.IndexOf(args,"--quit")>=0){Resident.Stop();return 0;}
            if(Array.IndexOf(args,"--worker")<0)return Resident.Supervise(Application.ExecutablePath,Array.IndexOf(args,"--tray")>=0,Resident.Scope);
            ServicePointManager.SecurityProtocol=SecurityProtocolType.Tls12;
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException+=(s,e)=>RuntimeLog.Write("ui-unhandled",e.Exception);
            AppDomain.CurrentDomain.UnhandledException+=(s,e)=>RuntimeLog.Write("fatal-unhandled",e.ExceptionObject as Exception);
            TaskScheduler.UnobservedTaskException+=(s,e)=>{RuntimeLog.Write("task-unobserved",e.Exception);e.SetObserved();};
            Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
            bool created;
            using(var mutex=new Mutex(true,@"Local\DesktopWorkbench-"+Environment.UserName,out created)){
                if(!created){Resident.Show();return 0;}
                try {Application.Run(new Shell(Array.IndexOf(args,"--tray")>=0));return 0;}
                catch(Exception e) {RuntimeLog.Write("worker-startup",e);return 1;}
            }
        }
    }
}

using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Workbench {
    public sealed class CampusTool : IWorkbenchTool {
        public string Id { get {return "zju-auto-connect";} }
        public string Title { get {return "自动联网";} }
        public string Description { get {return "浙江大学校园网";} }
        readonly Settings settings;
        readonly NetworkMonitor monitor;
        System.Threading.Timer timer;
        int busy;
        volatile bool disposed, enabled;
        Panel view;
        Label status,detail,meta,passwordState;
        int savedPasswordState;
        TextBox user,password;
        CheckBox auto,startup;
        Button check,save,forget;
        ListBox events;
        public CampusTool() {
            settings=Storage.Load(); enabled=settings.Enabled;
            monitor=new NetworkMonitor(new ZjuPortal(),settings,()=>Storage.Save(settings));
            monitor.ReadPassword=()=>Storage.Password(); monitor.Active=()=>enabled;
        }
        public Control CreateView() {
            view=new Panel {Dock=DockStyle.Fill,AutoScroll=true,BackColor=Color.FromArgb(246,248,252),Padding=new Padding(30)};
            var layout=new TableLayoutPanel {Dock=DockStyle.Top,AutoSize=true,ColumnCount=1};
            view.Controls.Add(layout);
            layout.Controls.Add(Label("自动联网",26,FontStyle.Bold,54));
            layout.Controls.Add(Label("让校园网认证在后台恢复，继续手头的工作。",11,FontStyle.Regular,38));
            status=Label("正在检查网络…",20,FontStyle.Bold,48); status.ForeColor=Color.FromArgb(30,105,104); layout.Controls.Add(status);
            detail=Label("直连 net3.zju.edu.cn · 每 30 秒检测一次",10,FontStyle.Regular,46); layout.Controls.Add(detail);
            meta=Label("认证状态 —    外网连通 —    在线设备 —",10,FontStyle.Regular,40); layout.Controls.Add(meta);
            var actions=new FlowLayoutPanel {AutoSize=true,Dock=DockStyle.Top,Margin=new Padding(0)};
            check=Button("立即检查并恢复",()=>Run(true)); actions.Controls.Add(check);
            actions.Controls.Add(Button("打开认证官网",()=>System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(ZjuPortal.Origin){UseShellExecute=true})));layout.Controls.Add(actions);
            layout.Controls.Add(Label("账户设置",13,FontStyle.Bold,48));
            user=new TextBox {Width=310,Text=settings.User,Margin=new Padding(0,4,0,10)};
            password=new TextBox {Width=310,UseSystemPasswordChar=true,Margin=new Padding(0,4,0,10)};
            layout.Controls.Add(Label("校园网账号",10,FontStyle.Regular,26)); layout.Controls.Add(user);
            layout.Controls.Add(Label("密码（留空保留已保存密码）",10,FontStyle.Regular,26)); layout.Controls.Add(password);
            passwordState=Label("",9,FontStyle.Regular,30);layout.Controls.Add(passwordState);
            user.TextChanged+=(s,e)=>UpdatePasswordState();password.TextChanged+=(s,e)=>UpdatePasswordState();
            RefreshPasswordState();
            var row=new FlowLayoutPanel {AutoSize=true,Dock=DockStyle.Top,Margin=new Padding(0)};
            save=Button("保存账户",SaveAccount); forget=Button("删除已存凭据",ForgetAccount); row.Controls.Add(save); row.Controls.Add(forget); layout.Controls.Add(row);
            auto=new CheckBox {Text="启用自动检测与恢复",Checked=enabled,AutoSize=true,Margin=new Padding(0,16,0,8)};
            auto.CheckedChanged+=(s,e)=>{enabled=auto.Checked;settings.Enabled=enabled;if(Volatile.Read(ref busy)==0) Persist();}; layout.Controls.Add(auto);
            startup=new CheckBox {Text="登录 Windows 后启动（托盘常驻）",Checked=Storage.Startup,AutoSize=true,Margin=new Padding(0,0,0,12)};
            startup.CheckedChanged+=(s,e)=>{try{Storage.SetStartup(startup.Checked,Application.ExecutablePath);}catch{MessageBox.Show("无法保存启动设置，请检查当前用户权限。");}}; layout.Controls.Add(startup);
            layout.Controls.Add(Label("密码由 Windows 当前用户加密保存。关闭窗口后继续在托盘运行。\n设备数超过 4 时，恢复本机认证可能使另一台设备下线。",9,FontStyle.Regular,52));
            layout.Controls.Add(Label("最近活动",13,FontStyle.Bold,38));
            events=new ListBox {Dock=DockStyle.Top,Height=115,BorderStyle=BorderStyle.None,BackColor=Color.White};layout.Controls.Add(events);
            return view;
        }
        static Label Label(string text,float size,FontStyle style,int height) {return new Label {Text=text,Font=new Font("Microsoft YaHei UI",size,style),Height=height,Dock=DockStyle.Top,AutoEllipsis=true,Margin=new Padding(0),TextAlign=ContentAlignment.MiddleLeft};}
        static Button Button(string text,Action click) { var b=new Button {Text=text,AutoSize=true,Height=36,MinimumSize=new Size(140,36),FlatStyle=FlatStyle.Flat,BackColor=Color.White,Margin=new Padding(0,4,10,4)};b.FlatAppearance.BorderColor=Color.FromArgb(213,221,230);b.Click+=(s,e)=>click();return b; }
        void Persist() {try{Storage.Save(settings);}catch{MessageBox.Show("无法保存设置，请检查本地应用数据目录权限。");}}
        void RefreshPasswordState(){try{savedPasswordState=Storage.Password().Length>0?1:0;}catch{savedPasswordState=2;}UpdatePasswordState();}
        void UpdatePasswordState(){
            if(passwordState==null)return;
            bool pending=password.Text.Length>0;
            bool changed=user.Text.Trim()!=settings.User;
            passwordState.Text=pending?"新密码尚未保存，请点击“保存账户”。":changed?"账号已修改，请同时输入新密码后保存。":savedPasswordState==1?"✓ 已保存密码（Windows 加密）；留空即可继续使用。":savedPasswordState==2?"已存密码无法读取，请重新输入并保存。":"尚未保存密码，请输入后点击“保存账户”。";
            passwordState.ForeColor=!pending&&!changed&&savedPasswordState==1?Color.FromArgb(30,105,104):Color.FromArgb(145,90,30);
        }
        void SaveAccount() {
            if(String.IsNullOrWhiteSpace(user.Text)){MessageBox.Show("请填写校园网账号。");return;}
            try {
                if(user.Text.Trim()!=settings.User && password.Text.Length==0){MessageBox.Show("更换账号时请同时输入密码。");return;}
                if(password.Text.Length>0) Storage.Password(password.Text);
                settings.User=user.Text.Trim(); Storage.Save(settings); password.Clear(); RefreshPasswordState(); AddEvent("账户设置已保存（Windows 用户加密）"); Run(false);
            } catch {MessageBox.Show("保存失败，请检查本地存储权限。");}
        }
        void ForgetAccount() {try {auto.Checked=false;Storage.Forget();password.Clear();RefreshPasswordState();AddEvent("凭据已删除，自动恢复已暂停");}catch{MessageBox.Show("删除失败，请检查本地存储权限。");}}
        string lastEvent="";
        void AddEvent(string message) {if(lastEvent==message)return;lastEvent=message;events.Items.Insert(0,DateTime.Now.ToString("HH:mm:ss")+"   "+message);while(events.Items.Count>80)events.Items.RemoveAt(events.Items.Count-1);}
        public void Start() {timer=new System.Threading.Timer(_=>RuntimeLog.Guard("campus-timer",()=>Run(false)),null,1000,30000);}
        void Run(bool manual) {
            if(disposed || (!manual && !enabled) || Interlocked.CompareExchange(ref busy,1,0)!=0)return;
            UI(()=>{check.Enabled=save.Enabled=forget.Enabled=auto.Enabled=false;});
            Task.Run(()=> {try{
                MonitorResult result;
                try{result=monitor.Check(manual);}catch{result=new MonitorResult {Title="检测未完成",Detail="凭据或本地设置不可用，请重新保存账户后重试。"};}
                enabled=settings.Enabled;
                UI(()=>{
                    status.Text=result.Title;detail.Text=result.Detail;
                    meta.Text="最近检查 "+result.CheckedAt.ToString("HH:mm:ss")+"    外网 "+(result.Internet==true?"可达":result.Internet==false?"待确认":"—")+"    在线设备 "+(result.Portal==null||result.Portal.Devices==""?"—":result.Portal.Devices+" / 4");
                    AddEvent(result.Title);auto.Checked=enabled;check.Enabled=save.Enabled=forget.Enabled=auto.Enabled=true;
                });
                }catch(Exception error){RuntimeLog.Write("campus-worker",error);}finally{Interlocked.Exchange(ref busy,0);}
            });
        }
        void UI(Action action) {if(disposed || view==null || !view.IsHandleCreated)return;try{view.BeginInvoke(new Action(()=>{if(!disposed)RuntimeLog.Guard("campus-ui",action);}));}catch(InvalidOperationException){}}
        public void Dispose(){disposed=true;if(timer!=null)timer.Dispose();}
    }
}

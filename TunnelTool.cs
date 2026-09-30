using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Workbench {
    public sealed class TunnelTool : IWorkbenchTool {
        public string Id {get{return "ssh-tunnels";}}
        public string Title {get{return "服务器隧道";}}
        public string Description {get{return "通过本机代理访问外网";}}
        readonly object gate=new object();
        readonly Dictionary<string,TunnelSession> sessions=new Dictionary<string,TunnelSession>();
        readonly Dictionary<string,string> results=new Dictionary<string,string>();
        readonly Dictionary<string,string> lastStates=new Dictionary<string,string>();
        readonly string configPath;
        TunnelConfig config;string loadError;
        Panel view;DataGridView grid;Label detail,saved;TextBox diagnostic;ListBox events;Button start,stop,edit,remove,test,copy;
        System.Threading.Timer timer;SynchronizationContext ui;int ticking,testing;string testingId;volatile bool disposed;
        public TunnelTool():this(TunnelConfig.DefaultPath){}
        internal TunnelTool(string path){configPath=path;try{config=TunnelConfig.Load(configPath);RuntimeLog.Write("tunnel-loaded-count-"+config.Profiles.Count,null);}catch(Exception e){RuntimeLog.Write("tunnel-load",e);config=new TunnelConfig();loadError="隧道配置无法读取。原文件已保留；请检查配置后点击重新加载。";}}
        public Control CreateView(){
            view=new Panel{Dock=DockStyle.Fill,Padding=new Padding(28),BackColor=Color.FromArgb(246,248,252),AutoScroll=true};
            var body=new TableLayoutPanel{Dock=DockStyle.Top,AutoSize=true,ColumnCount=1};view.Controls.Add(body);
            body.Controls.Add(Text("服务器隧道",25,FontStyle.Bold,54));
            body.Controls.Add(Text("把本机代理连接到校内服务器，让远程工作继续。",11,FontStyle.Regular,38));
            saved=Text("",9,FontStyle.Regular,32);body.Controls.Add(saved);
            var actions=new FlowLayoutPanel{AutoSize=true,Dock=DockStyle.Top,Margin=new Padding(0,8,0,12)};
            actions.Controls.Add(Button("新增服务器",()=>Edit(null)));edit=Button("编辑",()=>Edit(Selected()));actions.Controls.Add(edit);
            remove=Button("删除",Remove);actions.Controls.Add(remove);actions.Controls.Add(Button("重新加载",()=>ReloadSaved()));actions.Controls.Add(Button("配置目录",()=>System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(System.IO.Path.GetDirectoryName(configPath)){UseShellExecute=true})));body.Controls.Add(actions);
            grid=new DataGridView{Dock=DockStyle.Top,Height=205,ReadOnly=true,AllowUserToAddRows=false,AllowUserToDeleteRows=false,AllowUserToResizeRows=false,RowHeadersVisible=false,SelectionMode=DataGridViewSelectionMode.FullRowSelect,MultiSelect=false,BackgroundColor=Color.White,BorderStyle=BorderStyle.None,AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill};
            grid.DefaultCellStyle.SelectionBackColor=Color.FromArgb(222,239,236);grid.DefaultCellStyle.SelectionForeColor=Color.FromArgb(24,37,56);grid.RowTemplate.Height=38;grid.ColumnHeadersHeight=32;grid.ColumnHeadersHeightSizeMode=DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            grid.Columns.Add("name","服务器");grid.Columns.Add("route","远端 → 本机");grid.Columns.Add("state","连接状态");grid.Columns.Add("auto","自动启动");
            grid.Columns[0].FillWeight=22;grid.Columns[1].FillWeight=34;grid.Columns[2].FillWeight=29;grid.Columns[3].FillWeight=15;
            grid.SelectionChanged+=(s,e)=>Selection();body.Controls.Add(grid);
            var controls=new FlowLayoutPanel{AutoSize=true,Dock=DockStyle.Top,Margin=new Padding(0,12,0,6)};
            start=Button("启动",StartSelected);stop=Button("停止",StopSelected);test=Button("测试外网",()=>TestSelected());copy=Button("复制代理命令",CopyProxy);
            controls.Controls.AddRange(new Control[]{start,stop,test,copy});body.Controls.Add(controls);
            detail=Text("选择服务器，或点击新增服务器。",10,FontStyle.Regular,70);body.Controls.Add(detail);
            body.Controls.Add(Text("连接检查",12,FontStyle.Bold,36));
            diagnostic=new TextBox{Dock=DockStyle.Top,Height=126,Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical,BackColor=Color.White,BorderStyle=BorderStyle.FixedSingle};body.Controls.Add(diagnostic);
            body.Controls.Add(Text("远端仅监听 127.0.0.1。电脑和本机代理需保持运行。\nSSH 密钥复用 Windows ssh-agent，不保存服务器密码。",9,FontStyle.Regular,52));
            body.Controls.Add(Text("最近活动",12,FontStyle.Bold,32));events=new ListBox{Dock=DockStyle.Top,Height=90,BorderStyle=BorderStyle.None};body.Controls.Add(events);
            if(loadError!=null){detail.Text=loadError;diagnostic.Text=configPath;}
            foreach(var p in config.Profiles){sessions.Add(p.Id,new TunnelSession(p));AddRow(p);}
            SavedStatus();Selection();view.VisibleChanged+=(s,e)=>{if(view.Visible&&config.Profiles.Count==0)ReloadSaved();};return view;
        }
        void SavedStatus(){saved.Text=loadError!=null?"读取失败，未覆盖已存配置。":(config.RecoveredFromBackup?"已从备份恢复 ":"已保存 ")+config.Profiles.Count+" 条常用隧道 · 重启后保留；自动启动可在编辑中设置。";}
        bool ReloadSaved(){
            try{
                var next=TunnelConfig.Load(configPath);
                lock(gate){foreach(var session in sessions.Values)session.Dispose();sessions.Clear();config=next;foreach(var p in config.Profiles){var session=new TunnelSession(p);sessions.Add(p.Id,session);if(timer!=null&&p.AutoStart)session.Start();}}
                loadError=null;results.Clear();lastStates.Clear();grid.Rows.Clear();foreach(var p in config.Profiles)AddRow(p);SavedStatus();Selection();RuntimeLog.Write("tunnel-reloaded-count-"+config.Profiles.Count,null);return true;
            }catch(Exception error){RuntimeLog.Write("tunnel-reload",error);loadError="配置读取失败，现有连接和原文件已保留。";SavedStatus();detail.Text=loadError;return false;}
        }
        static Label Text(string text,float size,FontStyle style,int height){return new Label{Text=text,Dock=DockStyle.Top,Height=height,Font=new Font("Microsoft YaHei UI",size,style),TextAlign=ContentAlignment.MiddleLeft,AutoEllipsis=true,Margin=new Padding(0)};}
        static Button Button(string text,Action action){var b=new Button{Text=text,AutoSize=true,MinimumSize=new Size(85,36),FlatStyle=FlatStyle.Flat,BackColor=Color.White,Margin=new Padding(0,0,10,0)};b.FlatAppearance.BorderColor=Color.FromArgb(213,221,230);b.Click+=(s,e)=>action();return b;}
        void AddRow(TunnelProfile p){TunnelSession s;lock(gate)sessions.TryGetValue(p.Id,out s);int i=grid.Rows.Add(p.Name,p.RemotePort+" → "+p.LocalPort,s==null?"已停止":s.Snapshot().Status,p.AutoStart?"是":"否");grid.Rows[i].Tag=p.Id;}
        TunnelProfile Selected(){if(grid.SelectedRows.Count==0)return null;string id=(string)grid.SelectedRows[0].Tag;return config.Profiles.FirstOrDefault(p=>p.Id==id);}
        void Selection(){
            if(start==null)return;var p=Selected();bool exists=p!=null;
            start.Enabled=stop.Enabled=edit.Enabled=remove.Enabled=copy.Enabled=exists&&loadError==null;test.Enabled=exists&&Volatile.Read(ref testing)==0;
            if(!exists){detail.Text=loadError??(config.Profiles.Count==0?"还没有连接。点击“新增服务器”添加并保存常用隧道。":"请在上方选择已保存的常用隧道。");diagnostic.Text=loadError==null?"启动后点击“测试外网”，分别验证服务器监听范围和 HTTPS 出口。":configPath;return;}
            TunnelSession session;lock(gate)sessions.TryGetValue(p.Id,out session);if(session==null)return;
            var state=session.Snapshot();start.Enabled=!state.Desired;stop.Enabled=state.Desired||state.ProcessId!=0;
            detail.Text=p.User+"@"+p.Host+":"+p.SshPort+"    "+state.Status+"\n"+state.Detail;
            string result;diagnostic.Text=testingId==p.Id?"正在从服务器测试 HTTPS 出口，最长约 50 秒…":results.TryGetValue(p.Id,out result)?result:"尚未测试。点击“测试外网”检查服务器的 HTTPS 连接。";
        }
        void Edit(TunnelProfile existing){
            if(config.Profiles.Count==0&&!ReloadSaved())return;
            if(loadError!=null)return;
            using(var dialog=new TunnelEditor(existing)){
                if(dialog.ShowDialog(view.FindForm())!=DialogResult.OK)return;
                var replacement=dialog.Profile;var next=new TunnelConfig();next.Profiles=config.Profiles.Where(p=>existing==null||p.Id!=existing.Id).ToList();next.Profiles.Add(replacement);
                try{next.Save(configPath);}catch(Exception e){MessageBox.Show(e.Message,"无法保存连接");return;}
                lock(gate){if(existing!=null){sessions[existing.Id].Dispose();sessions.Remove(existing.Id);}sessions.Add(replacement.Id,new TunnelSession(replacement));}
                results.Remove(replacement.Id);config=next;grid.Rows.Clear();foreach(var p in config.Profiles)AddRow(p);grid.Rows[grid.Rows.Count-1].Selected=true;SavedStatus();Selection();Log(replacement.Name+"：已保存为常用隧道，重启后保留。 ");
            }
        }
        void Remove(){var p=Selected();if(p==null)return;if(MessageBox.Show("删除连接“"+p.Name+"”？只删除工作台配置，不删除密钥或服务器文件。","删除连接",MessageBoxButtons.OKCancel)!=DialogResult.OK)return;
            var next=new TunnelConfig();next.Profiles=config.Profiles.Where(x=>x.Id!=p.Id).ToList();try{next.Save(configPath);}catch{MessageBox.Show("保存失败，连接未删除。");return;}
            lock(gate){sessions[p.Id].Dispose();sessions.Remove(p.Id);}config=next;grid.Rows.RemoveAt(grid.SelectedRows[0].Index);SavedStatus();Selection();Log(p.Name+"：已删除连接");}
        void StartSelected(){var p=Selected();if(p==null)return;lock(gate)sessions[p.Id].Start();results.Remove(p.Id);Selection();}
        void StopSelected(){var p=Selected();if(p==null)return;lock(gate)sessions[p.Id].Stop();results.Remove(p.Id);Selection();Log(p.Name+"：已停止；随工作台启动的选项保持原设置");}
        void CopyProxy(){var p=Selected();if(p==null)return;Clipboard.SetText("export http_proxy=http://127.0.0.1:"+p.RemotePort+"\nexport https_proxy=\"$http_proxy\"\nexport HTTP_PROXY=\"$http_proxy\"\nexport HTTPS_PROXY=\"$http_proxy\"\nexport no_proxy=\"${no_proxy:+$no_proxy,}localhost,127.0.0.1,::1\"\nexport NO_PROXY=\"${NO_PROXY:+$NO_PROXY,}localhost,127.0.0.1,::1\"\n");Log(p.Name+"：已复制当前远端 shell 可用的代理命令");}
        async void TestSelected(){
            var p=Selected();if(p==null||Interlocked.CompareExchange(ref testing,1,0)!=0)return;testingId=p.Id;test.Enabled=false;diagnostic.Text="正在从服务器测试 HTTPS 出口，最长约 50 秒…";
            string result;
            try{var r=await Task.Run(()=>TunnelProbe.Run(p.Copy()));result=DateTime.Now.ToString("HH:mm:ss")+"  "+r.Summary+Environment.NewLine+r.Details;}
            catch(Exception e){result="测试未完成："+(e is TimeoutException?"SSH 或远端请求超时。":"请检查 SSH 认证、服务器可达性及 curl 安装情况。");}
            Interlocked.Exchange(ref testing,0);testingId=null;if(disposed)return;results[p.Id]=result;Log(p.Name+"："+result.Split('\n')[0]);Selection();
        }
        void Log(string text){events.Items.Insert(0,DateTime.Now.ToString("HH:mm:ss")+"  "+text);while(events.Items.Count>60)events.Items.RemoveAt(events.Items.Count-1);}
        public void Start(){
            if(timer!=null)return;
            if(config.Profiles.Count==0)ReloadSaved();
            ui=SynchronizationContext.Current;
            foreach(var p in config.Profiles)if(p.AutoStart)sessions[p.Id].Start();
            timer=new System.Threading.Timer(_=>{
                if(disposed||Interlocked.CompareExchange(ref ticking,1,0)!=0)return;
                try{
                    KeyValuePair<string,TunnelSession>[] current;lock(gate)current=sessions.ToArray();
                    var states=new Dictionary<string,TunnelSnapshot>();
                    foreach(var pair in current)RuntimeLog.Guard("tunnel-tick",()=>{pair.Value.Tick(DateTime.UtcNow);states[pair.Key]=pair.Value.Snapshot();});
                    if(ui!=null)ui.Post(x=>RuntimeLog.Guard("tunnel-ui",()=>{if(disposed)return;foreach(DataGridViewRow row in grid.Rows){string id=row.Tag as string;TunnelSnapshot state;if(id==null||!states.TryGetValue(id,out state))continue;row.Cells[2].Value=state.Status;string before;if(!lastStates.TryGetValue(id,out before)||before!=state.Status){Log(row.Cells[0].Value+"："+state.Status);lastStates[id]=state.Status;}}Selection();}),null);
                }catch(Exception error){RuntimeLog.Write("tunnel-timer",error);}finally{Interlocked.Exchange(ref ticking,0);}
            },null,0,1000);
        }
        public void Dispose(){disposed=true;if(timer!=null)timer.Dispose();lock(gate)foreach(var session in sessions.Values)session.Dispose();}
    }
    sealed class TunnelEditor : Form {
        public TunnelProfile Profile;
        readonly TextBox name=new TextBox(),host=new TextBox(),user=new TextBox(),key=new TextBox();
        readonly NumericUpDown ssh=Port(22,1),remote=Port(18080,1024),local=Port(7897,1);
        readonly CheckBox auto=new CheckBox{Text="随工作台启动此连接",AutoSize=true};
        static NumericUpDown Port(int value,int min){return new NumericUpDown{Minimum=min,Maximum=65535,Value=value,Width=150};}
        public TunnelEditor(TunnelProfile original){
            Profile=original==null?new TunnelProfile():original.Copy();Text=original==null?"新增服务器隧道":"编辑服务器隧道";Font=new Font("Microsoft YaHei UI",10);ClientSize=new Size(570,440);FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=false;MinimizeBox=false;StartPosition=FormStartPosition.CenterParent;
            var table=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(20),ColumnCount=2,RowCount=10};table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,145));table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));Controls.Add(table);
            name.Text=Profile.Name;host.Text=Profile.Host;user.Text=Profile.User;key.Text=Profile.IdentityFile;ssh.Value=Profile.SshPort;remote.Value=Profile.RemotePort;local.Value=Profile.LocalPort;auto.Checked=Profile.AutoStart;
            string[] labels={"连接名称","服务器 IP / 域名","SSH 用户名","SSH 端口","服务器代理端口","本机代理端口","密钥文件（可留空）"};Control[] fields={name,host,user,ssh,remote,local,key};
            for(int i=0;i<fields.Length;i++){table.Controls.Add(new Label{Text=labels[i],AutoSize=true,Margin=new Padding(0,8,4,0)},0,i);fields[i].Dock=DockStyle.Fill;table.Controls.Add(fields[i],1,i);}
            table.Controls.Add(auto,1,7);
            var hint=new Label{Text="密钥由 ssh-agent 解锁。监听与本机目标均限定 127.0.0.1。",Dock=DockStyle.Fill,Height=40};table.Controls.Add(hint,0,8);table.SetColumnSpan(hint,2);
            var buttons=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft};var save=new Button{Text="保存",AutoSize=true};var cancel=new Button{Text="取消",DialogResult=DialogResult.Cancel,AutoSize=true};buttons.Controls.Add(save);buttons.Controls.Add(cancel);table.Controls.Add(buttons,1,9);AcceptButton=save;CancelButton=cancel;
            save.Click+=(s,e)=>{try{Profile.Name=name.Text.Trim();Profile.Host=host.Text.Trim();Profile.User=user.Text.Trim();Profile.IdentityFile=key.Text.Trim();Profile.SshPort=(int)ssh.Value;Profile.RemotePort=(int)remote.Value;Profile.LocalPort=(int)local.Value;Profile.AutoStart=auto.Checked;Profile.Validate();DialogResult=DialogResult.OK;}catch(Exception ex){MessageBox.Show(ex.Message,"检查连接设置");}};
        }
    }
}

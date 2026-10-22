using System;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
namespace Workbench {
    static class OrganizerUi {
        public static Button Button(string text,string name,Action action){var button=new Button{Text=text,Name=name,AutoSize=true,MinimumSize=new Size(86,36),Margin=new Padding(0,4,8,4),FlatStyle=FlatStyle.Flat,BackColor=Color.White};button.Click+=(s,e)=>action();return button;}
        public static Panel Page(string title,string description,out FlowLayoutPanel actions,out Panel body,out Label status){
            var page=new Panel{Dock=DockStyle.Fill,BackColor=Color.FromArgb(246,248,252),Font=new Font("Microsoft YaHei UI",10)};
            var table=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(24),ColumnCount=1,RowCount=5};page.Controls.Add(table);
            foreach(float height in new[]{60,52,54})table.RowStyles.Add(new RowStyle(SizeType.Absolute,height));table.RowStyles.Add(new RowStyle(SizeType.Percent,100));table.RowStyles.Add(new RowStyle(SizeType.Absolute,64));
            table.Controls.Add(new Label{Text=title,Dock=DockStyle.Fill,Font=new Font("Microsoft YaHei UI",25,FontStyle.Bold),TextAlign=ContentAlignment.MiddleLeft},0,0);
            table.Controls.Add(new Label{Text=description,Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft},0,1);
            actions=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=false};table.Controls.Add(actions,0,2);
            body=new Panel{Dock=DockStyle.Fill};table.Controls.Add(body,0,3);
            status=new Label{Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft,ForeColor=Color.FromArgb(30,105,104)};table.Controls.Add(status,0,4);return page;
        }
        public static DataGridView Grid(string name){return new DataGridView{Name=name,AccessibleName=name,Dock=DockStyle.Fill,ReadOnly=true,AllowUserToAddRows=false,AllowUserToDeleteRows=false,AllowUserToResizeRows=false,MultiSelect=false,SelectionMode=DataGridViewSelectionMode.FullRowSelect,RowHeadersVisible=false,AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill,BackgroundColor=Color.White,BorderStyle=BorderStyle.None,AutoSizeRowsMode=DataGridViewAutoSizeRowsMode.AllCells,ColumnHeadersHeightSizeMode=DataGridViewColumnHeadersHeightSizeMode.AutoSize};}
        public static Form Editor(string title,string name,int[] rows,out TableLayoutPanel table){
            var form=new Form{Text=title,Name=name,Font=new Font("Microsoft YaHei UI",10),ClientSize=new Size(600,rows.Sum()+40),FormBorderStyle=FormBorderStyle.FixedDialog,MaximizeBox=false,MinimizeBox=false,ShowInTaskbar=false,StartPosition=FormStartPosition.CenterParent};
            table=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(20),ColumnCount=2,RowCount=rows.Length};table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,110));table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));foreach(int height in rows)table.RowStyles.Add(new RowStyle(SizeType.Absolute,height));form.Controls.Add(table);return form;
        }
        public static void Field(TableLayoutPanel table,int row,string title,Control field){table.Controls.Add(new Label{Text=title,AutoSize=true,Margin=new Padding(0,8,8,0)},0,row);field.Dock=DockStyle.Fill;field.AccessibleName=title;table.Controls.Add(field,1,row);}
        public static void Footer(Form form,TableLayoutPanel table,int row,Action save){
            var error=new Label{Dock=DockStyle.Fill,ForeColor=Color.Firebrick};table.Controls.Add(error,0,row);table.SetColumnSpan(error,2);
            var buttons=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft};var cancel=Button("取消","cancel",()=>form.DialogResult=DialogResult.Cancel);var accept=Button("保存","save",()=>{
                try{save();form.DialogResult=DialogResult.OK;}catch(ArgumentException e){error.Text=e.Message;}catch(System.IO.InvalidDataException e){error.Text=e.Message;}catch{error.Text="保存失败，原有记录未替换。请检查数据文件和目录权限。";}
            });buttons.Controls.Add(cancel);buttons.Controls.Add(accept);table.Controls.Add(buttons,0,row+1);table.SetColumnSpan(buttons,2);form.AcceptButton=accept;form.CancelButton=cancel;
        }
        public static bool Confirm(Control owner,string text){return MessageBox.Show(owner,text,"确认删除",MessageBoxButtons.YesNo,MessageBoxIcon.Question,MessageBoxDefaultButton.Button2)==DialogResult.Yes;}
    }
    public sealed class BookmarkTool : IWorkbenchTool {
        public string Id {get{return "bookmarks";}}
        public string Title {get{return "网址收藏";}}
        public string Description {get{return "收藏夹与常用网址";}}
        readonly OrganizerStore store;readonly Action<string> open;
        Panel view;ListBox folders;DataGridView grid;Label status;Button add,edit,remove,launch,addFolder,renameFolder,deleteFolder;bool refreshing;
        public BookmarkTool(OrganizerStore store):this(store,url=>Process.Start(new ProcessStartInfo(url){UseShellExecute=true})){}
        internal BookmarkTool(OrganizerStore store,Action<string> open){this.store=store;this.open=open;}
        public Control CreateView(){
            FlowLayoutPanel actions;Panel body;view=OrganizerUi.Page(Title,"把常用网站放进收藏夹。单击网址名称，即可用系统默认浏览器打开。",out actions,out body,out status);
            add=OrganizerUi.Button("添加网址","add-bookmark",()=>Edit(null));launch=OrganizerUi.Button("打开网址","open-bookmark",Open);edit=OrganizerUi.Button("编辑","edit-bookmark",()=>Edit(Selected()));remove=OrganizerUi.Button("删除","remove-bookmark",Remove);
            actions.Controls.AddRange(new Control[]{add,launch,edit,remove,OrganizerUi.Button("重新加载","reload-bookmarks",()=>{store.Reload();Refresh();})});
            var columns=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=1};columns.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,184));columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));body.Controls.Add(columns);
            var left=new Panel{Dock=DockStyle.Fill,Padding=new Padding(0,0,12,0)};columns.Controls.Add(left,0,0);
            folders=new ListBox{Name="bookmark-folders",AccessibleName="收藏夹",Dock=DockStyle.Fill,IntegralHeight=false};left.Controls.Add(folders);
            var folderActions=new FlowLayoutPanel{Dock=DockStyle.Bottom,Height=134,FlowDirection=FlowDirection.TopDown,WrapContents=false};left.Controls.Add(folderActions);
            addFolder=OrganizerUi.Button("新建收藏夹","add-folder",()=>FolderName(false));renameFolder=OrganizerUi.Button("收藏夹改名","rename-folder",()=>FolderName(true));deleteFolder=OrganizerUi.Button("删除空收藏夹","delete-folder",RemoveFolder);folderActions.Controls.AddRange(new Control[]{addFolder,renameFolder,deleteFolder});
            grid=OrganizerUi.Grid("bookmark-grid");grid.Columns.Add(new DataGridViewLinkColumn{Name="title",HeaderText="名称（点击打开）",FillWeight=42,LinkColor=Color.FromArgb(20,105,120),TrackVisitedState=false});grid.Columns.Add("url","网址");columns.Controls.Add(grid,1,0);
            folders.SelectedIndexChanged+=(s,e)=>{if(!refreshing)RefreshLinks();};grid.SelectionChanged+=(s,e)=>Selection();grid.CellContentClick+=(s,e)=>{if(e.RowIndex>=0&&e.ColumnIndex==0)Open();};
            view.VisibleChanged+=(s,e)=>{if(view.Visible)Refresh();};Refresh();return view;
        }
        string Folder {get{return folders.SelectedItem as string??"未分类";}}
        BookmarkEntry Selected(){string id=grid.CurrentRow==null?null:grid.CurrentRow.Tag as string;return store.Data.Bookmarks.FirstOrDefault(x=>x.Id==id);}
        void Refresh(){string folder=Folder;refreshing=true;folders.Items.Clear();folders.Items.AddRange(store.Data.Folders.Cast<object>().ToArray());folders.SelectedItem=store.Data.Folders.Contains(folder)?folder:"未分类";refreshing=false;RefreshLinks();}
        void RefreshLinks(){
            string id=Selected()==null?null:Selected().Id;grid.Rows.Clear();foreach(var entry in store.Data.Bookmarks.Where(x=>x.Folder==Folder).OrderBy(x=>x.Title)){int row=grid.Rows.Add(entry.Title,entry.Url);grid.Rows[row].Tag=entry.Id;if(entry.Id==id)grid.CurrentCell=grid.Rows[row].Cells[0];}
            status.Text=store.Notice.Length>0?store.Notice:Folder+" · "+grid.Rows.Count+" 个网址";Selection();
        }
        void Selection(){bool chosen=Selected()!=null;add.Enabled=addFolder.Enabled=store.CanWrite;edit.Enabled=remove.Enabled=store.CanWrite&&chosen;launch.Enabled=chosen;renameFolder.Enabled=deleteFolder.Enabled=store.CanWrite&&Folder!="未分类";foreach(DataGridViewRow row in grid.Rows){var cell=(DataGridViewLinkCell)row.Cells[0];cell.LinkColor=cell.ActiveLinkColor=row.Selected?cell.InheritedStyle.SelectionForeColor:Color.FromArgb(20,105,120);}}
        void Open(){var entry=Selected();if(entry==null)return;try{open(BookmarkEntry.Address(entry.Url));status.Text="已交给默认浏览器打开。";}catch{status.Text="无法打开网址，请检查系统默认浏览器设置。";}}
        void Edit(BookmarkEntry original){
            if(!store.CanWrite)return;var entry=original==null?new BookmarkEntry{Folder=Folder}:original.Copy();TableLayoutPanel table;
            using(var form=OrganizerUi.Editor(original==null?"添加网址":"编辑网址","bookmark-editor",new[]{38,38,38,48,54,48},out table)){
                var title=new TextBox{Name="title",Text=entry.Title,MaxLength=200};var url=new TextBox{Name="url",Text=entry.Url,MaxLength=4096};var folder=new ComboBox{Name="folder",DropDownStyle=ComboBoxStyle.DropDownList};folder.Items.AddRange(store.Data.Folders.Cast<object>().ToArray());folder.SelectedItem=entry.Folder;
                OrganizerUi.Field(table,0,"网址名称",title);OrganizerUi.Field(table,1,"网址",url);OrganizerUi.Field(table,2,"收藏夹",folder);var hint=new Label{Text="可省略 https://；支持 http 和 https 的网页。",Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft};table.Controls.Add(hint,0,3);table.SetColumnSpan(hint,2);
                OrganizerUi.Footer(form,table,4,()=>{entry.Title=title.Text;entry.Url=url.Text;entry.Folder=folder.SelectedItem as string;store.Edit(d=>d.PutBookmark(entry));});if(form.ShowDialog(view)==DialogResult.OK)RefreshLinks();
            }
        }
        void FolderName(bool rename){
            if(!store.CanWrite)return;string from=Folder;TableLayoutPanel table;
            using(var form=OrganizerUi.Editor(rename?"收藏夹改名":"新建收藏夹","organizer-name-editor",new[]{42,54,48},out table)){
                var name=new TextBox{Name="name",Text=rename?from:"",MaxLength=80};OrganizerUi.Field(table,0,"收藏夹名称",name);
                OrganizerUi.Footer(form,table,1,()=>store.Edit(d=>{if(rename)d.RenameFolder(from,name.Text);else d.AddFolder(name.Text);}));
                if(form.ShowDialog(view)==DialogResult.OK){Refresh();folders.SelectedItem=name.Text.Trim();}
            }
        }
        void Remove(){var entry=Selected();if(entry==null||!store.CanWrite||!OrganizerUi.Confirm(view,"删除选中的网址收藏？"))return;Change(d=>d.RemoveBookmark(entry.Id));}
        void RemoveFolder(){if(!store.CanWrite)return;if(store.Data.Bookmarks.Any(x=>x.Folder==Folder)){status.Text="请先移动或删除收藏夹内的网址。";return;}string folder=Folder;if(OrganizerUi.Confirm(view,"删除空收藏夹“"+folder+"”？"))Change(d=>d.RemoveFolder(folder));}
        void Change(Action<OrganizerData> change){try{store.Edit(change);Refresh();}catch(ArgumentException e){status.Text=e.Message;}catch{status.Text="保存失败，原有记录已保留。";}}
        public void Start(){}
        public void Dispose(){if(view!=null)view.Dispose();}
    }
    public sealed class ScheduleTool : IWorkbenchTool {
        public string Id {get{return "schedule";}}
        public string Title {get{return "待办 / 日程";}}
        public string Description {get{return "记录安排与完成进度";}}
        readonly OrganizerStore store;Panel view;DataGridView grid;ComboBox filter;Label status;Button add,edit,remove,toggle;
        public ScheduleTool(OrganizerStore store){this.store=store;}
        public Control CreateView(){
            FlowLayoutPanel actions;Panel body;view=OrganizerUi.Page(Title,"记录待办、备注和可选日期时间。这里用于记录安排，不发送到期提醒。",out actions,out body,out status);
            add=OrganizerUi.Button("添加记录","add-task",()=>Edit(null));edit=OrganizerUi.Button("编辑","edit-task",()=>Edit(Selected()));toggle=OrganizerUi.Button("标为完成","toggle-task",()=>{var entry=Selected();if(entry!=null)Change(d=>d.ToggleTask(entry.Id));});remove=OrganizerUi.Button("删除","remove-task",Remove);
            filter=new ComboBox{Name="schedule-filter",AccessibleName="完成状态筛选",Width=104,DropDownStyle=ComboBoxStyle.DropDownList,Margin=new Padding(0,9,8,0)};filter.Items.AddRange(new object[]{"未完成","全部","已完成"});filter.SelectedIndex=0;
            actions.Controls.AddRange(new Control[]{add,edit,toggle,remove,filter,OrganizerUi.Button("重新加载","reload-schedule",()=>{store.Reload();Refresh();})});
            grid=OrganizerUi.Grid("schedule-grid");grid.Columns.Add(new DataGridViewCheckBoxColumn{Name="done",HeaderText="完成",FillWeight=12});grid.Columns.Add(new DataGridViewTextBoxColumn{Name="title",HeaderText="标题",FillWeight=30});grid.Columns.Add(new DataGridViewTextBoxColumn{Name="due",HeaderText="日期时间",FillWeight=30});grid.Columns.Add(new DataGridViewTextBoxColumn{Name="notes",HeaderText="备注",FillWeight=28});body.Controls.Add(grid);
            grid.SelectionChanged+=(s,e)=>Selection();grid.CellDoubleClick+=(s,e)=>{if(e.RowIndex>=0)Edit(Selected());};filter.SelectedIndexChanged+=(s,e)=>Refresh();view.VisibleChanged+=(s,e)=>{if(view.Visible)Refresh();};Refresh();return view;
        }
        ScheduleEntry Selected(){string id=grid.CurrentRow==null?null:grid.CurrentRow.Tag as string;return store.Data.Tasks.FirstOrDefault(x=>x.Id==id);}
        void Refresh(){
            string selected=Selected()==null?null:Selected().Id;grid.Rows.Clear();foreach(var entry in store.Data.TaskItems(filter.SelectedIndex)){
                DateTime due;bool dated=DateTime.TryParseExact(entry.DueAt,"yyyy-MM-dd'T'HH:mm",CultureInfo.InvariantCulture,DateTimeStyles.None,out due);bool overdue=dated&&!entry.Completed&&due<DateTime.Now;
                int row=grid.Rows.Add(entry.Completed,entry.Title,dated?due.ToString("yyyy-MM-dd HH:mm")+(overdue?"（已过期）":""):"未安排日期",entry.Notes.Replace("\r","").Replace("\n"," · "));grid.Rows[row].Tag=entry.Id;grid.Rows[row].Cells[3].ToolTipText=entry.Notes;
                if(overdue)grid.Rows[row].DefaultCellStyle.ForeColor=Color.Firebrick;if(entry.Id==selected)grid.CurrentCell=grid.Rows[row].Cells[1];
            }
            status.Text=store.Notice.Length>0?store.Notice:"未完成 "+store.Data.Tasks.Count(x=>!x.Completed)+" 项 · 已完成 "+store.Data.Tasks.Count(x=>x.Completed)+" 项";Selection();
        }
        void Selection(){var entry=Selected();add.Enabled=store.CanWrite;edit.Enabled=remove.Enabled=toggle.Enabled=store.CanWrite&&entry!=null;toggle.Text=entry!=null&&entry.Completed?"恢复未完成":"标为完成";}
        void Edit(ScheduleEntry original){
            if(!store.CanWrite)return;var entry=original==null?new ScheduleEntry():original.Copy();TableLayoutPanel table;
            using(var form=OrganizerUi.Editor(original==null?"添加待办 / 日程":"编辑待办 / 日程","schedule-editor",new[]{38,42,130,44,54,48},out table)){
                var title=new TextBox{Name="title",Text=entry.Title,MaxLength=200};var notes=new TextBox{Name="notes",Text=entry.Notes,MaxLength=4000,Multiline=true,ScrollBars=ScrollBars.Vertical};var due=new DateTimePicker{Name="due",Format=DateTimePickerFormat.Custom,CustomFormat="yyyy-MM-dd HH:mm",ShowCheckBox=true};
                DateTime date;if(DateTime.TryParseExact(entry.DueAt,"yyyy-MM-dd'T'HH:mm",CultureInfo.InvariantCulture,DateTimeStyles.None,out date)){due.Value=date;due.Checked=true;}else due.Checked=false;
                OrganizerUi.Field(table,0,"标题",title);OrganizerUi.Field(table,1,"日期时间",due);OrganizerUi.Field(table,2,"备注",notes);var hint=new Label{Text="不勾选日期，即保存为无日期待办；时间按本机当地时间记录。",Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft};table.Controls.Add(hint,0,3);table.SetColumnSpan(hint,2);
                OrganizerUi.Footer(form,table,4,()=>{entry.Title=title.Text;entry.Notes=notes.Text;entry.DueAt=due.Checked?due.Value.ToString("yyyy-MM-dd'T'HH:mm",CultureInfo.InvariantCulture):"";store.Edit(d=>d.PutTask(entry));});if(form.ShowDialog(view)==DialogResult.OK)Refresh();
            }
        }
        void Remove(){var entry=Selected();if(entry==null||!store.CanWrite||!OrganizerUi.Confirm(view,"删除选中的待办 / 日程记录？"))return;Change(d=>d.RemoveTask(entry.Id));}
        void Change(Action<OrganizerData> change){if(!store.CanWrite)return;try{store.Edit(change);Refresh();}catch{status.Text="保存失败，原有记录已保留。";}}
        public void Start(){}
        public void Dispose(){if(view!=null)view.Dispose();}
    }
}

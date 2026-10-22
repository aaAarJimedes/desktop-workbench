using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using Workbench;
class OrganizerViewTests {
    static int count;
    static void Check(bool ok,string name){if(!ok)throw new Exception(name);count++;Console.WriteLine("PASS "+name);}
    static T Find<T>(Control root,string name) where T:Control{return root.Controls.Find(name,true).OfType<T>().Single();}
    static void Dialog(Button button,string name,Action<Form> edit){
        Exception failure=null;bool handled=false;DateTime deadline=DateTime.UtcNow.AddSeconds(5);
        using(var timer=new Timer{Interval=30}){
            timer.Tick+=(s,e)=>{
                var form=Application.OpenForms.Cast<Form>().FirstOrDefault(x=>x.Name==name);
                if(form==null){if(DateTime.UtcNow>deadline){failure=new Exception("editor did not open");timer.Stop();}return;}
                timer.Stop();try{edit(form);handled=true;}catch(Exception ex){failure=ex;form.Close();}
            };
            timer.Start();button.PerformClick();
        }
        if(failure!=null)throw failure;if(!handled)throw new Exception("editor action did not execute");
    }
    static Form Host(Control view){var form=new Form{ClientSize=new Size(820,870),ShowInTaskbar=false,StartPosition=FormStartPosition.Manual,Location=new Point(-3000,-3000)};view.Dock=DockStyle.Fill;form.Controls.Add(view);form.Show();Application.DoEvents();return form;}
    static void Preview(Control view,string file){using(var bitmap=new Bitmap(view.Width,view.Height)){view.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,file));}}
    [STAThread] static int Main(){
        Application.EnableVisualStyles();string root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"organizer-view-test-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try{
            string path=Path.Combine(root,"organizer.json"),opened=null;var store=new OrganizerStore(path);
            store.Edit(d=>d.AddFolder("研究"));store.Edit(d=>d.PutBookmark(new BookmarkEntry{Title="浙江大学",Url="https://www.zju.edu.cn/",Folder="研究"}));
            using(var tool=new BookmarkTool(store,url=>opened=url))using(var form=Host(tool.CreateView())){
                var view=form.Controls[0];var folders=Find<ListBox>(view,"bookmark-folders");var grid=Find<DataGridView>(view,"bookmark-grid");
                folders.SelectedItem="研究";Check(grid.Rows.Count==1,"selecting a folder displays its saved bookmarks");
                var cell=(DataGridViewLinkCell)grid.Rows[0].Cells[0];Check(cell.LinkColor==cell.InheritedStyle.SelectionForeColor,"selected bookmark links remain readable on the selection background");
                Find<Button>(view,"open-bookmark").PerformClick();Check(opened=="https://www.zju.edu.cn/","Open sends the selected HTTP address to the browser boundary");
                opened=null;typeof(DataGridView).GetMethod("OnCellContentClick",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(grid,new object[]{new DataGridViewCellEventArgs(0,0)});
                Check(opened=="https://www.zju.edu.cn/","a single click on the title opens the saved address");
                Dialog(Find<Button>(view,"add-folder"),"organizer-name-editor",f=>{Find<TextBox>(f,"name").Text="常用";Find<Button>(f,"save").PerformClick();});
                Check(new OrganizerStore(path).Data.Folders.Contains("常用")&&folders.SelectedItem.ToString()=="常用","the folder dialog saves and selects the new folder");
                Dialog(Find<Button>(view,"add-bookmark"),"bookmark-editor",f=>{Find<TextBox>(f,"title").Text="示例网址";Find<TextBox>(f,"url").Text="example.org";Find<Button>(f,"save").PerformClick();});
                Check(grid.Rows.Count==1&&new OrganizerStore(path).Data.Bookmarks.Any(x=>x.Folder=="常用"&&x.Url=="https://example.org/"),"the bookmark dialog saves into the selected folder");
                Dialog(Find<Button>(view,"edit-bookmark"),"bookmark-editor",f=>{Find<TextBox>(f,"title").Text="已移动的网址";Find<ComboBox>(f,"folder").SelectedItem="研究";Find<Button>(f,"save").PerformClick();});
                Check(grid.Rows.Count==0&&store.Data.Bookmarks.Any(x=>x.Title=="已移动的网址"&&x.Folder=="研究"),"editing a bookmark moves it between folders");
                folders.SelectedItem="研究";Preview(view,"bookmarks-preview.png");
            }
            using(var tool=new ScheduleTool(store))using(var form=Host(tool.CreateView())){
                var view=form.Controls[0];var grid=Find<DataGridView>(view,"schedule-grid");var filter=Find<ComboBox>(view,"schedule-filter");
                Dialog(Find<Button>(view,"add-task"),"schedule-editor",f=>{Find<TextBox>(f,"title").Text="周会准备";Find<TextBox>(f,"notes").Text="整理实验结果";var due=Find<DateTimePicker>(f,"due");due.Value=new DateTime(2026,10,23,14,30,0);due.Checked=true;Find<Button>(f,"save").PerformClick();});
                Check(grid.Rows.Count==1&&new OrganizerStore(path).Data.Tasks.Single().DueAt=="2026-10-23T14:30","the schedule dialog persists the chosen local date and time");
                Dialog(Find<Button>(view,"edit-task"),"schedule-editor",f=>{Find<TextBox>(f,"notes").Text="保留这条备注";Find<Button>(f,"save").PerformClick();});
                Check(store.Data.Tasks.Single().Notes=="保留这条备注","the schedule edit action preserves its record");
                Find<Button>(view,"toggle-task").PerformClick();Check(grid.Rows.Count==0&&new OrganizerStore(path).Data.Tasks.Single().Completed,"completing a task saves it and removes it from pending");
                filter.SelectedIndex=2;Check(grid.Rows.Count==1,"the completed filter displays the finished task");
                Find<Button>(view,"toggle-task").PerformClick();filter.SelectedIndex=0;Check(grid.Rows.Count==1&&!store.Data.Tasks.Single().Completed,"restoring a task returns it to the pending list");
                Preview(view,"schedule-preview.png");
            }
            File.WriteAllText(path,"bad");File.WriteAllText(path+".bak","bad");var broken=new OrganizerStore(path);
            using(var tool=new BookmarkTool(broken,url=>{}))using(var view=tool.CreateView())Check(!Find<Button>(view,"add-bookmark").Enabled&&!Find<Button>(view,"add-folder").Enabled,"bookmark editing is disabled for unreadable data");
            using(var tool=new ScheduleTool(broken))using(var view=tool.CreateView())Check(!Find<Button>(view,"add-task").Enabled,"schedule editing is disabled for unreadable data");
            Console.WriteLine(count+" organizer view checks passed");return 0;
        }catch(Exception e){Console.WriteLine("FAIL "+e);return 1;}
        finally{Directory.Delete(root,true);}
    }
}

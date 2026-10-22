using System;
using System.IO;
using System.Linq;
using Workbench;
class OrganizerTests {
    static int count;
    static void Check(bool ok,string name){if(!ok)throw new Exception(name);count++;Console.WriteLine("PASS "+name);}
    static void Reject(Action action,string name){bool rejected=false;try{action();}catch(ArgumentException){rejected=true;}catch(InvalidDataException){rejected=true;}Check(rejected,name);}
    static int Main(){
        string root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"organizer-test-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try{
            string path=Path.Combine(root,"organizer.json");var store=new OrganizerStore(path);
            Check(store.CanWrite&&store.Data.Folders.SequenceEqual(new[]{"未分类"})&&!File.Exists(path),"first use is empty and creates no personal file");
            store.Edit(d=>d.AddFolder("  研究  "));
            string before=File.ReadAllText(path);
            Reject(()=>store.Edit(d=>d.AddFolder("研究")),"duplicate folders are rejected");
            Check(File.ReadAllText(path)==before&&store.Data.Folders.Count==2,"invalid edits preserve disk and visible data");
            var link=new BookmarkEntry{Title="常用论文",Url="example.org/papers?q=a#results",Folder="研究"};store.Edit(d=>d.PutBookmark(link));
            var reopened=new OrganizerStore(path);
            Check(reopened.Data.Bookmarks.Single().Url=="https://example.org/papers?q=a#results","bookmarks persist a browser-safe normalized address");
            var edited=link.Copy();edited.Title="编辑后";edited.Url="http://localhost:8080/read";store.Edit(d=>d.PutBookmark(edited));
            Check(new OrganizerStore(path).Data.Bookmarks.Single().Title=="编辑后","editing replaces the same bookmark instead of duplicating it");
            store.Edit(d=>d.RenameFolder("研究","阅读"));
            Check(new OrganizerStore(path).Data.Bookmarks.Single().Folder=="阅读","renaming a folder preserves its bookmark membership");
            Reject(()=>store.Edit(d=>d.RemoveFolder("阅读")),"a nonempty folder cannot silently delete bookmarks");
            Reject(()=>store.Edit(d=>d.RemoveFolder("未分类")),"the default folder remains available");
            foreach(string url in new[]{"javascript:alert(1)","file:///C:/Windows/notepad.exe","data:text/html,hello","https://user:secret@example.org/","https://example.org/\nother","https://"}){
                string invalid=url;Reject(()=>store.Edit(d=>d.PutBookmark(new BookmarkEntry{Title="unsafe",Url=invalid})),"unsafe address rejected: "+url.Split(':')[0]);
            }
            Reject(()=>store.Edit(d=>d.PutBookmark(new BookmarkEntry{Title=" ",Url="https://example.org"})),"empty bookmark title is rejected");
            Reject(()=>store.Edit(d=>d.PutBookmark(new BookmarkEntry{Title="orphan",Url="https://example.org",Folder="missing"})),"bookmarks must belong to an existing folder");
            store.Edit(d=>d.RemoveBookmark(link.Id));store.Edit(d=>d.RemoveFolder("阅读"));
            Check(new OrganizerStore(path).Data.Bookmarks.Count==0&&store.Data.Folders.Count==1,"explicit bookmark and empty-folder deletion survive reload");
            var task=new ScheduleEntry{Title="周会",Notes="带上实验记录\n第二行",DueAt="2026-10-23T14:30"};
            store.Edit(d=>d.PutTask(task));store.Edit(d=>d.PutTask(new ScheduleEntry{Title="无日期待办"}));store.Edit(d=>d.PutTask(new ScheduleEntry{Title="更早",DueAt="2026-10-22T09:00"}));
            reopened=new OrganizerStore(path);
            Check(reopened.Data.TaskItems(0).Select(x=>x.Title).SequenceEqual(new[]{"更早","周会","无日期待办"}),"pending tasks sort by due time with undated tasks last");
            Check(reopened.Data.Tasks.First(x=>x.Id==task.Id).Notes=="带上实验记录\n第二行","schedule notes and local date-time survive reload");
            var taskEdit=task.Copy();taskEdit.Title="已改期";taskEdit.DueAt="2026-10-24T15:00";store.Edit(d=>d.PutTask(taskEdit));store.Edit(d=>d.ToggleTask(task.Id));
            reopened=new OrganizerStore(path);
            Check(reopened.Data.TaskItems(0).Count()==2&&reopened.Data.TaskItems(2).Single().Title=="已改期"&&reopened.Data.TaskItems(1).Count()==3,"completion and filters persist across restart");
            store.Edit(d=>d.ToggleTask(task.Id));Check(new OrganizerStore(path).Data.TaskItems(2).Count()==0,"a completed task can be restored");
            Reject(()=>store.Edit(d=>d.PutTask(new ScheduleEntry{Title="bad date",DueAt="2026-02-30T10:00"})),"invalid calendar dates are rejected");
            Reject(()=>store.Edit(d=>d.PutTask(new ScheduleEntry{Title="unsupported date",DueAt="0001-01-01T10:00"})),"dates outside the native editor range are rejected");
            Reject(()=>store.Edit(d=>d.PutTask(new ScheduleEntry{Title=" "})),"empty task titles are rejected");
            store.Edit(d=>d.RemoveTask(task.Id));Check(!new OrganizerStore(path).Data.Tasks.Any(x=>x.Id==task.Id),"explicit task deletion persists");
            before=File.ReadAllText(path);int folders=store.Data.Folders.Count;bool failed=false;
            using(var locked=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read)){try{store.Edit(d=>d.AddFolder("cannot save"));}catch(IOException){failed=true;}}
            Check(failed&&store.Data.Folders.Count==folders&&File.ReadAllText(path)==before,"failed save keeps both in-memory and on-disk data unchanged");
            Check(Directory.GetFiles(root,"*.tmp").Length==0,"failed saves leave no staging files");
            store.Edit(d=>d.AddFolder("备份保留"));File.WriteAllText(path,"broken-primary");reopened=new OrganizerStore(path);
            Check(reopened.CanWrite&&reopened.Notice.Length>0&&reopened.Data.Tasks.Count==2,"damaged primary recovers the last valid backup");
            reopened.Edit(d=>d.AddFolder("恢复后"));
            Check(Directory.GetFiles(root,"organizer.json.unreadable-*").Length==1&&new OrganizerStore(path).Data.Folders.Contains("恢复后"),"saving recovered data preserves the damaged original");
            File.Delete(path);reopened=new OrganizerStore(path);Check(reopened.CanWrite&&reopened.Data.Tasks.Count==2,"missing primary restores an existing backup");
            File.WriteAllText(path,"bad-primary");File.WriteAllText(path+".bak","bad-backup");reopened=new OrganizerStore(path);
            Check(!reopened.CanWrite&&reopened.Notice.Length>0,"unreadable personal data disables editing");
            bool blocked=false;try{reopened.Edit(d=>d.AddFolder("blocked"));}catch(InvalidOperationException){blocked=true;}
            Check(blocked&&File.ReadAllText(path)=="bad-primary"&&File.ReadAllText(path+".bak")=="bad-backup","unreadable files cannot be replaced with an empty document");
            File.WriteAllText(path,before);reopened.Reload();Check(reopened.CanWrite&&reopened.Data.Tasks.Count==2,"reload restores editing after the file is repaired");
            Console.WriteLine(count+" organizer checks passed");return 0;
        }catch(Exception e){Console.WriteLine("FAIL "+e.Message);return 1;}
        finally{Directory.Delete(root,true);}
    }
}

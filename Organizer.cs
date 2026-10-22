using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;
namespace Workbench {
    public sealed class BookmarkEntry {
        public string Id=Guid.NewGuid().ToString("N"),Title="",Url="",Folder="未分类";
        public BookmarkEntry Copy(){return (BookmarkEntry)MemberwiseClone();}
        public static string Address(string value){
            string text=(value??"").Trim();
            if(text.Length==0||text.Length>4096||text.Any(Char.IsControl)||text.Contains("\\"))throw new ArgumentException("请填写有效的 http 或 https 网址。");
            if(text.IndexOf(':')<0)text="https://"+text;
            Uri uri;if(!Uri.TryCreate(text,UriKind.Absolute,out uri)||(uri.Scheme!=Uri.UriSchemeHttp&&uri.Scheme!=Uri.UriSchemeHttps)||String.IsNullOrEmpty(uri.Host)||uri.UserInfo.Length>0)throw new ArgumentException("仅支持 http / https 网址，网址中不能包含账户和密码。");
            return uri.AbsoluteUri;
        }
    }
    public sealed class ScheduleEntry {
        public string Id=Guid.NewGuid().ToString("N"),Title="",Notes="",DueAt="";
        public bool Completed;
        public ScheduleEntry Copy(){return (ScheduleEntry)MemberwiseClone();}
    }
    public sealed class OrganizerData {
        public int Version=1;
        public List<string> Folders=new List<string>{"未分类"};
        public List<BookmarkEntry> Bookmarks=new List<BookmarkEntry>();
        public List<ScheduleEntry> Tasks=new List<ScheduleEntry>();
        internal static string Text(string value,int max,string name){string text=(value??"").Trim();if(text.Length==0||text.Length>max||text.Any(Char.IsControl))throw new ArgumentException("请填写"+name+"（最多 "+max+" 字）。");return text;}
        public void AddFolder(string name){name=Text(name,80,"收藏夹名称");if(Folders.Contains(name,StringComparer.OrdinalIgnoreCase))throw new ArgumentException("已存在同名收藏夹。");Folders.Add(name);}
        public void RenameFolder(string from,string to){if(from=="未分类"||!Folders.Contains(from))throw new ArgumentException("请选择自建收藏夹。");to=Text(to,80,"收藏夹名称");if(Folders.Any(x=>x!=from&&String.Equals(x,to,StringComparison.OrdinalIgnoreCase)))throw new ArgumentException("已存在同名收藏夹。");Folders[Folders.IndexOf(from)]=to;foreach(var entry in Bookmarks.Where(x=>x.Folder==from))entry.Folder=to;}
        public void RemoveFolder(string name){if(name=="未分类"||!Folders.Contains(name))throw new ArgumentException("请选择自建收藏夹。");if(Bookmarks.Any(x=>x.Folder==name))throw new ArgumentException("收藏夹内还有网址，请先移动或删除这些网址。");Folders.Remove(name);}
        public void PutBookmark(BookmarkEntry entry){var next=entry.Copy();Bookmarks.RemoveAll(x=>x.Id==next.Id);Bookmarks.Add(next);}
        public void RemoveBookmark(string id){if(Bookmarks.RemoveAll(x=>x.Id==id)==0)throw new ArgumentException("请先选择网址。");}
        public void PutTask(ScheduleEntry entry){var next=entry.Copy();Tasks.RemoveAll(x=>x.Id==next.Id);Tasks.Add(next);}
        public void ToggleTask(string id){var item=Tasks.FirstOrDefault(x=>x.Id==id);if(item==null)throw new ArgumentException("请先选择记录。");item.Completed=!item.Completed;}
        public void RemoveTask(string id){if(Tasks.RemoveAll(x=>x.Id==id)==0)throw new ArgumentException("请先选择记录。");}
        public IEnumerable<ScheduleEntry> TaskItems(int filter){return Tasks.Where(x=>filter==1||(filter==2?x.Completed:!x.Completed)).OrderBy(x=>x.Completed).ThenBy(x=>x.DueAt.Length==0?"9999":x.DueAt,StringComparer.Ordinal).ThenBy(x=>x.Title);}
        public void Validate(){
            if(Version!=1||Folders==null||Bookmarks==null||Tasks==null||Folders.Count==0||Folders.Count>200||Bookmarks.Count>5000||Tasks.Count>5000||Folders[0]!="未分类")throw new InvalidDataException("收藏与日程数据格式或数量无效。");
            var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);foreach(string folder in Folders)if(Text(folder,80,"收藏夹名称")!=folder||!names.Add(folder))throw new InvalidDataException("收藏夹名称无效或重复。");
            var ids=new HashSet<string>();Guid parsed;
            foreach(var entry in Bookmarks){
                if(entry==null||!Guid.TryParseExact(entry.Id,"N",out parsed)||!ids.Add(entry.Id)||!Folders.Contains(entry.Folder))throw new InvalidDataException("网址标识或收藏夹无效。");
                entry.Title=Text(entry.Title,200,"网址名称");entry.Url=BookmarkEntry.Address(entry.Url);
            }
            ids.Clear();foreach(var entry in Tasks){
                if(entry==null||!Guid.TryParseExact(entry.Id,"N",out parsed)||!ids.Add(entry.Id)||entry.Notes==null||entry.Notes.Length>4000||entry.DueAt==null)throw new InvalidDataException("待办记录格式无效。");
                entry.Title=Text(entry.Title,200,"待办 / 日程标题");DateTime due;
                if(entry.DueAt.Length>0&&(!DateTime.TryParseExact(entry.DueAt,"yyyy-MM-dd'T'HH:mm",CultureInfo.InvariantCulture,DateTimeStyles.None,out due)||due<new DateTime(1753,1,1)||due>new DateTime(9998,12,31)))throw new ArgumentException("日期时间无效，须在系统日期选择器支持的范围内。");
            }
        }
    }
    public sealed class OrganizerStore {
        readonly string path;
        public OrganizerData Data {get;private set;}
        public bool CanWrite {get;private set;}
        public string Notice {get;private set;}
        public static string DefaultPath {get{return Path.Combine(DataLocation.Root,"organizer.json");}}
        public OrganizerStore(string path){this.path=Path.GetFullPath(path);Data=new OrganizerData();Reload();}
        static OrganizerData Read(string path){var data=new JavaScriptSerializer().Deserialize<OrganizerData>(File.ReadAllText(path));if(data==null)throw new InvalidDataException();data.Validate();return data;}
        public void Reload(){
            try{
                bool missing=false;Exception failure=null;OrganizerData next=null;Notice="";
                try{next=Read(path);}catch(FileNotFoundException){missing=true;}catch(DirectoryNotFoundException){missing=true;}catch(Exception e){failure=e;}
                if(next==null){
                    try{next=Read(path+".bak");Notice="已从备份恢复。保存时会保留损坏的原文件。";}
                    catch(FileNotFoundException){if(!missing)throw new InvalidDataException("记录无法读取",failure);next=new OrganizerData();}
                    catch(DirectoryNotFoundException){if(!missing)throw new InvalidDataException("记录无法读取",failure);next=new OrganizerData();}
                }
                Data=next;CanWrite=true;
            }catch{CanWrite=false;Notice="收藏与日程文件无法读取，编辑已禁用。原文件已保留；修复后点击重新加载。";}
        }
        public void Edit(Action<OrganizerData> change){
            if(!CanWrite)throw new InvalidOperationException(Notice);
            var serializer=new JavaScriptSerializer();var next=serializer.Deserialize<OrganizerData>(serializer.Serialize(Data));change(next);next.Validate();
            Directory.CreateDirectory(Path.GetDirectoryName(path));string stage=path+"."+Guid.NewGuid().ToString("N")+".tmp";
            try{
                File.WriteAllText(stage,serializer.Serialize(next),new UTF8Encoding(false));
                if(File.Exists(path)){
                    bool valid=true;try{Read(path);}catch{valid=false;}
                    if(!valid)File.Copy(path,path+".unreadable-"+DateTime.UtcNow.Ticks);
                    File.Replace(stage,path,valid?path+".bak":null);
                }else{if(!File.Exists(path+".bak"))File.Copy(stage,path+".bak");File.Move(stage,path);}
                Data=next;Notice="";
            }finally{if(File.Exists(stage))File.Delete(stage);}
        }
    }
}

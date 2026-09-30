using System;
using System.Security.Principal;
using System.Text;
using System.Threading;
namespace Workbench {
    // Task service launches outside the packaged terminal's AppData virtualization.
    // Current-user, on-demand tasks are deleted after startup acknowledgement.
    public static class NativeProcess {
        public static string Quote(string value) {
            var result=new StringBuilder("\"");int slashes=0;
            foreach(char c in value){if(c=='\\'){slashes++;continue;}result.Append('\\',c=='"'?slashes*2+1:slashes);result.Append(c);slashes=0;}
            result.Append('\\',slashes*2);return result.Append('"').ToString();
        }
        public static int Launch(string executable,string[] args,bool wait) {
            string id=Guid.NewGuid().ToString("N"),name="DesktopWorkbench.Start-"+id,eventName=@"Local\DesktopWorkbench.Start-"+id;
            using(var ready=new EventWaitHandle(false,EventResetMode.ManualReset,eventName)) {
                dynamic service=Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service"));service.Connect();dynamic folder=service.GetFolder("\\");
                dynamic task=service.NewTask(0);task.RegistrationInfo.Description="Temporary portable Workbench launcher";
                task.Principal.UserId=WindowsIdentity.GetCurrent().User.Value;task.Principal.LogonType=3;task.Principal.RunLevel=0;
                task.Settings.ExecutionTimeLimit="PT0S";task.Settings.DisallowStartIfOnBatteries=false;task.Settings.StopIfGoingOnBatteries=false;task.Settings.Hidden=true;
                dynamic action=task.Actions.Create(0);action.Path=executable;action.WorkingDirectory=System.IO.Path.GetDirectoryName(executable);
                var command=new StringBuilder("--native-context --startup-event "+Quote(eventName));foreach(string arg in args)command.Append(' ').Append(Quote(arg));action.Arguments=command.ToString();
                dynamic registered=folder.RegisterTaskDefinition(name,task,6,null,null,3,null);
                try {
                    registered.Run(null);
                    if(!ready.WaitOne(15000))throw new TimeoutException("Native Workbench startup timed out.");
                    if(!wait)return 0;
                    var deadline=DateTime.UtcNow.AddSeconds(30);
                    while((int)registered.State==4){if(DateTime.UtcNow>=deadline)throw new TimeoutException("Native Workbench command timed out.");Thread.Sleep(100);}
                    return (int)registered.LastTaskResult;
                } finally {folder.DeleteTask(name,0);}
            }
        }
        public static void Acknowledge(string[] args) {
            int index=Array.IndexOf(args,"--startup-event");
            if(index>=0&&index+1<args.Length)using(var ready=EventWaitHandle.OpenExisting(args[index+1]))ready.Set();
        }
    }
}
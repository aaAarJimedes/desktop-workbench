using System;
using System.Windows.Forms;
using Workbench;

// Compile against Workbench.Contracts.dll and put the resulting DLL in plugins/.
// This example is not shipped as an installed tool.
public sealed class HelloTool : IWorkbenchTool {
    public string Id { get { return "hello-example"; } }
    public string Title { get { return "扩展示例"; } }
    public string Description { get { return "自定义工具"; } }
    public Control CreateView() { return new Label {Text="扩展已成功加载",Dock=DockStyle.Fill,TextAlign=System.Drawing.ContentAlignment.MiddleCenter}; }
    public void Start() { }
    public void Dispose() { }
}

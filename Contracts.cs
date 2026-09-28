using System;
using System.Windows.Forms;

namespace Workbench {
    // Plugins are trusted local code. Put compiled implementations in plugins/.
    public interface IWorkbenchTool : IDisposable {
        string Id { get; }
        string Title { get; }
        string Description { get; }
        Control CreateView();
        void Start();
    }
}

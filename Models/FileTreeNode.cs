using System.Collections.ObjectModel;

namespace AIBridge.Models
{
    public class FileTreeNode
    {
        public string Name { get; set; } = string.Empty;
        public string FullPath { get; set; } = string.Empty;
        public bool IsDirectory { get; set; }
        public string Icon => IsDirectory ? "📁" : "📄";
        public ObservableCollection<FileTreeNode> Children { get; set; } = new();
    }
}

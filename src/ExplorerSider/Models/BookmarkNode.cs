using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace ExplorerSider.Models;

public enum NodeType
{
    Folder,
    Bookmark
}

/// <summary>书签树节点。文件夹含 Children，书签含 Path/Icon。</summary>
public class BookmarkNode : INotifyPropertyChanged
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public NodeType Type { get; set; } = NodeType.Bookmark;

    public string Name { get; set; } = "";

    /// <summary>书签目标路径（规范化 Windows 形式：E:\x、\\host\share、::{CLSID}、计算机\HKEY...）</summary>
    public string? Path { get; set; }

    /// <summary>图标 data URI（data:image/png;base64,...），与 Clover 导出格式一致；可为空（用默认图标）</summary>
    public string? Icon { get; set; }

    /// <summary>收藏时间（unix 秒）</summary>
    public long AddDate { get; set; } = DateTimeOffset.Now.ToUnixTimeSeconds();

    public List<BookmarkNode> Children { get; set; } = new();

    [JsonIgnore]
    public bool IsFolder => Type == NodeType.Folder;

    /// <summary>管理窗口 TreeViewItem 展开状态（运行时，不持久化）</summary>
    [JsonIgnore]
    public bool IsExpanded
    {
        get => _isExpanded;
        set { _isExpanded = value; Raise(); }
    }
    private bool _isExpanded = true;

    /// <summary>路径失效标记（"检查失效路径"功能写入，运行时）</summary>
    [JsonIgnore]
    public bool PathInvalid
    {
        get => _pathInvalid;
        set { _pathInvalid = value; Raise(); }
    }
    private bool _pathInvalid;

    /// <summary>管理窗口多选标记（Ctrl+点击 / Ctrl+A，运行时）</summary>
    [JsonIgnore]
    public bool IsMultiSelected
    {
        get => _isMultiSelected;
        set { _isMultiSelected = value; Raise(); }
    }
    private bool _isMultiSelected;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name!));

    public static BookmarkNode NewFolder(string name) => new() { Type = NodeType.Folder, Name = name };

    public static BookmarkNode NewBookmark(string name, string path) => new() { Type = NodeType.Bookmark, Name = name, Path = path };

    /// <summary>递归统计书签数</summary>
    public int CountBookmarks()
    {
        int n = IsFolder ? 0 : 1;
        foreach (var c in Children) n += c.CountBookmarks();
        return n;
    }

    /// <summary>递归统计文件夹数（不含自身）</summary>
    public int CountFolders()
    {
        int n = 0;
        foreach (var c in Children)
        {
            if (c.IsFolder) n += 1 + c.CountFolders();
        }
        return n;
    }

    /// <summary>深度优先查找节点，返回其父节点（根的父节点返回 null）</summary>
    public BookmarkNode? FindParent(BookmarkNode target)
    {
        foreach (var c in Children)
        {
            if (ReferenceEquals(c, target)) return this;
            var r = c.FindParent(target);
            if (r != null) return r;
        }
        return null;
    }

    public BookmarkNode? FindById(Guid id)
    {
        if (Id == id) return this;
        foreach (var c in Children)
        {
            var r = c.FindById(id);
            if (r != null) return r;
        }
        return null;
    }

    /// <summary>target 是否在以 this 为根的子树内（防止把文件夹拖进自己的子孙）</summary>
    public bool ContainsSubtree(BookmarkNode target)
    {
        if (ReferenceEquals(this, target)) return true;
        foreach (var c in Children)
            if (c.ContainsSubtree(target)) return true;
        return false;
    }
}

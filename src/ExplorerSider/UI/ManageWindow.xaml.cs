using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ExplorerSider.Models;
using ExplorerSider.Services;
using Microsoft.Win32;

namespace ExplorerSider.UI;

/// <summary>书签管理窗口：TreeView 全树增删改、内部拖动排序/移动、外部文件拖入、导入导出。</summary>
public partial class ManageWindow : Window
{
    private readonly BookmarkStore _store;
    private BookmarkNode? _selected;
    /// <summary>Ctrl 多选集合（与 IsMultiSelected 同步）</summary>
    private readonly HashSet<BookmarkNode> _multiSelected = new();

    public ManageWindow(BookmarkStore store)
    {
        InitializeComponent();
        _store = store;
        Title = $"ExplorerSider 书签管理 — {BookmarkStore.DataDir}";

        _store.Changed += OnStoreChanged;
        RebuildTree();
        AutoStartMenu.IsChecked = AutoStartHelper.IsEnabled();
        Closed += (s, e) => _store.Changed -= OnStoreChanged;

        // Ctrl+点击 多选 + 拖动源（按住左键移动启动）
        Tree.PreviewMouseLeftButtonDown += Tree_PreviewMouseLeftButtonDown;
        Tree.PreviewMouseMove += Tree_PreviewMouseMove;
        InitTreeDrag();
    }

    private Point? _treeDragStart;

    private void Tree_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_treeDragStart is not Point p0) return;
        if (e.LeftButton != MouseButtonState.Pressed) { _treeDragStart = null; return; }
        var p = e.GetPosition(Tree);
        if (Math.Abs(p.X - p0.X) <= SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(p.Y - p0.Y) <= SystemParameters.MinimumVerticalDragDistance) return;

        _treeDragStart = null;
        var tvi = NearestTreeViewItem(e.OriginalSource);
        if (tvi?.Header is not BookmarkNode node) return;
        try { DragDrop.DoDragDrop(Tree, new DataObject(typeof(BookmarkNode), node), DragDropEffects.Move); }
        catch { /* 拖动被中断 */ }
        finally { ClearDropIndicator(); }
    }

    private void Tree_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _treeDragStart = e.GetPosition(Tree); // 记录按下位置供拖动判定
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            var tvi = NearestTreeViewItem(e.OriginalSource);
            if (tvi?.Header is BookmarkNode node)
            {
                e.Handled = true; // 接管默认单选
                if (node.IsMultiSelected)
                {
                    node.IsMultiSelected = false;
                    _multiSelected.Remove(node);
                }
                else
                {
                    node.IsMultiSelected = true;
                    _multiSelected.Add(node);
                }
            }
            return;
        }
        // 普通点击：清除多选，交由默认单选
        if (_multiSelected.Count > 0)
        {
            foreach (var n in _multiSelected) n.IsMultiSelected = false;
            _multiSelected.Clear();
        }
    }

    private void SelectAllNodes()
    {
        _multiSelected.Clear();
        void Walk(BookmarkNode n)
        {
            foreach (var c in n.Children)
            {
                c.IsMultiSelected = true;
                _multiSelected.Add(c);
                Walk(c);
            }
        }
        Walk(_store.Root);
    }

    private void OnStoreChanged() => Dispatcher.Invoke(RebuildTree);

    private void RebuildTree()
    {
        Tree.ItemsSource = null;
        Tree.ItemsSource = _store.Root.Children;
        if (_selected != null)
        {
            var again = _store.Root.FindById(_selected.Id);
            if (again != null) ExpandAndSelect(again);
        }
    }

    private void ExpandAndSelect(BookmarkNode node)
    {
        // 展开状态由 IsExpanded 双向绑定保留，这里仅刷新树（选中的容器视觉态由用户再次点击/键盘恢复）
        Tree.Items.Refresh();
    }

    private void Tree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        _selected = e.NewValue as BookmarkNode;
    }

    #region 增删改

    private BookmarkNode TargetFolder => _selected is { IsFolder: true } ? _selected : _selected != null ? ParentOf(_selected) : _store.Root;

    private BookmarkNode ParentOf(BookmarkNode node) => _store.Root.FindParent(node) ?? _store.Root;

    private void NewBookmark_Click(object sender, RoutedEventArgs e)
    {
        var node = BookmarkNode.NewBookmark("新书签", "");
        if (EditDialog.Show(this, node, isNew: true))
        {
            node.Icon = IconHelper.FetchIconDataUri(node.Path);
            TargetFolder.Children.Add(node);
            _store.Save();
            _selected = node;
        }
    }

    private void NewFolder_Click(object sender, RoutedEventArgs e)
    {
        var node = BookmarkNode.NewFolder("新文件夹");
        if (EditDialog.Show(this, node, isNew: true))
        {
            TargetFolder.Children.Add(node);
            _store.Save();
            _selected = node;
        }
    }

    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (_selected == null) return;
        if (EditDialog.Show(this, _selected, isNew: false))
        {
            if (!_selected.IsFolder)
                _selected.Icon = IconHelper.FetchIconDataUri(_selected.Path); // 路径可能改了
            _store.Save();
        }
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        // 删除范围 = Ctrl 多选集合 ∪ 当前单击选中项（单击项的高亮仍在，用户感知为已选中）
        var targets = new List<BookmarkNode>(_multiSelected);
        if (_selected != null && !_multiSelected.Contains(_selected))
            targets.Add(_selected);
        if (targets.Count == 0) return;

        if (MessageBox.Show(this, $"确认删除选中的 {targets.Count} 项（含其中全部内容）？", "确认删除",
            MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        foreach (var node in targets)
        {
            var parent = _store.Root.FindParent(node);
            parent?.Children.Remove(node); // 父已随先删的祖先移除时 FindParent 返回 null，自然跳过
        }
        foreach (var n in _multiSelected) n.IsMultiSelected = false;
        _multiSelected.Clear();
        _selected = null;
        _store.Save();
    }

    private void Up_Click(object sender, RoutedEventArgs e) => Move(-1);
    private void Down_Click(object sender, RoutedEventArgs e) => Move(1);

    private void Move(int delta)
    {
        if (_selected == null) return;
        var list = ParentOf(_selected).Children;
        int i = list.IndexOf(_selected);
        int j = i + delta;
        if (i < 0 || j < 0 || j >= list.Count) return;
        list.RemoveAt(i);
        list.Insert(j, _selected);
        _store.Save();
    }

    private void Tree_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F2) Edit_Click(sender, e);
        else if (e.Key == Key.Delete) Delete_Click(sender, e);
        else if (e.Key == Key.Insert) NewBookmark_Click(sender, e);
        else if (e.Key == Key.A && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            e.Handled = true;
            SelectAllNodes();
        }
        else if (e.Key == Key.Escape && _multiSelected.Count > 0)
        {
            foreach (var n in _multiSelected) n.IsMultiSelected = false;
            _multiSelected.Clear();
        }
    }

    private void Tree_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left) Edit_Click(sender, e);
    }

    #endregion

    #region 检查失效 / 刷新图标

    private void CheckPaths_Click(object sender, RoutedEventArgs e) => RunBackground("正在检查路径（网络共享可能较慢）…", () =>
    {
        int bad = 0;
        void Walk(BookmarkNode n)
        {
            foreach (var c in n.Children)
            {
                if (!c.IsFolder)
                {
                    c.PathInvalid = !PathOpener.Exists(c.Path);
                    if (c.PathInvalid) bad++;
                }
                Walk(c);
            }
        }
        Walk(_store.Root);
        return bad;
    },
    bad => MessageBox.Show(this, bad == 0 ? "全部书签路径有效 ✓" : $"{bad} 个书签路径不存在（已标红，可能是网络共享暂不可达）",
        "检查完成", MessageBoxButton.OK, MessageBoxImage.Information));

    private void RefreshIcons_Click(object sender, RoutedEventArgs e) => RunBackground("正在刷新图标（网络共享可能较慢）…", () =>
    {
        int n = 0;
        void Walk(BookmarkNode node)
        {
            foreach (var c in node.Children)
            {
                if (!c.IsFolder)
                {
                    var icon = IconHelper.FetchIconDataUri(c.Path);
                    if (icon != null) { c.Icon = icon; n++; }
                }
                Walk(c);
            }
        }
        Walk(_store.Root);
        return n;
    },
    n => MessageBox.Show(this, $"已刷新 {n} 个书签图标（不可达的网络路径保留原图标）", "刷新图标",
        MessageBoxButton.OK, MessageBoxImage.Information));

    /// <summary>后台执行批量任务（避免 SHGetFileInfo/Directory.Exists 对不可达网络路径阻塞 UI），完成后 UI 线程回调并保存</summary>
    private void RunBackground<T>(string status, Func<T> work, Action<T> done)
    {
        IsEnabled = false;
        var hint = StatusText.Text;
        StatusText.Text = status;
        System.Threading.Tasks.Task.Run(() =>
        {
            T result;
            try { result = work(); }
            catch { result = default!; }
            Dispatcher.Invoke(() =>
            {
                _store.Save();
                IsEnabled = true;
                StatusText.Text = hint;
                done(result);
            });
        });
    }

    #endregion

    #region 导入导出

    private void Import_Click(object sender, RoutedEventArgs e) => App.ImportBookmarks(this);

    private void Export_Click(object sender, RoutedEventArgs e) => App.ExportBookmarks(this);

    private void OpenDataDir_Click(object sender, RoutedEventArgs e)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{BookmarkStore.DataDir}\"") { UseShellExecute = true }); }
        catch { }
    }

    private void AutoStart_Click(object sender, RoutedEventArgs e)
    {
        var enable = !AutoStartHelper.IsEnabled();
        AutoStartHelper.SetEnabled(enable);
        AutoStartMenu.IsChecked = enable;
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => ((App)Application.Current).ShutdownApp();

    #endregion

    #region 拖拽：外部文件拖入 + 内部节点拖动

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
    }

    /// <summary>外部文件/文件夹拖到窗口任意位置 → 加入当前选中文件夹（否则根）</summary>
    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths || paths.Length == 0) return;
        var folder = TargetFolder;
        foreach (var p in paths)
        {
            string name = SafeName(p);
            var bm = BookmarkNode.NewBookmark(name, p);
            bm.Icon = IconHelper.FetchIconDataUri(p);
            folder.Children.Add(bm);
        }
        _store.Save();
    }

    private static string SafeName(string p)
    {
        try
        {
            var n = Path.GetFileName(p.TrimEnd('\\', '/'));
            return string.IsNullOrEmpty(n) ? p : n;
        }
        catch { return p; }
    }

    // ---- TreeView 拖放：指示线（目标项上/下边蓝线）+ 分区语义 ----
    // 书签拖到文件夹节点：中部=移入；上/下边缘区=排序到其前/后（与悬浮栏规则一致）
    // 书签拖到书签节点：上半=插前面，下半=插后面；文件夹仅根级排序
    private TreeViewItem? _lineItem;      // 当前画线的目标容器
    private BookmarkNode? _lineNode;      // 线的目标节点
    private BookmarkNode? _lineFolder;    // 插入操作的目标文件夹
    private bool _lineBefore;
    private TreeViewItem? _hoverFolder;   // 移入语义高亮的容器（+悬停展开）
    private readonly DispatcherTimer _treeExpandTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private TreeViewItem? _treeExpandItem;

    private void InitTreeDrag()
    {
        _treeExpandTimer.Tick += (s, e) =>
        {
            _treeExpandTimer.Stop();
            if (_treeExpandItem != null) _treeExpandItem.IsExpanded = true; // 拖动悬停文件夹 → 展开子项
        };
    }

    private void Tree_DragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(typeof(BookmarkNode))) return; // 外部 FileDrop：交给 Window_DragOver
        if (e.Data.GetData(typeof(BookmarkNode)) is not BookmarkNode drag) return;
        e.Effects = DragDropEffects.Move;
        e.Handled = true;

        ClearDropIndicator();

        var tvi = NearestTreeViewItem(e.OriginalSource);
        if (tvi?.Header is not BookmarkNode target) return;
        if (ReferenceEquals(drag, target) || drag.ContainsSubtree(target)) { e.Effects = DragDropEffects.None; return; }

        var pos = e.GetPosition(tvi);
        double h = Math.Max(tvi.RenderSize.Height, 1);
        var parent = _store.Root.FindParent(target) ?? _store.Root;

        // 书签拖到文件夹节点中部（25%~75%）→ 移入 + 高亮 + 悬停展开
        if (target.IsFolder && !drag.IsFolder && pos.Y >= h * 0.25 && pos.Y <= h * 0.75)
        {
            _hoverFolder = tvi;
            tvi.Background = DropTargetBrush;
            _treeExpandItem = tvi;
            _treeExpandTimer.Stop();
            _treeExpandTimer.Start();
            return;
        }

        // 排序：上半=插前面，下半=插后面（文件夹拖动仅当目标父级为根时允许）
        if (drag.IsFolder && !ReferenceEquals(parent, _store.Root)) { e.Effects = DragDropEffects.None; return; }

        bool before = pos.Y < h / 2;
        tvi.BorderBrush = Brushes.DodgerBlue;
        tvi.BorderThickness = before ? new Thickness(0, 2, 0, 0) : new Thickness(0, 0, 0, 2);
        _lineItem = tvi;
        _lineNode = target;
        _lineFolder = parent;
        _lineBefore = before;
    }

    private void Tree_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(BookmarkNode)) is not BookmarkNode drag) return;
        e.Handled = true;

        // 1) 指示线在 → 按线位置插入目标文件夹（先取局部值再清指示，防 Clear 置空后误用）
        if (_lineNode != null && _lineFolder is { } lineFolder)
        {
            int idx = lineFolder.Children.IndexOf(_lineNode);
            bool before = _lineBefore;
            ClearDropIndicator();
            if (idx < 0) return;
            MoveTo(_store, drag, lineFolder, before ? idx : idx + 1);
            _selected = drag;
            return;
        }

        var tvi = NearestTreeViewItem(e.OriginalSource);
        BookmarkNode? target = tvi?.Header as BookmarkNode;
        ClearDropIndicator();

        // 2) 书签在文件夹节点中部松开 → 移入末尾
        if (target is { IsFolder: true } && !drag.IsFolder && !ReferenceEquals(drag, target)
            && !drag.ContainsSubtree(target))
        {
            var pos = e.GetPosition(tvi!);
            double h = Math.Max(tvi!.RenderSize.Height, 1);
            if (pos.Y >= h * 0.25 && pos.Y <= h * 0.75)
            {
                MoveTo(_store, drag, target, target.Children.Count);
                _selected = drag;
                return;
            }
        }

        // 3) 空白处松开 → 根级末尾
        if (_store.Root.FindParent(drag) is { } p && !ReferenceEquals(p, _store.Root))
        {
            p.Children.Remove(drag);
            _store.Root.Children.Add(drag);
            _store.Save();
            _selected = drag;
        }
    }

    private static void MoveTo(BookmarkStore store, BookmarkNode drag, BookmarkNode folder, int index)
    {
        if (drag.ContainsSubtree(folder)) return;
        if (drag.IsFolder && !ReferenceEquals(folder, store.Root)) return; // 文件夹仅根级
        var oldParent = store.Root.FindParent(drag);
        if (oldParent != null && ReferenceEquals(oldParent, folder) && oldParent.Children.IndexOf(drag) < index)
            index--; // 同文件夹内后移时索引修正
        oldParent?.Children.Remove(drag);
        folder.Children.Insert(Math.Clamp(index, 0, folder.Children.Count), drag);
        store.Save();
    }

    private void ClearDropIndicator()
    {
        if (_lineItem != null)
        {
            _lineItem.BorderBrush = null;
            _lineItem.BorderThickness = new Thickness(0);
            _lineItem = null;
        }
        _lineNode = null;
        _lineFolder = null;
        if (_hoverFolder != null)
        {
            _hoverFolder.Background = Brushes.Transparent;
            _hoverFolder = null;
        }
        _treeExpandTimer.Stop();
        _treeExpandItem = null;
    }

    private static readonly Brush DropTargetBrush = new SolidColorBrush(Color.FromRgb(0xE3, 0xF0, 0xFF));

    private TreeViewItem? NearestTreeViewItem(object? originalSource)
    {
        var dep = originalSource as DependencyObject;
        while (dep != null && dep is not TreeViewItem)
            dep = System.Windows.Media.VisualTreeHelper.GetParent(dep);
        return dep as TreeViewItem;
    }

    #endregion
}

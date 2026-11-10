using System.Windows;
using ExplorerSider.Models;

namespace ExplorerSider.UI;

/// <summary>新建/编辑书签或文件夹（文件夹类型隐藏路径区）。</summary>
public partial class EditDialog : Window
{
    private readonly BookmarkNode _node;

    private EditDialog(Window owner, BookmarkNode node, bool isNew)
    {
        InitializeComponent();
        _node = node;
        Owner = owner;
        Title = (isNew ? "新建" : "编辑") + (node.IsFolder ? "文件夹" : "书签");

        NameBox.Text = node.Name;
        if (node.IsFolder)
        {
            PathLabel.Visibility = Visibility.Collapsed;
            PathBox.Visibility = Visibility.Collapsed;
            BrowseBtn.Visibility = Visibility.Collapsed;
        }
        else
        {
            PathBox.Text = node.Path ?? "";
        }
    }

    /// <summary>弹出编辑。返回 true 表示已确认修改（直接写回 node）。</summary>
    public static bool Show(Window owner, BookmarkNode node, bool isNew)
    {
        var dlg = new EditDialog(owner, node, isNew);
        return dlg.ShowDialog() == true;
    }

    private void BrowseBtn_Click(object sender, RoutedEventArgs e)
    {
        // .NET 8 WPF 原生文件夹选择框
        var dlg = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "选择文件夹"
        };
        if (dlg.ShowDialog(this) == true)
            PathBox.Text = dlg.FolderName;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        var name = NameBox.Text.Trim();
        if (name.Length == 0)
        {
            MessageBox.Show(this, "名称不能为空", "ExplorerSider", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        _node.Name = name;
        if (!_node.IsFolder)
        {
            var path = PathBox.Text.Trim().Trim('"');
            if (path.Length == 0)
            {
                MessageBox.Show(this, "路径不能为空", "ExplorerSider", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            _node.Path = path;
        }
        DialogResult = true;
    }
}

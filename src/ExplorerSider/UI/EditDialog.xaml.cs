using System.Windows;
using ExplorerSider.Models;
using ExplorerSider.Services;

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
        // 语言相关文本运行时赋值（XAML 仅占位）
        Title = isNew
            ? (node.IsFolder ? L.EditNewFolderTitle : L.EditNewBookmarkTitle)
            : (node.IsFolder ? L.EditFolderTitle : L.EditBookmarkTitle);
        NameLabel.Text = L.LabelName;
        PathLabel.Text = L.LabelPath;
        BrowseBtn.Content = L.BtnBrowse;
        PathHintText.Text = L.PathHint;
        OkBtn.Content = L.BtnOk;
        CancelBtn.Content = L.BtnCancel;

        NameBox.Text = node.Name;
        if (node.IsFolder)
        {
            PathLabel.Visibility = Visibility.Collapsed;
            PathBox.Visibility = Visibility.Collapsed;
            BrowseBtn.Visibility = Visibility.Collapsed;
            PathHintText.Visibility = Visibility.Collapsed;
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
            Title = L.FolderPickerTitle
        };
        if (dlg.ShowDialog(this) == true)
            PathBox.Text = dlg.FolderName;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        var name = NameBox.Text.Trim();
        if (name.Length == 0)
        {
            MessageBox.Show(this, L.NameRequired, "ExplorerSider", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        _node.Name = name;
        if (!_node.IsFolder)
        {
            var path = PathBox.Text.Trim().Trim('"');
            if (path.Length == 0)
            {
                MessageBox.Show(this, L.PathRequired, "ExplorerSider", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            _node.Path = path;
        }
        DialogResult = true;
    }
}

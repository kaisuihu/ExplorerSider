using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using ExplorerSider.Services;

namespace ExplorerSider.UI;

/// <summary>书签 icon data URI → ImageSource（空则默认文件夹图标）</summary>
public class IconToImageConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return IconHelper.FromDataUri(value as string) ?? IconHelper.DefaultFolderIcon;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>路径 → 显示文本（供灰色副标题；null 显示空）</summary>
public class PathDisplayConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value as string ?? "";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

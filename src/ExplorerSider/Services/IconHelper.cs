using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ExplorerSider.Services;

/// <summary>书签图标：base64 ↔ ImageSource；SHGetFileInfo 抓取目标路径系统图标。</summary>
public static partial class IconHelper
{
    // ---- base64 data URI → ImageSource ----

    private static readonly Dictionary<string, ImageSource> Cache = new();

    public static ImageSource? FromDataUri(string? dataUri)
    {
        if (string.IsNullOrEmpty(dataUri)) return null;
        if (Cache.TryGetValue(dataUri, out var cached)) return cached;

        try
        {
            var b64 = dataUri.Contains(',') ? dataUri.Split(',', 2)[1] : dataUri;
            var bytes = Convert.FromBase64String(b64);
            var img = new BitmapImage();
            using var ms = new MemoryStream(bytes);
            img.BeginInit();
            img.CacheOption = BitmapCacheOption.OnLoad;
            img.StreamSource = ms;
            img.EndInit();
            img.Freeze();
            Cache[dataUri] = img;
            return img;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>当前没有图标时的默认文件夹图标（系统样式，懒加载缓存）</summary>
    public static ImageSource? DefaultFolderIcon
    {
        get
        {
            _defaultFolder ??= FetchIconDataUri(Environment.GetFolderPath(Environment.SpecialFolder.Windows));
            return FromDataUri(_defaultFolder);
        }
    }

    private static string? _defaultFolder;

    // ---- SHGetFileInfo 抓系统图标 → base64 data URI ----

    public static string? FetchIconDataUri(string? path, bool openIcon = false)
    {
        var kind = PathOpener.Classify(path);
        // 虚拟路径/注册表/不可达路径：用 Windows 目录的文件夹图标兜底
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var target = kind is PathKind.Shell or PathKind.Registry or PathKind.Unknown ? windows : (path ?? windows);

        try
        {
            var flags = SHGFI_ICON | SHGFI_LARGEICON;
            if (openIcon) flags |= SHGFI_OPENICON;

            var shfi = new SHFILEINFO();
            var h = SHGetFileInfoW(target, 0, ref shfi, (uint)Marshal.SizeOf<SHFILEINFO>(), flags);
            if (shfi.hIcon == IntPtr.Zero) return null;

            try
            {
                var bs = Imaging.CreateBitmapSourceFromHIcon(shfi.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                bs.Freeze();

                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(bs));
                using var ms = new MemoryStream();
                enc.Save(ms);
                return "data:image/png;base64," + Convert.ToBase64String(ms.ToArray());
            }
            finally
            {
                DestroyIcon(shfi.hIcon);
            }
        }
        catch
        {
            return null;
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
    }

    private const uint SHGFI_ICON = 0x100;
    private const uint SHGFI_LARGEICON = 0x0;
    private const uint SHGFI_OPENICON = 0x2;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfoW(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);
}

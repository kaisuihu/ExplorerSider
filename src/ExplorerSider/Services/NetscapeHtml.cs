using System.Text;
using System.Text.RegularExpressions;
using ExplorerSider.Models;

namespace ExplorerSider.Services;

/// <summary>
/// Netscape Bookmark HTML（Clover / Chrome / Firefox 书签导出格式）的导入与导出。
/// 兼容四类 HREF：file:/// 本地、file://host UNC、::{CLSID} 等 shell 路径、原始 Windows 路径（含注册表）。
/// </summary>
public static class NetscapeHtml
{
    // 行式结构：Clover/Chrome 生成的格式均为一行一个条目
    private static readonly Regex RxH3 = new(@"<DT>\s*<H3([^>]*)>(.*?)</H3>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex RxA = new(@"<DT>\s*<A\s([^>]*)>(.*?)</A>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex RxDlOpen = new(@"<DL>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex RxDlClose = new(@"</DL>", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // 引号边界提取属性值，天然规避 base64 padding 的 '=' 干扰
    private static readonly Regex RxAttr = new(@"([A-Z_]+)\s*=\s*""([^""]*)""", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    #region 导入

    /// <summary>解析 Netscape HTML 为书签树。返回根节点（通常是"书签栏"）。</summary>
    public static BookmarkNode Import(string html)
    {
        var root = BookmarkNode.NewFolder("书签栏");
        var stack = new Stack<BookmarkNode>();
        stack.Push(root);

        string[] lines = html.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        BookmarkNode? pendingFolder = null; // H3 与其 <DL> 在相邻行，H3 先记下

        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;

            var mH3 = RxH3.Match(line);
            if (mH3.Success)
            {
                var folder = BookmarkNode.NewFolder(DecodeEntities(mH3.Groups[2].Value));
                foreach (Match a in RxAttr.Matches(mH3.Groups[1].Value))
                {
                    if (a.Groups[1].Value.Equals("ADD_DATE", StringComparison.OrdinalIgnoreCase))
                        folder.AddDate = ParseUnix(a.Groups[2].Value);
                }
                stack.Peek().Children.Add(folder);
                pendingFolder = folder;
                continue;
            }

            var mA = RxA.Match(line);
            if (mA.Success)
            {
                string? href = null, icon = null;
                long addDate = DateTimeOffset.Now.ToUnixTimeSeconds();
                foreach (Match a in RxAttr.Matches(mA.Groups[1].Value))
                {
                    switch (a.Groups[1].Value.ToUpperInvariant())
                    {
                        case "HREF": href = a.Groups[2].Value; break;
                        case "ICON": icon = a.Groups[2].Value; break;
                        case "ADD_DATE": addDate = ParseUnix(a.Groups[2].Value); break;
                    }
                }
                var name = DecodeEntities(mA.Groups[2].Value);
                var path = NormalizeHref(href);
                if (path == null) continue;

                var bm = BookmarkNode.NewBookmark(name, path);
                bm.AddDate = addDate;
                bm.Icon = icon;
                stack.Peek().Children.Add(bm);
                continue;
            }

            if (RxDlOpen.IsMatch(line))
            {
                // 文件夹内容开始：栈顶切到刚创建的文件夹
                if (pendingFolder != null)
                {
                    stack.Push(pendingFolder);
                    pendingFolder = null;
                }
                continue;
            }

            if (RxDlClose.IsMatch(line))
            {
                if (stack.Count > 1) stack.Pop();
                continue;
            }
        }

        // 若根内容恰好是一个"书签栏"文件夹，提升为根（保持与 Clover 结构一致）
        if (root.Children.Count == 1 && root.Children[0].IsFolder)
        {
            var promoted = root.Children[0];
            if (promoted.Name.Contains("书签栏") || promoted.Name.Contains("Bookmarks", StringComparison.OrdinalIgnoreCase) || promoted.Name.Contains("toolbar", StringComparison.OrdinalIgnoreCase))
                root = promoted;
        }
        return root;
    }

    /// <summary>file:///E:/x%20y → E:\x y；file://host/share → \\host\share；::{CLSID}、C:\..、计算机\HKEY.. 原样</summary>
    public static string? NormalizeHref(string? href)
    {
        if (string.IsNullOrWhiteSpace(href)) return null;
        href = href.Trim();

        if (href.StartsWith("file:///", StringComparison.OrdinalIgnoreCase))
        {
            var rest = href.Substring(8); // "E:/x%20y"
            return DecodePath(rest);
        }
        if (href.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
        {
            var rest = href.Substring(7); // "host/share" 或 "host"
            return @"\\" + DecodePath(rest);
        }
        if (href.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
        {
            return DecodePath(href.Substring(5));
        }
        if (href.StartsWith("shell:::", StringComparison.OrdinalIgnoreCase))
        {
            return href.Substring(8); // shell:::{CLSID} → ::{CLSID}
        }
        // ::{CLSID}、C:\原始路径、计算机\HKEY...：原样保留（对可疑的 %XX 做一次保守解码）
        return MaybeDecodeBare(href);
    }

    private static string DecodePath(string s)
    {
        s = Uri.UnescapeDataString(s);
        return s.Replace('/', '\\');
    }

    /// <summary>裸路径若含 %XX 序列且解码后不含 '%'，采用解码结果（处理个别导出器对明文路径的编码）</summary>
    private static string MaybeDecodeBare(string s)
    {
        if (!s.Contains('%')) return s;
        try
        {
            var d = Uri.UnescapeDataString(s);
            return d.Contains('%') ? s : d;
        }
        catch { return s; }
    }

    private static long ParseUnix(string s) =>
        long.TryParse(s, out var v) && v > 0 ? v : DateTimeOffset.Now.ToUnixTimeSeconds();

    /// <summary>HTML 实体解码（Clover 备份中存在裸 &amp; 也有未转义的 &amp;，两者都容忍）</summary>
    private static string DecodeEntities(string s)
    {
        if (!s.Contains('&')) return s;
        return s.Replace("&lt;", "<").Replace("&gt;", ">").Replace("&quot;", "\"")
                .Replace("&#39;", "'").Replace("&apos;", "'").Replace("&amp;", "&");
    }

    #endregion

    #region 导出

    /// <summary>导出为 Clover/Chrome 可再导入的 Netscape HTML（UTF-8 BOM、CRLF、含 ICON/ADD_DATE）。</summary>
    public static string Export(BookmarkNode root)
    {
        var sb = new StringBuilder();
        sb.Append("<!DOCTYPE NETSCAPE-Bookmark-file-1>\r\n");
        sb.Append("<!-- This is an automatically generated file.\r\n");
        sb.Append("     It will be read and overwritten.\r\n");
        sb.Append("     DO NOT EDIT! -->\r\n");
        sb.Append("<META HTTP-EQUIV=\"Content-Type\" CONTENT=\"text/html; charset=UTF-8\">\r\n");
        sb.Append("<TITLE>Bookmarks</TITLE>\r\n");
        sb.Append("<H1>Bookmarks</H1>\r\n");
        sb.Append("<DL><p>\r\n");

        long now = DateTimeOffset.Now.ToUnixTimeSeconds();
        AppendFolderOpen(sb, root.Name, now, root.AddDate, personalToolbar: true, 1);
        AppendChildren(sb, root, 2);
        AppendFolderClose(sb, 1);

        sb.Append("</DL><p>\r\n");
        return sb.ToString();
    }

    private static void AppendChildren(StringBuilder sb, BookmarkNode parent, int depth)
    {
        foreach (var node in parent.Children)
        {
            if (node.IsFolder)
            {
                AppendFolderOpen(sb, node.Name, node.AddDate, node.AddDate, false, depth);
                AppendChildren(sb, node, depth + 1);
                AppendFolderClose(sb, depth);
            }
            else
            {
                sb.Append(Indent(depth));
                sb.Append("<DT><A HREF=\"").Append(PathToHref(node.Path ?? ""));
                sb.Append("\" ADD_DATE=\"").Append(node.AddDate > 0 ? node.AddDate : DateTimeOffset.Now.ToUnixTimeSeconds());
                sb.Append('"');
                if (!string.IsNullOrEmpty(node.Icon))
                    sb.Append(" ICON=\"").Append(node.Icon).Append('"');
                sb.Append('>').Append(EncodeEntities(node.Name)).Append("</A>\r\n");
            }
        }
    }

    private static void AppendFolderOpen(StringBuilder sb, string name, long add, long modified, bool personalToolbar, int depth)
    {
        sb.Append(Indent(depth));
        sb.Append("<DT><H3 ADD_DATE=\"").Append(add);
        sb.Append("\" LAST_MODIFIED=\"").Append(modified);
        if (personalToolbar) sb.Append("\" PERSONAL_TOOLBAR_FOLDER=\"true");
        sb.Append("\">").Append(EncodeEntities(name)).Append("</H3>\r\n");
        sb.Append(Indent(depth)).Append("<DL><p>\r\n");
    }

    private static void AppendFolderClose(StringBuilder sb, int depth)
    {
        sb.Append(Indent(depth)).Append("</DL><p>\r\n");
    }

    private static string Indent(int depth) => new(' ', depth * 4);

    /// <summary>Windows 路径 → HREF（与 Clover 编码风格一致：中文/空格按 UTF-8 百分号编码，正斜杠）</summary>
    public static string PathToHref(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "";
        if (path.StartsWith(@"\\"))                    // \\host\share → file://host/share
            return "file://" + EncodePath(path.Substring(2).Replace('\\', '/'));
        if (path.StartsWith("::"))                     // CLSID/虚拟路径原样
            return path;
        if (path.StartsWith("计算机\\") || path.StartsWith("HKEY_", StringComparison.OrdinalIgnoreCase))
            return path;                              // 注册表路径原样
        // E:\x y → file:///E:/x%20y
        return "file:///" + EncodePath(path.Replace('\\', '/'));
    }

    /// <summary>百分号编码：保留 URI 安全字符及 ':/()'，其余按 UTF-8 字节编码（含中文、空格、&amp;）</summary>
    private static string EncodePath(string s)
    {
        var sb = new StringBuilder(s.Length + 16);
        foreach (byte b in Encoding.UTF8.GetBytes(s))
        {
            char c = (char)b;
            bool safe = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9')
                       || c == '-' || c == '_' || c == '.' || c == '~' || c == ':' || c == '/' || c == '(' || c == ')';
            if (safe) sb.Append(c);
            else sb.Append('%').Append(b.ToString("X2"));
        }
        return sb.ToString();
    }

    private static string EncodeEntities(string s) =>
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");

    #endregion
}

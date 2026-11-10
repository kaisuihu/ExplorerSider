using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using ExplorerSider.Models;
using ExplorerSider.Services;

namespace ExplorerSider;

/// <summary>
/// 命令行自检：--selftest &lt;bookmarks.html&gt;
/// 导入真实 Clover 备份 → 导出 → 再导入 → 树等价断言；报告写入 stdout 与临时文件。
/// </summary>
public static class SelfTest
{
    [DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int dwProcessId);

    public static int Run(string? htmlPath)
    {
        var log = new StringBuilder();
        int failures = 0;

        void Log(string s) { log.AppendLine(s); Console.WriteLine(s); }

        AttachConsole(-1); // 附加到父控制台（若从终端启动）

        if (htmlPath == null || !File.Exists(htmlPath))
        {
            Log($"[FAIL] 找不到输入文件：{htmlPath ?? "(未提供)"}");
            WriteReport(log.ToString());
            return 2;
        }

        try
        {
            Log($"== ExplorerSider selftest ==");
            Log($"输入: {htmlPath}");

            // 1. 导入
            var root = NetscapeHtml.Import(File.ReadAllText(htmlPath));
            int bookmarks = root.CountBookmarks();
            int folders = root.CountFolders();
            int icons = 0, hrefLocal = 0, hrefUnc = 0, hrefBare = 0;
            var specialPaths = new List<string>();
            var hrefs = new List<(string href, BookmarkNode node)>();

            void Walk(BookmarkNode n)
            {
                foreach (var c in n.Children)
                {
                    if (c.IsFolder) Walk(c);
                    else
                    {
                        if (!string.IsNullOrEmpty(c.Icon)) icons++;
                        var kind = PathOpener.Classify(c.Path);
                        switch (kind)
                        {
                            case PathKind.Unc: hrefUnc++; break;
                            case PathKind.Shell: goto special;
                            case PathKind.Registry: goto special;
                            default:
                                if (System.Text.RegularExpressions.Regex.IsMatch(c.Path ?? "", @"^[A-Za-z]:\\")) hrefLocal++;
                                else goto special;
                                break;
                            special:
                                hrefBare++;
                                specialPaths.Add($"{c.Name}  =>  {c.Path}");
                                break;
                        }
                    }
                }
            }
            Walk(root);

            Log($"根: “{root.Name}”  书签: {bookmarks}  文件夹: {folders}  带图标: {icons}");
            Log($"路径分类: 本地 {hrefLocal} | UNC {hrefUnc} | CLSID/注册表/特殊 {hrefBare}");
            Log("-- 特殊路径规范化结果 --");
            foreach (var s in specialPaths.Take(10)) Log("   " + s);

            // 2. 导出（UTF-8 BOM）
            var exportPath = Path.Combine(Path.GetTempPath(), "explorersider_selftest_export.html");
            File.WriteAllText(exportPath, NetscapeHtml.Export(root), new UTF8Encoding(true));
            Log($"导出: {exportPath}");

            // 3. 再导入 → 树等价
            var root2 = NetscapeHtml.Import(File.ReadAllText(exportPath));
            var diffs = new List<string>();
            int checkedNodes = Compare(root, root2, "", diffs);
            Log($"节点比较: {checkedNodes} 个，差异 {diffs.Count} 个");
            foreach (var d in diffs.Take(10)) Log("   [DIFF] " + d);
            failures = diffs.Count;

            // 4. BOM/格式检查
            var bytes = File.ReadAllBytes(exportPath);
            bool hasBom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
            var text = Encoding.ASCII.GetString(bytes);
            bool hasCrLf = text.Contains("\r\n");
            int head = hasBom ? 3 : 0;
            bool hasDoctype = Encoding.ASCII.GetString(bytes, head, Math.Min(200, bytes.Length - head))
                .StartsWith("<!DOCTYPE NETSCAPE-Bookmark-file-1>");
            Log($"格式: BOM={hasBom} CRLF={hasCrLf} DOCTYPE={hasDoctype}");
            if (!hasBom || !hasCrLf || !hasDoctype) failures++;

            Log(failures == 0 ? "== PASS ==" : $"== FAIL ({failures}) ==");
        }
        catch (Exception ex)
        {
            Log($"[FAIL] 异常: {ex}");
            failures++;
        }

        WriteReport(log.ToString());
        return failures == 0 ? 0 : 1;
    }

    private static int Compare(BookmarkNode a, BookmarkNode b, string path, List<string> diffs)
    {
        int n = 1;
        if (a.Name != b.Name) diffs.Add($"{path}名称: “{a.Name}” vs “{b.Name}”");
        if (a.IsFolder != b.IsFolder) diffs.Add($"{path}类型不一致");
        if (!a.IsFolder)
        {
            if (a.Path != b.Path) diffs.Add($"{path}“{a.Name}”路径: {a.Path} vs {b.Path}");
            if ((a.Icon ?? "") != (b.Icon ?? "")) diffs.Add($"{path}“{a.Name}”图标不一致");
        }
        if (a.Children.Count != b.Children.Count)
            diffs.Add($"{path}“{a.Name}”子项数: {a.Children.Count} vs {b.Children.Count}");
        int count = Math.Min(a.Children.Count, b.Children.Count);
        for (int i = 0; i < count; i++)
            n += Compare(a.Children[i], b.Children[i], $"{path}{a.Name}/", diffs);
        return n;
    }

    private static void WriteReport(string text)
    {
        try
        {
            var report = Path.Combine(Path.GetTempPath(), "explorersider_selftest.txt");
            File.WriteAllText(report, text, new UTF8Encoding(false));
            Console.WriteLine($"报告: {report}");
        }
        catch { }
    }
}

using System.IO;

namespace ExplorerSider.Services;

/// <summary>
/// 极简文本日志（诊断悬浮栏偶发不显示等状态问题）。
/// 只记关键事件（启动/呼出/收起/热键/救援），写 %APPDATA%\ExplorerSider\log.txt，超 64KB 截断保留后半。
/// </summary>
public static class AppLog
{
    private static readonly string LogFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ExplorerSider", "log.txt");
    private static readonly object _lock = new();

    public static void Write(string msg)
    {
        try
        {
            lock (_lock)
            {
                if (File.Exists(LogFile))
                {
                    var fi = new FileInfo(LogFile);
                    if (fi.Length > 64 * 1024) // 只留后半，防无限膨胀
                    {
                        var text = File.ReadAllText(LogFile);
                        File.WriteAllText(LogFile, text[(text.Length / 2)..]);
                    }
                }
                File.AppendAllText(LogFile, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {msg}\r\n");
            }
        }
        catch { /* 日志失败绝不影响主流程 */ }
    }
}

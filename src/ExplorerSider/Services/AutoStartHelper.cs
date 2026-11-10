using Microsoft.Win32;

namespace ExplorerSider.Services;

/// <summary>开机自启：HKCU\Software\Microsoft\Windows\CurrentVersion\Run。</summary>
public static class AutoStartHelper
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "ExplorerSider";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) != null;
    }

    public static void SetEnabled(bool enable)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (key == null) return;
        if (enable)
            key.SetValue(ValueName, $"\"{Environment.ProcessPath}\"");
        else
            key.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    /// <summary>exe 移动位置后自启路径失效：启动时若已启用则刷新为当前路径。</summary>
    public static void RefreshPathIfEnabled()
    {
        if (!IsEnabled()) return;
        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe)) return;
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        var cur = key?.GetValue(ValueName) as string;
        if (cur != null && cur != $"\"{exe}\"")
            key!.SetValue(ValueName, $"\"{exe}\"");
    }
}

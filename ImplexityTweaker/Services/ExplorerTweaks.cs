using Microsoft.Win32;

namespace ImplexityTweaker.Services;

public static class ExplorerTweaks
{
    private const string Advanced = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";

    public static bool GetShowHiddenFiles() => GetDword(Advanced, "Hidden") == 1;
    public static bool GetShowSystemFiles() => GetDword(Advanced, "ShowSuperHidden") == 1;
    public static bool GetShowFileExtensions() => GetDword(Advanced, "HideFileExt") == 0;
    public static int GetLaunchTo() => GetDword(Advanced, "LaunchTo");

    public static void SetShowHiddenFiles(bool enabled) =>
        SetDword(Advanced, "Hidden", enabled ? 1 : 2);

    public static void SetShowSystemFiles(bool enabled) =>
        SetDword(Advanced, "ShowSuperHidden", enabled ? 1 : 0);

    public static void SetShowFileExtensions(bool enabled) =>
        SetDword(Advanced, "HideFileExt", enabled ? 0 : 1);

    public static void SetLaunchToThisPc(bool enabled) =>
        SetDword(Advanced, "LaunchTo", enabled ? 1 : 2);

    private static int GetDword(string path, string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(path, false);
        return key?.GetValue(name) as int? ?? 0;
    }

    private static void SetDword(string path, string name, int value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(path, true);
        key?.SetValue(name, value, RegistryValueKind.DWord);
    }
}

using Microsoft.Win32;

namespace ImplexityTweaker.Services;

/// <summary>
/// Твики специальных возможностей (Accessibility).
/// </summary>
public static class AccessibilityTweaks
{
    private const string StickyKeysPath = @"Control Panel\Accessibility\StickyKeys";

    /// <summary>True, если «залипание клавиш» отключено (Flags = "0").</summary>
    public static bool GetStickyKeysDisabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(StickyKeysPath, false);
            var flags = key?.GetValue("Flags") as string;
            return flags == "0";
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Отключает (или возвращает) «залипание клавиш».
    /// Отключение: Flags = 0, HotkeyActive = 0, HotkeyApproved = 0.
    /// Включение: стандартные значения по умолчанию (Flags = 510, хоткей активен).
    /// </summary>
    public static void SetStickyKeysDisabled(bool disable)
    {
        using var key = Registry.CurrentUser.CreateSubKey(StickyKeysPath, true);
        if (key == null)
            return;

        if (disable)
        {
            key.SetValue("Flags", "0", RegistryValueKind.String);
            key.SetValue("HotkeyActive", "0", RegistryValueKind.String);
            key.SetValue("HotkeyApproved", "0", RegistryValueKind.String);
        }
        else
        {
            key.SetValue("Flags", "510", RegistryValueKind.String);
            key.SetValue("HotkeyActive", "1", RegistryValueKind.String);
            key.SetValue("HotkeyApproved", "1", RegistryValueKind.String);
        }
    }
}

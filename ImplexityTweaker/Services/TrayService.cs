using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;
using ImplexityTweaker.Windows;

namespace ImplexityTweaker.Services;

/// <summary>
/// Системный трей и автозапуск для фоновых функций Implexity Tweaker.
/// Работает ТОЛЬКО тогда, когда включена хотя бы одна функция, требующая фонового наблюдения
/// (авто-приоритет процессов, привязка к физическим ядрам, автоочистка ОЗУ по порогу или при запуске игр).
/// </summary>
public static class TrayService
{
    private const string RunRegKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "ImplexityTweaker";

    private const int WM_USER = 0x0400;
    private const int WM_TRAYMESSAGE = WM_USER + 202;
    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_LBUTTONDBLCLK = 0x0203;
    private const int WM_RBUTTONUP = 0x0205;
    private const int WM_CONTEXTMENU = 0x007B;

    private const uint NIM_ADD = 0x00000000;
    private const uint NIM_MODIFY = 0x00000001;
    private const uint NIM_DELETE = 0x00000002;

    private const uint NIF_MESSAGE = 0x00000001;
    private const uint NIF_ICON = 0x00000002;
    private const uint NIF_TIP = 0x00000004;
    private const uint NIF_INFO = 0x00000010;
    private const uint NIIF_INFO = 0x00000001;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public int cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public uint uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIconW(uint dwMessage, ref NotifyIconData lpData);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr ExtractAssociatedIconW(IntPtr hInst, string lpIconPath, ref ushort lpiIcon);

    [DllImport("user32.dll")]
    private static extern IntPtr LoadIcon(IntPtr hInstance, IntPtr lpIconName);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    private static HwndSource? _hwndSource;
    private static IntPtr _hIcon;
    private static bool _ownsIcon;
    private static bool _iconAdded;
    private static bool _forceExit;
    private static TrayMenuWindow? _trayMenuWindow;

    public static bool ForceExit => _forceExit;

    /// <summary>Проверяет, включена ли хотя бы одна фоновая функция, требующая работы в трее.</summary>
    public static bool HasActiveBackgroundFeatures()
        => ProcessTweaksStore.Load().RequiresBackgroundTray || OverlayService.IsRunning;

    /// <summary>Возвращает список названий активных фоновых функций для уведомления пользователя.</summary>
    public static List<string> GetActiveFeatureNames()
    {
        var t = ProcessTweaksStore.Load();
        var list = new List<string>();
        if (t.AutoPriorityEnabled)
            list.Add(LocalizationService.TF("Авто-приоритет ({0})", t.AutoPriorityProcess));
        if (t.AffinityEnabled)
            list.Add(LocalizationService.TF("Только физические ядра ({0})", t.AffinityProcess));
        if (t.AutoRamThresholdEnabled)
            list.Add(LocalizationService.TF("Автоочистка ОЗУ (> {0}%)", t.AutoRamThresholdPercent));
        if (t.AutoRamOnGameLaunchEnabled)
            list.Add(LocalizationService.T("Автоочистка ОЗУ при запуске игр"));
        if (OverlayService.IsRunning)
            list.Add("ImplexityOverlay");
        return list;
    }

    /// <summary>
    /// Синхронизирует состояние иконки в трее и автозапуска с текущими настройками.
    /// Показывает иконку только если включены фоновые функции.
    /// </summary>
    public static void SyncWithSettings(bool notifyIfActivated = false)
    {
        try
        {
            var active = HasActiveBackgroundFeatures();
            if (active)
            {
                bool wasAdded = _iconAdded;
                EnsureTrayIcon();
                UpdateTooltip();

                if (notifyIfActivated && !wasAdded)
                {
                    var features = string.Join(", ", GetActiveFeatureNames());
                    ShowBalloon(
                        LocalizationService.T("Фоновый режим Implexity активен"),
                        LocalizationService.TF("Включены фоновые функции ({0}). При закрытии окна твикер останется работать в системном трее.", features));
                }
            }
            else
            {
                RemoveTrayIcon();
                // Если все фоновые функции выключены — убираем и автозапуск в трее, чтобы не висеть в системе зря.
                if (GetAutostartEnabled())
                    SetAutostartEnabled(false);
            }
        }
        catch
        {
            // не ломаем приложение при ошибке shell32
        }
    }

    public static void ShowBalloon(string title, string message)
    {
        try
        {
            if (!_iconAdded || _hwndSource == null)
                return;

            var data = CreateBaseData();
            data.uFlags = NIF_INFO | NIF_TIP;
            data.szInfoTitle = Truncate(title, 60);
            data.szInfo = Truncate(message, 250);
            data.dwInfoFlags = NIIF_INFO;
            data.uTimeoutOrVersion = 4000;
            Shell_NotifyIconW(NIM_MODIFY, ref data);
        }
        catch
        {
            // ignore
        }
    }

    private static void EnsureTrayIcon()
    {
        if (_iconAdded)
            return;

        if (_hwndSource == null)
        {
            var parameters = new HwndSourceParameters("ImplexityTraySink")
            {
                Width = 0,
                Height = 0,
                WindowStyle = 0
            };
            _hwndSource = new HwndSource(parameters);
            _hwndSource.AddHook(WndProc);
        }

        if (_hIcon == IntPtr.Zero)
        {
            try
            {
                var exePath = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(exePath))
                {
                    ushort idx = 0;
                    _hIcon = ExtractAssociatedIconW(IntPtr.Zero, exePath, ref idx);
                    _ownsIcon = _hIcon != IntPtr.Zero;
                }
            }
            catch { }

            if (_hIcon == IntPtr.Zero)
            {
                _hIcon = LoadIcon(IntPtr.Zero, new IntPtr(32512)); // IDI_APPLICATION
                _ownsIcon = false;
            }
        }

        var data = CreateBaseData();
        data.uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP;
        Shell_NotifyIconW(NIM_ADD, ref data);
        _iconAdded = true;
    }

    private static void UpdateTooltip()
    {
        if (!_iconAdded || _hwndSource == null)
            return;

        var data = CreateBaseData();
        data.uFlags = NIF_TIP;
        Shell_NotifyIconW(NIM_MODIFY, ref data);
    }

    public static void RemoveTrayIcon()
    {
        if (!_iconAdded || _hwndSource == null)
            return;

        try
        {
            var data = CreateBaseData();
            Shell_NotifyIconW(NIM_DELETE, ref data);
        }
        catch { }
        finally
        {
            _iconAdded = false;
        }
    }

    private static NotifyIconData CreateBaseData()
    {
        var features = GetActiveFeatureNames();
        var tip = features.Count > 0
            ? $"Implexity Tweaker — {string.Join(", ", features)}"
            : "Implexity Tweaker";

        return new NotifyIconData
        {
            cbSize = Marshal.SizeOf<NotifyIconData>(),
            hWnd = _hwndSource?.Handle ?? IntPtr.Zero,
            uID = 1,
            uCallbackMessage = WM_TRAYMESSAGE,
            hIcon = _hIcon,
            szTip = Truncate(tip, 120),
            szInfo = string.Empty,
            szInfoTitle = string.Empty
        };
    }

    private static string Truncate(string value, int maxLength)
        => string.IsNullOrEmpty(value) ? string.Empty : (value.Length <= maxLength ? value : value[..(maxLength - 1)] + "…");

    private static IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_TRAYMESSAGE)
        {
            int eventId = (int)(lParam.ToInt64() & 0xFFFF);
            if (eventId is WM_LBUTTONUP or WM_LBUTTONDBLCLK)
            {
                handled = true;
                Application.Current?.Dispatcher.BeginInvoke(RestoreMainWindow);
            }
            else if (eventId is WM_RBUTTONUP or WM_CONTEXTMENU)
            {
                handled = true;
                Application.Current?.Dispatcher.BeginInvoke(OpenTrayMenu);
            }
        }
        return IntPtr.Zero;
    }

    private static void OpenTrayMenu()
    {
        try
        {
            if (_trayMenuWindow != null)
            {
                try { _trayMenuWindow.Close(); } catch { }
                _trayMenuWindow = null;
            }

            if (_hwndSource != null)
                SetForegroundWindow(_hwndSource.Handle);

            _trayMenuWindow = new TrayMenuWindow();
            _trayMenuWindow.Closed += (_, _) => _trayMenuWindow = null;
            _trayMenuWindow.Show();
            _trayMenuWindow.Activate();
        }
        catch { }
    }

    public static void RestoreMainWindow()
    {
        if (Application.Current?.MainWindow is not Window win)
            return;

        if (win is MainWindow mw)
            mw.RestoreFromBackgroundTrayMode();

        if (!win.IsVisible)
            win.Show();

        if (win.WindowState == WindowState.Minimized)
            win.WindowState = WindowState.Normal;

        win.Activate();
        win.Topmost = true;
        win.Topmost = false;
        win.Focus();
    }

    public static void ExitApplication()
    {
        _forceExit = true;
        OverlayService.ShutdownAll();
        RemoveTrayIcon();
        if (_hwndSource != null)
        {
            try
            {
                _hwndSource.RemoveHook(WndProc);
                _hwndSource.Dispose();
            }
            catch { }
            _hwndSource = null;
        }
        if (_ownsIcon && _hIcon != IntPtr.Zero)
        {
            try { DestroyIcon(_hIcon); } catch { }
            _hIcon = IntPtr.Zero;
            _ownsIcon = false;
        }
        Application.Current?.Shutdown();
    }

    // ===================== Автозапуск с Windows =====================

    public static bool GetAutostartEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunRegKey, false);
            var val = key?.GetValue(RunValueName) as string;
            return !string.IsNullOrWhiteSpace(val);
        }
        catch
        {
            return false;
        }
    }

    public static void SetAutostartEnabled(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunRegKey, true);
            if (key == null)
                return;

            if (enable)
            {
                var exePath = Environment.ProcessPath;
                if (!string.IsNullOrWhiteSpace(exePath))
                    key.SetValue(RunValueName, $"\"{exePath}\" --minimized", RegistryValueKind.String);
            }
            else
            {
                key.DeleteValue(RunValueName, throwOnMissingValue: false);
            }
        }
        catch
        {
            // ignore
        }
    }
}

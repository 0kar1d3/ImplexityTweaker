using Microsoft.Win32;
using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using ImplexityTweaker.Services;
using ImplexityTweaker.Windows;

namespace ImplexityTweaker.Pages;

public partial class FunSettingsPage : Page
{
    // Ключ реестра, где хранится название процессора, отображаемое в диспетчере задач.
    private const string CpuKeyPath = @"HARDWARE\DESCRIPTION\System\CentralProcessor\0";
    private const string CpuValueName = "ProcessorNameString";

    // Ключ реестра для текста перед экраном входа в Windows.
    private const string LogonNoticeKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System";
    private const string LogonNoticeCaptionValue = "legalnoticecaption";
    private const string LogonNoticeTextValue = "legalnoticetext";

    // Win32 API для поворота экрана.
    private const int ENUM_CURRENT_SETTINGS = -1;
    private const uint DM_DISPLAYORIENTATION = 0x00000080;
    private const uint DM_PELSWIDTH = 0x00080000;
    private const uint DM_PELSHEIGHT = 0x00100000;
    private const uint DMDO_DEFAULT = 0;
    private const uint DMDO_180 = 2;
    private const int DISP_CHANGE_SUCCESSFUL = 0;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DEVMODEW
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string dmDeviceName;
        public ushort dmSpecVersion;
        public ushort dmDriverVersion;
        public ushort dmSize;
        public ushort dmDriverExtra;
        public uint dmFields;
        public int dmPositionX;
        public int dmPositionY;
        public uint dmDisplayOrientation;
        public uint dmDisplayFixedOutput;
        public short dmColor;
        public short dmDuplex;
        public short dmYResolution;
        public short dmTTOption;
        public short dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string dmFormName;
        public ushort dmLogPixels;
        public uint dmBitsPerPel;
        public uint dmPelsWidth;
        public uint dmPelsHeight;
        public uint dmDisplayFlags;
        public uint dmDisplayFrequency;
        public uint dmICMMethod;
        public uint dmICMIntent;
        public uint dmMediaType;
        public uint dmDitherType;
        public uint dmReserved1;
        public uint dmReserved2;
        public uint dmPanningWidth;
        public uint dmPanningHeight;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplaySettingsW(string? lpszDeviceName, int iModeNum, ref DEVMODEW lpDevMode);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int ChangeDisplaySettingsW(ref DEVMODEW lpDevMode, uint dwFlags);

    private DispatcherTimer? _revertTimer;
    private int _revertSecondsLeft;

    public FunSettingsPage()
    {
        InitializeComponent();
        LoadCurrentValues();
        Unloaded += (_, _) => StopRevertTimer();
    }

    private void LoadCurrentValues()
    {
        // 1. Текущее название процессора.
        try
        {
            using var cpuKey = Registry.LocalMachine.OpenSubKey(CpuKeyPath, false);
            var cpuName = cpuKey?.GetValue(CpuValueName) as string;
            CurrentCpuText.Text = string.IsNullOrWhiteSpace(cpuName)
                ? LocalizationService.T("Не удалось определить название процессора.")
                : LocalizationService.TF("Текущее название: {0}", cpuName);
        }
        catch
        {
            CurrentCpuText.Text = LocalizationService.T("Не удалось прочитать название процессора.");
        }

        // 2. Текст при входе в Windows.
        try
        {
            using var sysKey = Registry.LocalMachine.OpenSubKey(LogonNoticeKeyPath, false);
            LoginNoticeCaptionBox.Text = sysKey?.GetValue(LogonNoticeCaptionValue) as string ?? string.Empty;
            LoginNoticeTextBox.Text = sysKey?.GetValue(LogonNoticeTextValue) as string ?? string.Empty;
        }
        catch
        {
        }

        // 3. Текущая ориентация экрана.
        UpdateRotationStatus();
    }

    // ===================== 1. Процессор =====================

    private async void ApplyCpu_Click(object sender, RoutedEventArgs e)
    {
        var name = CpuNameBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            await Dialogs.ShowErrorAsync("Ошибка", "Введите название процессора.");
            return;
        }

        var ok = await TryWriteRegistryAsync(CpuKeyPath, CpuValueName, name);
        if (ok)
        {
            CurrentCpuText.Text = LocalizationService.TF("Текущее название: {0}", name);
            await Dialogs.ShowInfoAsync("Готово",
                "Название процессора изменено. Оно вступит в силу сразу и сбросится после перезагрузки ПК.");
        }
        else
        {
            await Dialogs.ShowErrorAsync("Ошибка",
                "Не удалось изменить название процессора. Убедитесь, что приложение запущено от имени администратора.");
        }
    }

    private async void ResetCpu_Click(object sender, RoutedEventArgs e)
    {
        var ok = await TryResetRegistryAsync(CpuKeyPath, CpuValueName);
        if (ok)
        {
            CpuNameBox.Text = string.Empty;
            LoadCurrentValues();
            await Dialogs.ShowInfoAsync("Готово", "Название процессора сброшено.");
        }
        else
        {
            await Dialogs.ShowErrorAsync("Ошибка",
                "Не удалось сбросить название процессора. Убедитесь, что приложение запущено от имени администратора.");
        }
    }

    // ===================== 2. Текст при входе в Windows =====================

    private async void ApplyLoginNotice_Click(object sender, RoutedEventArgs e)
    {
        var caption = LoginNoticeCaptionBox.Text?.Trim() ?? string.Empty;
        var text = LoginNoticeTextBox.Text?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(caption) && string.IsNullOrWhiteSpace(text))
        {
            await Dialogs.ShowErrorAsync("Ошибка", "Введите заголовок или текст сообщения.");
            return;
        }

        var ok1 = await TryWriteRegistryAsync(LogonNoticeKeyPath, LogonNoticeCaptionValue, caption);
        var ok2 = await TryWriteRegistryAsync(LogonNoticeKeyPath, LogonNoticeTextValue, text);

        if (ok1 && ok2)
        {
            await Dialogs.ShowInfoAsync("Готово",
                "Сообщение при входе в Windows сохранено. Оно появится при следующем входе в систему или блокировке.");
        }
        else
        {
            await Dialogs.ShowErrorAsync("Ошибка",
                "Не удалось сохранить сообщение. Убедитесь, что приложение запущено от имени администратора.");
        }
    }

    private async void ResetLoginNotice_Click(object sender, RoutedEventArgs e)
    {
        var ok1 = await TryWriteRegistryAsync(LogonNoticeKeyPath, LogonNoticeCaptionValue, string.Empty);
        var ok2 = await TryWriteRegistryAsync(LogonNoticeKeyPath, LogonNoticeTextValue, string.Empty);

        if (ok1 && ok2)
        {
            LoginNoticeCaptionBox.Text = string.Empty;
            LoginNoticeTextBox.Text = string.Empty;
            await Dialogs.ShowInfoAsync("Готово", "Сообщение при входе в Windows отключено.");
        }
        else
        {
            await Dialogs.ShowErrorAsync("Ошибка",
                "Не удалось сбросить сообщение. Убедитесь, что приложение запущено от имени администратора.");
        }
    }

    // ===================== 3. Поворот экрана на 180° =====================

    private async void Rotate180_Click(object sender, RoutedEventArgs e)
    {
        if (!TrySetDisplayOrientation(DMDO_180))
        {
            await Dialogs.ShowErrorAsync("Ошибка", "Драйвер видеокарты не поддерживает поворот этого дисплея.");
            return;
        }

        StartRevertCountdown();
    }

    private void KeepRotation_Click(object sender, RoutedEventArgs e)
    {
        StopRevertTimer();
        UpdateRotationStatus();
    }

    private async void RotateReset_Click(object sender, RoutedEventArgs e)
    {
        StopRevertTimer();
        if (!TrySetDisplayOrientation(DMDO_DEFAULT))
        {
            await Dialogs.ShowErrorAsync("Ошибка", "Не удалось вернуть стандартную ориентацию экрана.");
            return;
        }

        UpdateRotationStatus();
    }

    private void StartRevertCountdown()
    {
        StopRevertTimer();
        _revertSecondsLeft = 10;
        KeepRotationButton.Visibility = Visibility.Visible;
        RotationStatusText.Text = LocalizationService.TF(
            "Экран перевёрнут на 180°. Автоматический возврат через {0} сек…", _revertSecondsLeft);

        _revertTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _revertTimer.Tick += (_, _) =>
        {
            _revertSecondsLeft--;
            if (_revertSecondsLeft <= 0)
            {
                StopRevertTimer();
                TrySetDisplayOrientation(DMDO_DEFAULT);
                UpdateRotationStatus();
                return;
            }

            RotationStatusText.Text = LocalizationService.TF(
                "Экран перевёрнут на 180°. Автоматический возврат через {0} сек…", _revertSecondsLeft);
        };
        _revertTimer.Start();
    }

    private void StopRevertTimer()
    {
        if (_revertTimer != null)
        {
            _revertTimer.Stop();
            _revertTimer = null;
        }
        KeepRotationButton.Visibility = Visibility.Collapsed;
    }

    private void UpdateRotationStatus()
    {
        var dm = new DEVMODEW { dmSize = (ushort)Marshal.SizeOf<DEVMODEW>() };
        if (EnumDisplaySettingsW(null, ENUM_CURRENT_SETTINGS, ref dm))
        {
            var deg = dm.dmDisplayOrientation switch
            {
                1 => "90°",
                2 => "180°",
                3 => "270°",
                _ => "0°"
            };
            RotationStatusText.Text = LocalizationService.TF("Текущая ориентация: {0}", deg);
        }
        else
        {
            RotationStatusText.Text = LocalizationService.T("Текущая ориентация: 0°");
        }
    }

    private static bool TrySetDisplayOrientation(uint targetOrientation)
    {
        var dm = new DEVMODEW { dmSize = (ushort)Marshal.SizeOf<DEVMODEW>() };
        if (!EnumDisplaySettingsW(null, ENUM_CURRENT_SETTINGS, ref dm))
            return false;

        // Если текущий угол 90° или 270°, меняем ширину и высоту местами перед установкой 0°/180°.
        if (dm.dmDisplayOrientation is 1 or 3)
            (dm.dmPelsWidth, dm.dmPelsHeight) = (dm.dmPelsHeight, dm.dmPelsWidth);

        dm.dmDisplayOrientation = targetOrientation;
        dm.dmFields |= DM_DISPLAYORIENTATION | DM_PELSWIDTH | DM_PELSHEIGHT;

        return ChangeDisplaySettingsW(ref dm, 0) == DISP_CHANGE_SUCCESSFUL;
    }

    // ===================== 4. Фейковый BSOD =====================

    private void LaunchFakeBsod_Click(object sender, RoutedEventArgs e)
    {
        var bsod = new FakeBsodWindow(BsodStopCodeBox.Text)
        {
            Owner = Window.GetWindow(this)
        };
        bsod.ShowDialog();
    }

    // Запись строкового значения в реестр (HKLM). Требует прав администратора.
    private static Task<bool> TryWriteRegistryAsync(string keyPath, string valueName, string value)
    {
        return Task.Run(() =>
        {
            try
            {
                using var key = Registry.LocalMachine.CreateSubKey(keyPath, true);
                key?.SetValue(valueName, value, RegistryValueKind.String);
                return true;
            }
            catch
            {
                return false;
            }
        });
    }

    // Удаление значения из реестра (HKLM), чтобы вернуть исходное название.
    private static Task<bool> TryResetRegistryAsync(string keyPath, string valueName)
    {
        return Task.Run(() =>
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(keyPath, true);
                if (key == null)
                    return true;

                if (key.GetValue(valueName) != null)
                    key.DeleteValue(valueName, false);

                return true;
            }
            catch
            {
                return false;
            }
        });
    }
}

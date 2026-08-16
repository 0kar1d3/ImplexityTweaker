using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace ImplexityTweaker.Services;

public class AppSettings
{
    /// <summary>Маркер формата файла конфигурации .implexity.</summary>
    public const string ConfigFormatMarker = "implexity-config";

    /// <summary>Версия формата файла конфигурации.</summary>
    public const int ConfigVersion = 2;

    /// <summary>Расширение файла конфигурации.</summary>
    public const string ConfigExtension = ".implexity";

    public string ThemeId { get; set; } = ThemeApplier.ThemeImplexity;

    // ===== Оптимизация (OptimizationPage) =====
    public bool? DisableFso { get; set; }
    public bool? DisableMpo { get; set; }
    public bool? DisableGameDvr { get; set; }
    public bool? DisableNagle { get; set; }
    public bool? DisableAutoTuning { get; set; }
    public bool? HighPriority { get; set; }
    public bool? PrioritizeBurst { get; set; }
    public bool? DisableWindowsAnimation { get; set; }
    public bool? DisableVisualEffects { get; set; }
    public int? ProcessorState { get; set; }

    // ===== Проводник (ExplorerPage) =====
    public bool? ShowHiddenFiles { get; set; }
    public bool? ShowSystemFiles { get; set; }
    public bool? ShowFileExtensions { get; set; }
    public bool? LaunchToThisPc { get; set; }

    // ===== Контекстное меню (ContextMenuPage) =====
    public bool? OldContextMenu { get; set; }
    public bool? RemoveContextDelay { get; set; }
    public bool? OpenInTerminal { get; set; }
    public bool? Share { get; set; }
    public bool? RestoreOldVersion { get; set; }
    public bool? Send { get; set; }
    public bool? CopyPath { get; set; }
    public bool? PinStart { get; set; }
    public bool? PinTaskbar { get; set; }

    // ===== Телеметрия (TelemetryPage) =====
    public bool? TelemetryPolicy { get; set; }
    public bool? TelemetryDiagTrack { get; set; }
    public bool? TelemetryDmwappush { get; set; }
    public bool? TelemetryCompatAppraiser { get; set; }
    public bool? TelemetryDeviceCensus { get; set; }
    public bool? TelemetryAdvertisingId { get; set; }
    public bool? TelemetryTailoredExperiences { get; set; }
    public bool? TelemetryStartSuggestions { get; set; }
    public bool? TelemetryFeedbackFrequency { get; set; }
    public bool? TelemetryWerConsent { get; set; }

    // ===== Персонализация (PersonalizationPage) =====
    public bool? Transparency { get; set; }
    public bool? EndTask { get; set; }
    public bool? SmallWindowButtons { get; set; }
    public bool? DisableLockScreenBlur { get; set; }
    public bool? DarkTheme { get; set; }
    public bool? SuperDetailedInfo { get; set; }
    public bool? DisableBootLogo { get; set; }
    public bool? DisableBootAnimation { get; set; }

    // ===== Быстрая настройка (FirstSetupPage) =====
    public bool? ShowHidden { get; set; }
    public bool? ShowExtensions { get; set; }
    public bool? ThisPc { get; set; }
    public bool? PauseWu { get; set; }
    public bool? ThisPcDesktop { get; set; }
    public bool? NoShortcutSuffix { get; set; }
    public bool? HideTaskViewWidgets { get; set; }
    public bool? ReduceAds { get; set; }

    // ===== Windows Update (WindowsUpdatePage) =====
    public string? WuStartupMode { get; set; }

    private static string FilePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ImplexityTweaker", "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                var s = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                s.ThemeId = ThemeApplier.NormalizeThemeId(s.ThemeId);
                return s;
            }
        }
        catch
        {
            /* ignore */
        }

        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            /* ignore */
        }
    }

    /// <summary>
    /// Экспортирует текущие настройки в файл конфигурации .implexity.
    /// Возвращает true при успехе.
    /// </summary>
    public bool ExportToFile(string path)
    {
        try
        {
            var wrapper = new ConfigFile
            {
                Format = ConfigFormatMarker,
                Version = ConfigVersion,
                Settings = this
            };

            var json = JsonSerializer.Serialize(wrapper, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Импортирует настройки из файла конфигурации .implexity.
    /// Применяет их к текущему экземпляру и возвращает true при успехе.
    /// </summary>
    public bool ImportFromFile(string path)
    {
        try
        {
            if (!File.Exists(path))
                return false;

            var json = File.ReadAllText(path);
            var wrapper = JsonSerializer.Deserialize<ConfigFile>(json);
            if (wrapper == null || !string.Equals(wrapper.Format, ConfigFormatMarker, StringComparison.OrdinalIgnoreCase))
                return false;

            var settings = wrapper.Settings ?? new AppSettings();
            settings.ThemeId = ThemeApplier.NormalizeThemeId(settings.ThemeId);

            // Копируем все свойства импортированных настроек в текущий экземпляр.
            CopyFrom(settings);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Копирует значения всех свойств из другого экземпляра AppSettings в текущий.
    /// </summary>
    public void CopyFrom(AppSettings source)
    {
        ThemeId = source.ThemeId;

        DisableFso = source.DisableFso;
        DisableMpo = source.DisableMpo;
        DisableGameDvr = source.DisableGameDvr;
        DisableNagle = source.DisableNagle;
        DisableAutoTuning = source.DisableAutoTuning;
        HighPriority = source.HighPriority;
        PrioritizeBurst = source.PrioritizeBurst;
        DisableWindowsAnimation = source.DisableWindowsAnimation;
        DisableVisualEffects = source.DisableVisualEffects;
        ProcessorState = source.ProcessorState;

        ShowHiddenFiles = source.ShowHiddenFiles;
        ShowSystemFiles = source.ShowSystemFiles;
        ShowFileExtensions = source.ShowFileExtensions;
        LaunchToThisPc = source.LaunchToThisPc;

        OldContextMenu = source.OldContextMenu;
        RemoveContextDelay = source.RemoveContextDelay;
        OpenInTerminal = source.OpenInTerminal;
        Share = source.Share;
        RestoreOldVersion = source.RestoreOldVersion;
        Send = source.Send;
        CopyPath = source.CopyPath;
        PinStart = source.PinStart;
        PinTaskbar = source.PinTaskbar;

        TelemetryPolicy = source.TelemetryPolicy;
        TelemetryDiagTrack = source.TelemetryDiagTrack;
        TelemetryDmwappush = source.TelemetryDmwappush;
        TelemetryCompatAppraiser = source.TelemetryCompatAppraiser;
        TelemetryDeviceCensus = source.TelemetryDeviceCensus;
        TelemetryAdvertisingId = source.TelemetryAdvertisingId;
        TelemetryTailoredExperiences = source.TelemetryTailoredExperiences;
        TelemetryStartSuggestions = source.TelemetryStartSuggestions;
        TelemetryFeedbackFrequency = source.TelemetryFeedbackFrequency;
        TelemetryWerConsent = source.TelemetryWerConsent;

        Transparency = source.Transparency;
        EndTask = source.EndTask;
        SmallWindowButtons = source.SmallWindowButtons;
        DisableLockScreenBlur = source.DisableLockScreenBlur;
        DarkTheme = source.DarkTheme;
        SuperDetailedInfo = source.SuperDetailedInfo;
        DisableBootLogo = source.DisableBootLogo;
        DisableBootAnimation = source.DisableBootAnimation;

        ShowHidden = source.ShowHidden;
        ShowExtensions = source.ShowExtensions;
        ThisPc = source.ThisPc;
        PauseWu = source.PauseWu;
        ThisPcDesktop = source.ThisPcDesktop;
        NoShortcutSuffix = source.NoShortcutSuffix;
        HideTaskViewWidgets = source.HideTaskViewWidgets;
        ReduceAds = source.ReduceAds;

        WuStartupMode = source.WuStartupMode;
    }

    /// <summary>
    /// Открывает диалог сохранения и экспортирует настройки в выбранный файл .implexity.
    /// Возвращает true при успехе.
    /// </summary>
    public bool ExportWithDialog()
    {
        var dlg = new SaveFileDialog
        {
            Title = "Сохранить настройки Implexity Tweaker",
            Filter = "Файл конфигурации Implexity (*.implexity)|*.implexity|Все файлы (*.*)|*.*",
            DefaultExt = "implexity",
            AddExtension = true,
            FileName = $"ImplexityTweaker-{DateTime.Now:yyyyMMdd-HHmm}.implexity"
        };

        if (dlg.ShowDialog() != true)
            return false;

        return ExportToFile(dlg.FileName);
    }

    /// <summary>
    /// Открывает диалог открытия и импортирует настройки из выбранного файла .implexity.
    /// Возвращает true при успехе.
    /// </summary>
    public bool ImportWithDialog()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Загрузить настройки Implexity Tweaker",
            Filter = "Файл конфигурации Implexity (*.implexity)|*.implexity|Все файлы (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dlg.ShowDialog() != true)
            return false;

        return ImportFromFile(dlg.FileName);
    }

    /// <summary>
    /// Обёртка файла конфигурации .implexity: маркер формата + версия + сами настройки.
    /// </summary>
    private sealed class ConfigFile
    {
        public string Format { get; set; } = ConfigFormatMarker;
        public int Version { get; set; } = ConfigVersion;
        public AppSettings? Settings { get; set; }
    }
}

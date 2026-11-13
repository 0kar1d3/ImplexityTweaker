using System.Collections.Generic;
using System.Linq;

namespace ImplexityTweaker.Services;

/// <summary>Насколько пресет «наглый» — насколько заметны изменения для пользователя.</summary>
public enum PresetRisk
{
    Safe,
    Moderate,
    Aggressive,
}

/// <summary>Один шаг пресета: подпись (ключ локализации) и действие.</summary>
public sealed class PresetStep
{
    /// <summary>Подпись шага на русском (она же ключ перевода).</summary>
    public required string Label { get; init; }

    /// <summary>Действие. Получает настройки, которые пресет правит (сохраняются один раз в конце).</summary>
    public Func<AppSettings, Task> Apply { get; init; } = _ => Task.CompletedTask;

    /// <summary>Условие доступности шага на текущей системе (например, только Windows 11).</summary>
    public Func<bool>? IsAvailable { get; init; }
}

/// <summary>Готовый набор настроек, который можно применить в один клик.</summary>
public sealed class PresetDefinition
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Category { get; init; }
    public required string Description { get; init; }

    /// <summary>Название иконки WPF-UI (SymbolIcon), например Games24.</summary>
    public required string Icon { get; init; }

    public PresetRisk Risk { get; init; } = PresetRisk.Safe;

    /// <summary>Минимальная сборка Windows, на которой пресет имеет смысл.</summary>
    public int MinBuild { get; init; }

    /// <summary>Максимальная сборка (0 — без ограничения).</summary>
    public int MaxBuild { get; init; }

    /// <summary>Пресет нужен только на Windows 11.</summary>
    public bool Windows11Only { get; init; }

    /// <summary>Требуемая редакция (начало EditionID), например Professional.</summary>
    public string? RequiredEdition { get; init; }

    /// <summary>Пресет сбрасывает сохранённые настройки приложения, а не правит систему.</summary>
    public bool IsReset { get; init; }

    /// <summary>Слова для поиска (Ctrl+K), через пробел.</summary>
    public string Keywords { get; init; } = string.Empty;

    public IReadOnlyList<PresetStep> Steps { get; init; } = [];
}

/// <summary>
/// Каталог пресетов: что именно применяется, на каких сборках это доступно и как
/// выполнить набор. Снимок перед изменениями и прогресс выполняет страница «Пресеты».
/// </summary>
public static class PresetCatalog
{
    public static IReadOnlyList<PresetDefinition> All { get; } = Create();

    /// <summary>Шаги пресета, доступные на текущей системе (недоступные пропускаются).</summary>
    public static IReadOnlyList<PresetStep> StepsFor(PresetDefinition preset)
        => preset.Steps.Where(s => s.IsAvailable?.Invoke() ?? true).ToList();

    /// <summary>Доступен ли пресет на этой сборке/редакции Windows.</summary>
    public static WindowsVersionService.Availability CheckAvailability(PresetDefinition preset)
        => WindowsVersionService.Check(preset.MinBuild, preset.MaxBuild, preset.Windows11Only, preset.RequiredEdition);

    /// <summary>Название уровня риска на текущем языке.</summary>
    public static string RiskLabel(PresetRisk risk) => risk switch
    {
        PresetRisk.Safe => LocalizationService.T("Безопасный"),
        PresetRisk.Moderate => LocalizationService.T("Умеренный"),
        _ => LocalizationService.T("Агрессивный"),
    };

    /// <summary>Выполняет PowerShell-скрипт шага; при ошибке бросает исключение (его покажет окно прогресса).</summary>
    private static async Task Ps(string script)
    {
        var r = await CommandRunner.RunPowerShellAsync(script);
        if (r.ExitCode != 0)
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(r.Error) ? r.Output : r.Error);
    }

    private static IReadOnlyList<PresetDefinition> Create() =>
    [
        new PresetDefinition
        {
            Id = "gaming",
            Title = "Игровой ПК",
            Category = "Игровая оптимизация",
            Icon = "Games24",
            Risk = PresetRisk.Moderate,
            Keywords = "игры game fps кадры производительность видеокарта mpo game dvr fso парковка ядер игровой режим",
            Description = "Отключает оптимизации во весь экран, MPO и запись Game DVR, включает игровой режим Windows, снимает парковку ядер CPU и включает план «Максимальная производительность».",
            Steps =
            [
                new PresetStep
                {
                    Label = "Отключение оптимизаций во весь экран (FSO)",
                    Apply = async s => { s.DisableFso = true; await OptimizationService.SetFullScreenOptimizationsAsync(true); },
                },
                new PresetStep
                {
                    Label = "Отключение MPO (Multi-Plane Overlay)",
                    Apply = async s => { s.DisableMpo = true; await OptimizationService.SetMpoDisabledAsync(true); },
                },
                new PresetStep
                {
                    Label = "Отключение записи Game DVR",
                    Apply = async s => { s.DisableGameDvr = true; await OptimizationService.SetGameDvrDisabledAsync(true); },
                },
                new PresetStep
                {
                    Label = "Включение игрового режима Windows",
                    Apply = async s => { s.QuickGameMode = true; await GamingService.SetGameModeAsync(true); },
                },
                new PresetStep
                {
                    Label = "Отключение парковки ядер CPU",
                    Apply = async s => { s.QuickCoreParking = true; await GamingService.SetCoreParkingAsync(true); },
                },
                new PresetStep
                {
                    Label = "Приоритет GPU для игр",
                    Apply = async s => { s.GpuPriority = true; await NetworkLatencyService.SetGpuPriorityAsync(true); },
                },
                new PresetStep
                {
                    Label = "Включение плана «Максимальная производительность»",
                    Apply = async s => { s.UltimatePerformance = true; await OptimizationService.SetUltimatePerformanceAsync(true); },
                },
            ],
        },
        new PresetDefinition
        {
            Id = "lowlatency",
            Title = "Низкая задержка сети",
            Category = "Сетевые настройки",
            Icon = "Wifi124",
            Risk = PresetRisk.Moderate,
            Keywords = "сеть ping задержка lag нагл nagle tcp dns онлайн игры",
            Description = "Отключает алгоритм Nagle и автотюнинг TCP, выставляет TcpAckFrequency и SystemResponsiveness = 10 и чистит кэш DNS — меньше задержка в онлайн-играх.",
            Steps =
            [
                new PresetStep
                {
                    Label = "Отключение алгоритма Nagle (TcpAckFrequency)",
                    Apply = async s => { s.DisableNagle = true; await OptimizationService.SetNagleDisabledAsync(true); },
                },
                new PresetStep
                {
                    Label = "Отключение автотюнинга TCP",
                    Apply = async s => { s.DisableAutoTuning = true; await OptimizationService.SetAutoTuningDisabledAsync(true); },
                },
                new PresetStep
                {
                    Label = "TcpAckFrequency для игр",
                    Apply = async s => { s.TcpAckFrequency = true; await NetworkLatencyService.SetTcpAckFrequencyAsync(true); },
                },
                new PresetStep
                {
                    Label = "SystemResponsiveness = 10",
                    Apply = async s => { s.DisableSystemResponsiveness = true; await NetworkLatencyService.SetSystemResponsivenessAsync(true); },
                },
                new PresetStep
                {
                    Label = "Очистка кэша DNS",
                    Apply = async _ => await OptimizationService.ClearDnsCacheAsync(),
                },
            ],
        },
        new PresetDefinition
        {
            Id = "privacy",
            Title = "Приватность и без рекламы",
            Category = "Приватность",
            Icon = "EyeOff24",
            Risk = PresetRisk.Moderate,
            Keywords = "приватность телеметрия реклама tracking diagtrack dmwappush советы рекомендации wer",
            Description = "Гасит телеметрию (AllowTelemetry = 0), отключает службы DiagTrack и dmwappushservice, рекламный идентификатор, рекомендации в Пуске, фоновые приложения и отчёты об ошибках.",
            Steps =
            [
                new PresetStep
                {
                    Label = "Политика телеметрии (AllowTelemetry = 0)",
                    Apply = async _ => await TelemetryTweaks.DisableAsync(
                        TelemetryTweaks.Selection.None with { Policy = true, CompatAppraiser = true, DeviceCensus = true }),
                },
                new PresetStep
                {
                    Label = "Отключение служб DiagTrack и dmwappushservice",
                    Apply = async _ => await TelemetryTweaks.DisableAsync(
                        TelemetryTweaks.Selection.None with { DiagTrack = true, Dmwappush = true }),
                },
                new PresetStep
                {
                    Label = "Отключение рекламного идентификатора",
                    Apply = async _ => await TelemetryTweaks.DisableAsync(
                        TelemetryTweaks.Selection.None with { AdvertisingId = true, TailoredExperiences = true }),
                },
                new PresetStep
                {
                    Label = "Отключение рекомендаций в Пуске",
                    Apply = async _ => await TelemetryTweaks.DisableAsync(
                        TelemetryTweaks.Selection.None with { StartSuggestions = true }),
                },
                new PresetStep
                {
                    Label = "Отключение отчётов об ошибках (WER)",
                    Apply = async _ => await TelemetryTweaks.DisableAsync(
                        TelemetryTweaks.Selection.None with { WerConsent = true, FeedbackFrequency = true }),
                },
                new PresetStep
                {
                    Label = "Отключение фоновых приложений",
                    Apply = async _ => await OptimizationService.SetBackgroundAppsDisabledAsync(true),
                },
            ],
        },
        new PresetDefinition
        {
            Id = "explorer",
            Title = "Проводник и контекстное меню",
            Category = "Проводник",
            Icon = "Folder24",
            Risk = PresetRisk.Safe,
            Keywords = "проводник explorer скрытые файлы расширения контекстное меню задержка этот компьютер",
            Description = "Показывает скрытые и системные файлы вместе с расширениями, открывает «Этот компьютер», возвращает классическое контекстное меню Windows 11 и убирает задержку его появления.",
            Steps =
            [
                new PresetStep
                {
                    Label = "Показ скрытых файлов",
                    Apply = s => { s.ShowHiddenFiles = true; ExplorerTweaks.SetShowHiddenFiles(true); return Task.CompletedTask; },
                },
                new PresetStep
                {
                    Label = "Показ расширений файлов",
                    Apply = s => { s.ShowFileExtensions = true; ExplorerTweaks.SetShowFileExtensions(true); return Task.CompletedTask; },
                },
                new PresetStep
                {
                    Label = "Показ системных файлов",
                    Apply = s => { s.ShowSystemFiles = true; ExplorerTweaks.SetShowSystemFiles(true); return Task.CompletedTask; },
                },
                new PresetStep
                {
                    Label = "Открывать «Этот компьютер» вместо «Главной»",
                    Apply = s => { s.LaunchToThisPc = true; ExplorerTweaks.SetLaunchToThisPc(true); return Task.CompletedTask; },
                },
                new PresetStep
                {
                    Label = "Классическое контекстное меню (Windows 11)",
                    IsAvailable = () => WindowsVersionService.IsWindows11,
                    Apply = async s =>
                    {
                        s.OldContextMenu = true;
                        await Ps(
                            "New-Item -Path 'HKCU:\\Software\\Classes\\CLSID\\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\\InprocServer32' -Force | Out-Null; " +
                            "Set-ItemProperty -Path 'HKCU:\\Software\\Classes\\CLSID\\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\\InprocServer32' -Name '(Default)' -Value '' -Force");
                    },
                },
                new PresetStep
                {
                    Label = "Без задержки при открытии контекстного меню",
                    Apply = async s =>
                    {
                        s.RemoveContextDelay = true;
                        await Ps("New-Item -Path 'HKCU:\\Control Panel\\Desktop' -Force | Out-Null; " +
                                 "Set-ItemProperty -Path 'HKCU:\\Control Panel\\Desktop' -Name MenuShowDelay -Type String -Value '0' -Force");
                    },
                },
                new PresetStep
                {
                    Label = "Перезапуск проводника Windows",
                    Apply = async _ => await Ps("Stop-Process -Name explorer -Force -ErrorAction SilentlyContinue; Start-Sleep -Milliseconds 800; Start-Process explorer.exe"),
                },
            ],
        },
        new PresetDefinition
        {
            Id = "responsive",
            Title = "Отзывчивый интерфейс",
            Category = "Интерфейс",
            Icon = "Layer24",
            Risk = PresetRisk.Safe,
            Keywords = "интерфейс анимации эффекты прозрачность размытие быстродействие ui",
            Description = "Отключает анимации окон и эффекты оформления, убирает прозрачность и размытие экрана блокировки — интерфейс реагирует заметно быстрее.",
            Steps =
            [
                new PresetStep
                {
                    Label = "Отключение анимаций окон",
                    Apply = async s => { s.DisableWindowsAnimation = true; await OptimizationService.SetWindowAnimationsDisabledAsync(true); },
                },
                new PresetStep
                {
                    Label = "Отключение эффектов оформления",
                    Apply = async s => { s.DisableVisualEffects = true; await OptimizationService.SetVisualEffectsDisabledAsync(true); },
                },
                new PresetStep
                {
                    Label = "Отключение прозрачности",
                    Apply = async s =>
                    {
                        s.Transparency = false;
                        await Ps("New-Item -Path 'HKCU:\\Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize' -Force | Out-Null; " +
                                 "Set-ItemProperty -Path 'HKCU:\\Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize' -Name EnableTransparency -Type DWord -Value 0 -Force");
                    },
                },
                new PresetStep
                {
                    Label = "Отключение размытия экрана блокировки",
                    Apply = async s =>
                    {
                        s.DisableLockScreenBlur = true;
                        await Ps("New-Item -Path 'HKLM:\\SOFTWARE\\Policies\\Microsoft\\Windows\\System' -Force | Out-Null; " +
                                 "Set-ItemProperty -Path 'HKLM:\\SOFTWARE\\Policies\\Microsoft\\Windows\\System' -Name DisableAcrylicBackgroundOnLogon -Type DWord -Value 1 -Force");
                    },
                },
            ],
        },
        new PresetDefinition
        {
            Id = "maintenance",
            Title = "Обслуживание системы",
            Category = "Обслуживание",
            Icon = "ArrowTrendingWrench24",
            Risk = PresetRisk.Safe,
            Keywords = "очистка мусор temp кэш ram эскизы шейдеры обслуживание диска",
            Description = "Чистит временные файлы, кэш игр, эскизы и шейдеры, сбрасывает кэш DNS и освобождает оперативную память. Ничего не отключает — только убирает мусор.",
            Steps =
            [
                new PresetStep
                {
                    Label = "Очистка временных файлов",
                    Apply = async _ => await OptimizationService.ClearTempFilesAsync(),
                },
                new PresetStep
                {
                    Label = "Очистка кэша игр (DirectX)",
                    Apply = async _ => await OptimizationService.ClearGameCacheAsync(),
                },
                new PresetStep
                {
                    Label = "Очистка кэша эскизов",
                    Apply = async _ => await OptimizationService.ClearThumbnailCacheAsync(),
                },
                new PresetStep
                {
                    Label = "Очистка кэша шейдеров",
                    Apply = async _ => await ShaderCacheService.ClearAsync(),
                },
                new PresetStep
                {
                    Label = "Очистка кэша DNS",
                    Apply = async _ => await OptimizationService.ClearDnsCacheAsync(),
                },
                new PresetStep
                {
                    Label = "Освобождение оперативной памяти",
                    Apply = async _ => await RamCleanerService.CleanAsync(),
                },
            ],
        },
        new PresetDefinition
        {
            Id = "disable-protection",
            Title = "Безопасность: выключить защиту",
            Category = "Безопасность",
            Icon = "ShieldError24",
            Risk = PresetRisk.Moderate,
            Keywords = "защита защитник defender антивирус вирусы smartscreen брандмауэр firewall uac отключить выключить центр безопасности",
            Description = "Полностью выключает защиту Windows: Защитник (политики, службы и приложение «Безопасность Windows»), SmartScreen, брандмауэр и контроль учётных записей. Нужны права администратора; в Windows 11 сначала выключите «Защиту от изменений» в Центре безопасности, иначе система отменит часть изменений.",
            Steps =
            [
                new PresetStep
                {
                    Label = "Полное отключение Защитника Windows",
                    Apply = async _ => await SecurityService.SetDefenderFullyDisabledAsync(true),
                },
                new PresetStep
                {
                    Label = "Отключение SmartScreen",
                    Apply = async _ => await SecurityService.SetSmartScreenDisabledAsync(true),
                },
                new PresetStep
                {
                    Label = "Отключение брандмауэра Windows",
                    Apply = async _ => await SecurityService.SetFirewallDisabledAsync(true),
                },
                new PresetStep
                {
                    Label = "Отключение контроля учётных записей (UAC)",
                    Apply = async _ => await SecurityService.SetUacDisabledAsync(true),
                },
            ],
        },
        new PresetDefinition
        {
            Id = "protection",
            Title = "Безопасность: включить защиту",
            Category = "Безопасность",
            Icon = "LockShield24",
            Risk = PresetRisk.Safe,
            Keywords = "безопасность defender защитник smartscreen брандмауэр firewall uac антивирус вернуть защиту",
            Description = "Возвращает защиту Windows: Защитник, SmartScreen, брандмауэр и контроль учётных записей, а также стандартные значения телеметрии. Полезно после агрессивных твиков.",
            Steps =
            [
                new PresetStep
                {
                    Label = "Включение Защитника Windows",
                    Apply = async _ => await SecurityService.SetDefenderDisabledAsync(false),
                },
                new PresetStep
                {
                    Label = "Включение SmartScreen",
                    Apply = async _ => await SecurityService.SetSmartScreenDisabledAsync(false),
                },
                new PresetStep
                {
                    Label = "Включение брандмауэра Windows",
                    Apply = async _ => await SecurityService.SetFirewallDisabledAsync(false),
                },
                new PresetStep
                {
                    Label = "Включение контроля учётных записей (UAC)",
                    Apply = async _ => await SecurityService.SetUacDisabledAsync(false),
                },
                new PresetStep
                {
                    Label = "Возврат телеметрии к значениям по умолчанию",
                    Apply = async _ => await TelemetryTweaks.RestoreAsync(),
                },
            ],
        },
        new PresetDefinition
        {
            Id = "reset-settings",
            Title = "Сбросить настройки Implexity",
            Category = "Настройки приложения",
            Icon = "ArrowCounterclockwise16",
            Risk = PresetRisk.Moderate,
            IsReset = true,
            Keywords = "сброс reset настройки приложения значения по умолчанию",
            Description = "Возвращает сохранённые настройки Implexity Tweaker к значениям по умолчанию (язык интерфейса не меняется). Твики в системе не откатываются — для этого используйте снимки.",
            Steps =
            [
                new PresetStep
                {
                    Label = "Сброс сохранённых настроек приложения",
                    Apply = s => { s.CopyFrom(new AppSettings()); return Task.CompletedTask; },
                },
            ],
        },
        // --- новые пресеты добавляются выше этой строки ---
    ];
}

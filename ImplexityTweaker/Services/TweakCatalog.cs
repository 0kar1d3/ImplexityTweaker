using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace ImplexityTweaker.Services;

/// <summary>Тип записи в глобальном поиске.</summary>
public enum TweakKind
{
    /// <summary>Раздел приложения (переход).</summary>
    Section,

    /// <summary>Пресет: открывает страницу «Пресеты» с подсветкой нужного набора.</summary>
    Preset,

    /// <summary>Конкретный твик или настройка (переход в раздел, где он включается).</summary>
    Tweak,

    /// <summary>Быстрое действие: выполняется сразу из поиска.</summary>
    Action,
}

/// <summary>Запись каталога: раздел, твик, пресет или быстрое действие.</summary>
public sealed class TweakEntry
{
    public required string Id { get; init; }

    /// <summary>Название на русском (оно же ключ перевода).</summary>
    public required string Title { get; init; }

    /// <summary>Раздел-категория (для пресетов и действий); у разделов пустая.</summary>
    public string Category { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public TweakKind Kind { get; init; } = TweakKind.Tweak;

    /// <summary>Тег раздела для перехода (совпадает с тегами MainWindow).</summary>
    public string? Route { get; init; }

    /// <summary>Id пресета — открыть страницу «Пресеты» и подсветить его.</summary>
    public string? PresetId { get; init; }

    /// <summary>Быстрое действие.</summary>
    public Func<Task>? Run { get; init; }

    /// <summary>Спросить подтверждение перед выполнением (операции, заметные сразу).</summary>
    public bool NeedsConfirm { get; init; }

    /// <summary>Показать «Готово» после успешного выполнения действия.</summary>
    public bool NotifyOnSuccess { get; init; }

    /// <summary>Текущее состояние («Включено»/«Выключено») по сохранённым настройкам приложения.</summary>
    public Func<AppSettings, string>? State { get; init; }

    // ---- Версионная доступность ----
    public int MinBuild { get; init; }
    public int MaxBuild { get; init; }
    public bool Windows11Only { get; init; }
    public string? RequiredEdition { get; init; }

    /// <summary>Слова для поиска (через пробел): синонимы, английские названия, жаргон.</summary>
    public string Keywords { get; init; } = string.Empty;
}

/// <summary>Найденная запись вместе с доступностью и состоянием.</summary>
public sealed record TweakSearchHit(TweakEntry Entry, WindowsVersionService.Availability Availability, string State, int Score);

/// <summary>
/// Единый каталог всего, что есть в твикере: разделы (они же пункты бокового меню),
/// пресеты, отдельные твики и быстрые действия. Используется глобальным поиском (Ctrl+K).
/// </summary>
public static class TweakCatalog
{
    /// <summary>
    /// Разделы приложения. Из этого списка MainWindow строит боковое меню,
    /// поэтому здесь же живут теги навигации и слова для поиска.
    /// </summary>
    public static IReadOnlyList<TweakEntry> Sections { get; } =
    [
        new TweakEntry { Id = "sec:explorer", Title = "Проводник и Рабочий стол", Kind = TweakKind.Section, Route = "explorer", Keywords = "проводник папки файлы explorer рабочий стол скрытые расширения ярлыки" },
        new TweakEntry { Id = "sec:wupdate", Title = "Windows Update", Kind = TweakKind.Section, Route = "wupdate", Keywords = "обновления windows update пауза перезагрузка центр обновления" },
        new TweakEntry { Id = "sec:systemrecovery", Title = "Система и восстановление", Kind = TweakKind.Section, Route = "systemrecovery", Keywords = "снимок бэкап backup откат восстановление точки защиты диагностика" },
        new TweakEntry { Id = "sec:security", Title = "Безопасность", Kind = TweakKind.Section, Route = "security", Keywords = "безопасность defender защитник smartscreen uac брандмауэр firewall вирусы" },
        new TweakEntry { Id = "sec:uwp", Title = "Удаление UWP приложений", Kind = TweakKind.Section, Route = "uwp", Keywords = "uwp приложения удаление магазин store bloatware xbox" },
        new TweakEntry { Id = "sec:personalization", Title = "Персонализация", Kind = TweakKind.Section, Route = "personalization", Keywords = "персонализация тема обои панель задач пуск акцент прозрачность виджеты" },
        new TweakEntry { Id = "sec:contextmenu", Title = "Контекстное меню", Kind = TweakKind.Section, Route = "contextmenu", Keywords = "контекстное меню правый клик задержка windows terminal копировать путь" },
        new TweakEntry { Id = "sec:shutdown", Title = "Таймер выключения", Kind = TweakKind.Section, Route = "shutdown", Keywords = "выключение таймер сон sleep shutdown питание" },
        new TweakEntry { Id = "sec:wincomponents", Title = "Компоненты Windows", Kind = TweakKind.Section, Route = "wincomponents", Keywords = "компоненты windows features функции hyper-v wsl .net" },
        new TweakEntry { Id = "sec:activation", Title = "Активация Windows", Kind = TweakKind.Section, Route = "activation", Keywords = "активация лицензия ключ hwid kms" },
        new TweakEntry { Id = "sec:apps", Title = "Установка приложений", Kind = TweakKind.Section, Route = "apps", Keywords = "приложения установка programs winget браузер" },
        new TweakEntry { Id = "sec:optimization", Title = "Игровая оптимизация", Kind = TweakKind.Section, Route = "optimization", Keywords = "оптимизация игры fps mpo game dvr fso hags приоритет питание" },
        new TweakEntry { Id = "sec:overlay", Title = "ImplexityOverlay", Kind = TweakKind.Section, Route = "overlay", Keywords = "overlay оверлей fps cpu gpu ram мониторинг в игре время кадра frametime температура" },
        new TweakEntry { Id = "sec:network", Title = "Сетевые настройки", Kind = TweakKind.Section, Route = "network", Keywords = "сеть dns ping tcp nagle нагл ip wifi задержка адаптер" },
        new TweakEntry { Id = "sec:firstsetup", Title = "Быстрая настройка Windows", Kind = TweakKind.Section, Route = "firstsetup", Keywords = "быстрая настройка сценарии всё сразу новичок старт" },
        new TweakEntry { Id = "sec:presets", Title = "Пресеты", Kind = TweakKind.Section, Route = "presets", Keywords = "пресеты готовые наборы профиль один клик сценарий" },
        new TweakEntry { Id = "sec:processes", Title = "Управление процессами", Kind = TweakKind.Section, Route = "processes", Keywords = "процессы диспетчер задач приоритет завершить" },
        new TweakEntry { Id = "sec:autostart", Title = "Автозагрузка", Kind = TweakKind.Section, Route = "autostart", Keywords = "автозагрузка startup службы планировщик" },
        new TweakEntry { Id = "sec:pcinfo", Title = "Информация о ПК", Kind = TweakKind.Section, Route = "pcinfo", Keywords = "информация о пк железо cpu gpu ram материнская плата сведения" },
        new TweakEntry { Id = "sec:benchmark", Title = "Бенчмарк", Kind = TweakKind.Section, Route = "benchmark", Keywords = "бенчмарк тест производительность скорость cpu диск индекс замер" },
        new TweakEntry { Id = "sec:funsettings", Title = "Фановые настройки", Kind = TweakKind.Section, Route = "funsettings", Keywords = "фановые настройки эффекты приколы пасхалки" },
        new TweakEntry { Id = "sec:settings", Title = "Настройки / О программе", Kind = TweakKind.Section, Route = "settings", Keywords = "настройки о программе язык тема аккаунт обновление конфиги" },
        // --- остальные записи добавляются выше этого комментария ---
    ];

    /// <summary>Весь каталог: разделы, пресеты, твики и действия.</summary>
    public static IReadOnlyList<TweakEntry> All { get; } = BuildAll();

    /// <summary>Поиск по каталогу. Пустой запрос — стартовый экран (разделы и пресеты).</summary>
    public static IReadOnlyList<TweakSearchHit> Search(string? query, int limit = 40)
    {
        var tokens = (query ?? string.Empty)
            .ToLowerInvariant()
            .Split([' ', ',', ';', ':', '.', '(', ')', '«', '»', '-'], StringSplitOptions.RemoveEmptyEntries);

        var settings = AppSettings.Load();
        var hits = new List<TweakSearchHit>();

        foreach (var entry in All)
        {
            var score = Score(entry, tokens);

            if (tokens.Length == 0)
            {
                // Стартовый экран: только разделы и пресеты, чтобы список был обозримым.
                if (entry.Kind is not (TweakKind.Section or TweakKind.Preset))
                    continue;
            }
            else if (score <= 0)
            {
                continue;
            }

            hits.Add(new TweakSearchHit(entry, Check(entry), entry.State?.Invoke(settings) ?? string.Empty, score));
        }

        if (tokens.Length == 0)
            return hits.Take(limit).ToList();

        return hits
            .OrderByDescending(h => h.Availability.IsAvailable)
            .ThenByDescending(h => h.Score)
            .ThenBy(h => h.Entry.Title, StringComparer.CurrentCulture)
            .Take(limit)
            .ToList();
    }

    /// <summary>Доступность твика на этой сборке и редакции Windows.</summary>
    public static WindowsVersionService.Availability Check(TweakEntry entry)
        => WindowsVersionService.Check(entry.MinBuild, entry.MaxBuild, entry.Windows11Only, entry.RequiredEdition);

    /// <summary>Подпись типа записи на текущем языке.</summary>
    public static string KindLabel(TweakKind kind) => kind switch
    {
        TweakKind.Section => LocalizationService.T("Раздел"),
        TweakKind.Preset => LocalizationService.T("Пресет"),
        TweakKind.Action => LocalizationService.T("Действие"),
        _ => LocalizationService.T("Твик"),
    };

    /// <summary>«Включено»/«Выключено» по сохранённому флагу (null — состояние неизвестно).</summary>
    public static string Flag(bool? value) => value switch
    {
        true => LocalizationService.T("Включено"),
        false => LocalizationService.T("Выключено"),
        _ => string.Empty,
    };

    /// <summary>Вес совпадения: все слова запроса должны найтись, иначе запись не подходит.</summary>
    private static int Score(TweakEntry entry, string[] tokens)
    {
        if (tokens.Length == 0)
            return 0;

        // Ищем и по переводам: в каталоге лежит русский текст, а вводят слова
        // на языке интерфейса, поэтому к каждой строке добавляем её перевод.
        var title = SearchText(entry.Title);
        var keywords = SearchText(entry.Keywords);
        var category = SearchText(entry.Category);
        var description = SearchText(entry.Description);

        var total = 0;

        foreach (var token in tokens)
        {
            int score;
            if (title.StartsWith(token, StringComparison.Ordinal))
                score = 120;
            else if (title.Contains(token, StringComparison.Ordinal))
                score = 90;
            else if (keywords.Contains(token, StringComparison.Ordinal))
                score = 60;
            else if (category.Contains(token, StringComparison.Ordinal))
                score = 40;
            else if (description.Contains(token, StringComparison.Ordinal))
                score = 20;
            else
                return 0;

            total += score;
        }

        return total;
    }

    /// <summary>Строка для поиска: исходный текст и его перевод на текущий язык, в нижнем регистре.</summary>
    private static string SearchText(string text)
        => string.IsNullOrEmpty(text)
            ? string.Empty
            : (text + " " + LocalizationService.T(text)).ToLowerInvariant();

    private static IReadOnlyList<TweakEntry> BuildAll()
    {
        var list = new List<TweakEntry>();
        list.AddRange(Sections);

        foreach (var preset in PresetCatalog.All)
        {
            list.Add(new TweakEntry
            {
                Id = "preset:" + preset.Id,
                Title = preset.Title,
                Category = preset.Category,
                Description = preset.Description,
                Kind = TweakKind.Preset,
                PresetId = preset.Id,
                Keywords = preset.Keywords,
                MinBuild = preset.MinBuild,
                MaxBuild = preset.MaxBuild,
                Windows11Only = preset.Windows11Only,
                RequiredEdition = preset.RequiredEdition,
            });
        }

        list.AddRange(BuildTweaks());
        list.AddRange(BuildActions());
        return list;
    }

    /// <summary>Отдельные твики: ведут в раздел, где включаются (с текущим состоянием).</summary>
    private static IReadOnlyList<TweakEntry> BuildTweaks() =>
    [
        // --- твики ---
        new TweakEntry
        {
            Id = "tweak:show-hidden", Title = "Показ скрытых файлов", Category = "Проводник",
            Kind = TweakKind.Tweak, Route = "explorer",
            Keywords = "скрытые файлы hidden показать проводник",
            State = s => Flag(s.ShowHiddenFiles),
        },
        new TweakEntry
        {
            Id = "tweak:show-ext", Title = "Показ расширений файлов", Category = "Проводник",
            Kind = TweakKind.Tweak, Route = "explorer",
            Keywords = "расширения файлов hidefileext типы файлов",
            State = s => Flag(s.ShowFileExtensions),
        },
        new TweakEntry
        {
            Id = "tweak:show-system", Title = "Показ системных файлов", Category = "Проводник",
            Kind = TweakKind.Tweak, Route = "explorer",
            Keywords = "системные файлы скрытые superhidden защищённые",
            State = s => Flag(s.ShowSystemFiles),
        },
        new TweakEntry
        {
            Id = "tweak:this-pc", Title = "Открывать «Этот компьютер» в проводнике", Category = "Проводник",
            Kind = TweakKind.Tweak, Route = "explorer",
            Keywords = "этот компьютер этот пк проводник стартовая папка",
            State = s => Flag(s.LaunchToThisPc),
        },
        new TweakEntry
        {
            Id = "tweak:old-context", Title = "Классическое контекстное меню", Category = "Контекстное меню",
            Kind = TweakKind.Tweak, Route = "contextmenu", Windows11Only = true,
            Keywords = "контекстное меню classic правый клик windows 11 show more",
            State = s => Flag(s.OldContextMenu),
        },
        new TweakEntry
        {
            Id = "tweak:context-delay", Title = "Без задержки контекстного меню", Category = "Контекстное меню",
            Kind = TweakKind.Tweak, Route = "contextmenu",
            Keywords = "задержка контекстное меню menushowdelay быстрее",
            State = s => Flag(s.RemoveContextDelay),
        },
        new TweakEntry
        {
            Id = "tweak:terminal-context", Title = "Пункт «Открыть в Windows Terminal»", Category = "Контекстное меню",
            Kind = TweakKind.Tweak, Route = "contextmenu",
            Keywords = "windows terminal контекстное меню терминал открыть здесь",
            State = s => Flag(s.OpenInTerminal),
        },
        new TweakEntry
        {
            Id = "tweak:copy-path", Title = "Пункт «Копировать путь»", Category = "Контекстное меню",
            Kind = TweakKind.Tweak, Route = "contextmenu",
            Keywords = "копировать путь copy as path контекстное меню",
            State = s => Flag(s.CopyPath),
        },
        new TweakEntry
        {
            Id = "tweak:animations", Title = "Отключить анимации окон", Category = "Интерфейс",
            Kind = TweakKind.Tweak, Route = "optimization",
            Keywords = "анимации окна эффекты быстродействие интерфейс",
            State = s => Flag(s.DisableWindowsAnimation),
        },
        new TweakEntry
        {
            Id = "tweak:visual-effects", Title = "Отключить эффекты оформления", Category = "Интерфейс",
            Kind = TweakKind.Tweak, Route = "optimization",
            Keywords = "эффекты оформления визуальные анимации производительность",
            State = s => Flag(s.DisableVisualEffects),
        },
        new TweakEntry
        {
            Id = "tweak:transparency", Title = "Прозрачность интерфейса", Category = "Интерфейс",
            Kind = TweakKind.Tweak, Route = "personalization",
            Keywords = "прозрачность transparency акрил mica эффекты",
            State = s => Flag(s.Transparency),
        },
        new TweakEntry
        {
            Id = "tweak:lock-blur", Title = "Размытие экрана блокировки", Category = "Интерфейс",
            Kind = TweakKind.Tweak, Route = "personalization",
            Keywords = "размытие экран блокировки акрил logon blur",
            State = s => Flag(s.DisableLockScreenBlur),
        },
        new TweakEntry
        {
            Id = "tweak:end-task", Title = "Кнопка «Завершить задачу» на панели задач", Category = "Интерфейс",
            Kind = TweakKind.Tweak, Route = "personalization",
            MinBuild = WindowsVersionService.Windows11_22H2Build, Windows11Only = true,
            Keywords = "завершить задачу панель задач end task 22h2 диспетчер",
        },
        new TweakEntry
        {
            Id = "tweak:reduce-ads", Title = "Меньше рекламы и советов в системе", Category = "Приватность",
            Kind = TweakKind.Tweak, Route = "personalization",
            Keywords = "реклама советы рекомендации redirect reduce ads",
            State = s => Flag(s.ReduceAds),
        },
        new TweakEntry
        {
            Id = "tweak:fso", Title = "Отключить оптимизации во весь экран (FSO)", Category = "Игровая оптимизация",
            Kind = TweakKind.Tweak, Route = "optimization",
            Keywords = "fso fullscreen optimizations игры оптимизация во весь экран",
            State = s => Flag(s.DisableFso),
        },
        new TweakEntry
        {
            Id = "tweak:mpo", Title = "Отключить MPO (Multi-Plane Overlay)", Category = "Игровая оптимизация",
            Kind = TweakKind.Tweak, Route = "optimization",
            MinBuild = 19041,
            Keywords = "mpo multi-plane overlay оверлей мерцание видеокарта",
            State = s => Flag(s.DisableMpo),
        },
        new TweakEntry
        {
            Id = "tweak:gamedvr", Title = "Отключить запись Game DVR", Category = "Игровая оптимизация",
            Kind = TweakKind.Tweak, Route = "optimization",
            Keywords = "game dvr запись экран xbox бар фоновые записи",
            State = s => Flag(s.DisableGameDvr),
        },
        new TweakEntry
        {
            Id = "tweak:hags", Title = "Аппаратное планирование GPU (HAGS)", Category = "Игровая оптимизация",
            Kind = TweakKind.Tweak, Route = "optimization",
            MinBuild = 18362,
            Keywords = "hags gpu scheduling планирование видеокарты hwschmode",
            State = _ => Flag(GamingService.GetHagsEnabled()),
        },
        new TweakEntry
        {
            Id = "tweak:game-mode", Title = "Игровой режим Windows", Category = "Игровая оптимизация",
            Kind = TweakKind.Tweak, Route = "optimization",
            Keywords = "игровой режим game mode приоритет игры",
            State = _ => Flag(GamingService.GetGameModeEnabled()),
        },
        new TweakEntry
        {
            Id = "tweak:core-parking", Title = "Отключить парковку ядер CPU", Category = "Игровая оптимизация",
            Kind = TweakKind.Tweak, Route = "optimization",
            Keywords = "парковка ядер core parking cpu микрофризы",
            State = s => Flag(s.QuickCoreParking),
        },
        new TweakEntry
        {
            Id = "tweak:ultimate-plan", Title = "План «Максимальная производительность»", Category = "Игровая оптимизация",
            Kind = TweakKind.Tweak, Route = "optimization",
            Keywords = "план питания электропитание максимальная производительность ultimate",
            State = s => Flag(s.UltimatePerformance),
        },
        new TweakEntry
        {
            Id = "tweak:gpu-priority", Title = "Приоритет GPU для игр", Category = "Игровая оптимизация",
            Kind = TweakKind.Tweak, Route = "optimization",
            Keywords = "приоритет gpu gpu priority mmcss игры",
            State = s => Flag(s.GpuPriority),
        },
        new TweakEntry
        {
            Id = "tweak:nagle", Title = "Отключить алгоритм Nagle (TcpNoDelay)", Category = "Сети",
            Kind = TweakKind.Tweak, Route = "network",
            Keywords = "nagle tcp tcpnodelay пинг задержка сети интерфейсы",
            State = s => Flag(s.DisableNagle),
        },
        new TweakEntry
        {
            Id = "tweak:autotuning", Title = "Отключить автонастройку TCP", Category = "Сети",
            Kind = TweakKind.Tweak, Route = "network",
            Keywords = "автонастройка tcp autotuning receive window масштабирование окна",
            State = s => Flag(s.DisableAutoTuning),
        },
        new TweakEntry
        {
            Id = "tweak:tcpack", Title = "TcpAckFrequency = 1 (для игр)", Category = "Сети",
            Kind = TweakKind.Tweak, Route = "network",
            Keywords = "tcpackfrequency ack tcp задержка пинг игры",
            State = s => Flag(s.TcpAckFrequency),
        },
        new TweakEntry
        {
            Id = "tweak:sysresponsiveness", Title = "SystemResponsiveness = 10", Category = "Сети",
            Kind = TweakKind.Tweak, Route = "optimization",
            Keywords = "systemresponsiveness mmcss мультимедиа приоритет отклик",
            State = s => Flag(s.DisableSystemResponsiveness),
        },
        new TweakEntry
        {
            Id = "tweak:telemetry-policy", Title = "AllowTelemetry = 0 (политика)", Category = "Приватность",
            Kind = TweakKind.Tweak, Route = "systemrecovery",
            Keywords = "телеметрия allowtelemetry политика приватность данные",
            State = s => Flag(s.TelemetryPolicy),
        },
        new TweakEntry
        {
            Id = "tweak:diagtrack", Title = "Отключить службу DiagTrack", Category = "Приватность",
            Kind = TweakKind.Tweak, Route = "systemrecovery",
            Keywords = "diagtrack telemetry телеметрия служба опыт пользователей",
            State = s => Flag(s.TelemetryDiagTrack),
        },
        new TweakEntry
        {
            Id = "tweak:defender", Title = "Отключить Защитник Windows", Category = "Безопасность",
            Kind = TweakKind.Tweak, Route = "security",
            Keywords = "defender защитник антивирус отключить defender политика",
            State = _ => Flag(SecurityService.GetDefenderDisabled()),
        },
        new TweakEntry
        {
            Id = "tweak:smartscreen", Title = "Отключить SmartScreen", Category = "Безопасность",
            Kind = TweakKind.Tweak, Route = "security",
            Keywords = "smartscreen фильтр смартскрин проверка приложений",
            State = _ => Flag(SecurityService.GetSmartScreenDisabled()),
        },
        new TweakEntry
        {
            Id = "tweak:firewall", Title = "Отключить брандмауэр Windows", Category = "Безопасность",
            Kind = TweakKind.Tweak, Route = "security",
            Keywords = "брандмауэр firewall защита сети файрвол",
            State = _ => Flag(SecurityService.GetFirewallDisabled()),
        },
        new TweakEntry
        {
            Id = "tweak:uac", Title = "Отключить контроль учётных записей (UAC)", Category = "Безопасность",
            Kind = TweakKind.Tweak, Route = "security",
            Keywords = "uac контроль учётных записей запросы прав администратора",
            State = _ => Flag(SecurityService.GetUacDisabled()),
        },
        new TweakEntry
        {
            Id = "tweak:msi-mode", Title = "Режим прерываний устройств и приоритет", Category = "Игровая оптимизация",
            Kind = TweakKind.Tweak, Route = "optimization",
            Keywords = "msi mode interrupt affinity прерывания видеокарта сетевой адаптер usb контроллер ядро латентность инпут лаг",
        },
        new TweakEntry
        {
            Id = "tweak:raw-mouse", Title = "Точный ввод мыши 1:1 без ускорения", Category = "Игровая оптимизация",
            Kind = TweakKind.Tweak, Route = "optimization",
            Keywords = "мышь mouse raw input markc акселерация ускорение smoothmousexcurve mousedataqueuesize",
            State = s => Flag(s.RawMouseInput),
        },
        new TweakEntry
        {
            Id = "tweak:timer-resolution", Title = "Точный системный таймер (0.5 мс)", Category = "Игровая оптимизация",
            Kind = TweakKind.Tweak, Route = "optimization",
            Keywords = "timer resolution 0.5 ms globaltimerresolutionrequests таймер задержка отклик windows 11",
            State = s => Flag(s.HighPrecisionTimer),
        },
        new TweakEntry
        {
            Id = "tweak:dynamic-tick", Title = "Отключить Dynamic Tick и принудительный HPET", Category = "Игровая оптимизация",
            Kind = TweakKind.Tweak, Route = "optimization",
            Keywords = "dynamic tick disabledynamictick hpet useplatformclock таймер инпут лаг",
            State = s => Flag(s.DisableDynamicTick),
        },
        new TweakEntry
        {
            Id = "tweak:win32-priority", Title = "Приоритет активного окна (Win32PrioritySeparation)", Category = "Игровая оптимизация",
            Kind = TweakKind.Tweak, Route = "optimization",
            Keywords = "win32priorityseparation кванты приоритет окно игра отзывчивость",
        },
        new TweakEntry
        {
            Id = "tweak:auto-ram", Title = "Автоочистка ОЗУ (по порогу и при запуске игр)", Category = "Игровая оптимизация",
            Kind = TweakKind.Tweak, Route = "optimization",
            Keywords = "автоочистка озу ram трей фоновый режим mem reduct игры",
            State = _ => Flag(ProcessTweaksStore.Load().AutoRamThresholdEnabled || ProcessTweaksStore.Load().AutoRamOnGameLaunchEnabled),
        },
        new TweakEntry
        {
            Id = "tweak:hibernation", Title = "Управление гибернацией и файлом подкачки", Category = "Система",
            Kind = TweakKind.Tweak, Route = "systemrecovery",
            Keywords = "гибернация hiberfil pagefile файл подкачки ssd место очистить",
        },
        new TweakEntry
        {
            Id = "tweak:winsxs", Title = "Очистка кэша обновлений Windows", Category = "Система",
            Kind = TweakKind.Tweak, Route = "systemrecovery",
            Keywords = "winsxs softwaredistribution очистка обновлений dism startcomponentcleanup",
        },
        new TweakEntry
        {
            Id = "tweak:battery-report", Title = "Отчёт о батарее и износе аккумулятора", Category = "Система",
            Kind = TweakKind.Tweak, Route = "systemrecovery",
            Keywords = "батарея аккумулятор ноутбук износ ёмкость batteryreport powercfg",
        },
        new TweakEntry
        {
            Id = "tweak:msi-mode", Title = "MSI Mode (режим прерываний PCI)", Category = "Производительность",
            Kind = TweakKind.Tweak, Route = "optimization",
            Keywords = "msi mode прерывания gpu видеокарта сеть usb pci инпут лаг input lag",
        },
        new TweakEntry
        {
            Id = "tweak:interrupt-affinity", Title = "Interrupt Affinity (привязка прерываний к ядрам)", Category = "Производительность",
            Kind = TweakKind.Tweak, Route = "optimization",
            Keywords = "interrupt affinity привязка прерываний ядра cpu msi приоритет",
        },
        new TweakEntry
        {
            Id = "tweak:shader-cache-page", Title = "Очистка кэша шейдеров", Category = "Система",
            Kind = TweakKind.Tweak, Route = "systemrecovery",
            Keywords = "шейдеры shader cache directx nvidia amd очистка кэш",
        },
        new TweakEntry
        {
            Id = "tweak:fake-bsod", Title = "Фейковый BSOD (Синий экран)", Category = "Оформление",
            Kind = TweakKind.Tweak, Route = "funsettings",
            Keywords = "bsod синий экран фейк прикол пранк фан fun",
        },
        new TweakEntry
        {
            Id = "tweak:rotate-screen-180", Title = "Поворот экрана на 180°", Category = "Оформление",
            Kind = TweakKind.Tweak, Route = "funsettings",
            Keywords = "поворот экрана 180 перевернуть дисплей монитор фан пранк",
        },
        new TweakEntry
        {
            Id = "tweak:logon-notice", Title = "Текст при входе в Windows", Category = "Оформление",
            Kind = TweakKind.Tweak, Route = "funsettings",
            Keywords = "текст вход блокировка экран legalnoticecaption legalnoticetext сообщение",
        },
        // --- новые твики добавляются выше этой строки ---
    ];

    /// <summary>Быстрые действия: выполняются прямо из поиска.</summary>
    private static IReadOnlyList<TweakEntry> BuildActions() =>
    [
        new TweakEntry
        {
            Id = "act:restart-explorer", Title = "Перезапустить проводник", Category = "Быстрые действия",
            Kind = TweakKind.Action, NeedsConfirm = true, NotifyOnSuccess = true,
            Description = "Закрывает и снова открывает explorer.exe — применяет настройки Explorer без перезагрузки.",
            Keywords = "перезапуск проводник explorer restart обновить",
            Run = static async () =>
            {
                await Task.Run(() =>
                {
                    foreach (var process in Process.GetProcessesByName("explorer"))
                    {
                        try
                        {
                            process.Kill();
                            process.WaitForExit();
                        }
                        catch
                        {
                            // Процесс уже завершился — не мешаем остальным.
                        }
                    }

                    Process.Start("explorer.exe");
                });
            },
        },
        new TweakEntry
        {
            Id = "act:clear-dns", Title = "Очистить кэш DNS", Category = "Быстрые действия",
            Kind = TweakKind.Action, NotifyOnSuccess = true,
            Description = "ipconfig /flushdns: сбрасывает кэш DNS, помогает при «сайт не открывается».",
            Keywords = "dns кэш очистить flushdns сеть",
            Run = static async () => await OptimizationService.ClearDnsCacheAsync(),
        },
        new TweakEntry
        {
            Id = "act:clear-ram", Title = "Освободить оперативную память", Category = "Быстрые действия",
            Kind = TweakKind.Action, NotifyOnSuccess = true,
            Description = "Сбрасывает рабочие наборы процессов и очищает standby-память.",
            Keywords = "озу ram память освободить очистить оперативная",
            Run = static async () => await RamCleanerService.CleanAsync(),
        },
        new TweakEntry
        {
            Id = "act:restore-point", Title = "Создать точку восстановления", Category = "Быстрые действия",
            Kind = TweakKind.Action, NeedsConfirm = true, NotifyOnSuccess = true,
            Description = "Точка восстановления системы — страховка перед изменениями твиков.",
            Keywords = "точка восстановления снимок системы restore point страховка",
            Run = static async () =>
            {
                if (!await DiagnosticsService.CreateRestorePointAsync("Implexity Tweaker"))
                    throw new InvalidOperationException(
                        LocalizationService.T("Не удалось создать точку восстановления. Проверьте, включена ли защита системы."));
            },
        },
        new TweakEntry
        {
            Id = "act:tweak-backup", Title = "Создать снимок твиков Implexity", Category = "Быстрые действия",
            Kind = TweakKind.Action, NotifyOnSuccess = true,
            Description = "Сохраняет текущее состояние твиков, чтобы потом откатить изменения.",
            Keywords = "снимок backup бэкап сохранить состояние твиков откат",
            Run = static async () => await TweakBackupService.CreateAsync(),
        },
        new TweakEntry
        {
            Id = "act:clear-thumbnails", Title = "Очистить кэш эскизов", Category = "Быстрые действия",
            Kind = TweakKind.Action, NotifyOnSuccess = true,
            Description = "Чистит кэш миниатюр проводника — помогает при «побитых» превью.",
            Keywords = "кэш эскизы превью thumbnails миниатюры очистить",
            Run = static async () => await OptimizationService.ClearThumbnailCacheAsync(),
        },
        new TweakEntry
        {
            Id = "act:clear-shaders", Title = "Очистить кэш шейдеров", Category = "Быстрые действия",
            Kind = TweakKind.Action, NeedsConfirm = true, NotifyOnSuccess = true,
            Description = "Удаляет кэш шейдеров (DirectX/NVIDIA/AMD) — лечит артефакты и «тормоза» в играх. Первый запуск игры будет дольше.",
            Keywords = "шейдеры shader кэш directx nvidia amd очистить игры",
            Run = static async () => await ShaderCacheService.ClearAsync(),
        },
        // --- новые действия добавляются выше этой строки ---
    ];
}

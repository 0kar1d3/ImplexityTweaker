using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ImplexityTweaker.Services;
using ImplexityTweaker.Windows;
using Wpf.Ui.Common;

namespace ImplexityTweaker.Pages;

/// <summary>
/// Страница «Пресеты»: готовые наборы настроек (игровой ПК, низкая задержка сети,
/// приватность, обслуживание…). Показывает, что именно изменится, доступен ли пресет
/// на этой сборке Windows, применяет шаги через окно прогресса и при желании делает
/// снимок настроек, чтобы изменения можно было откатить.
/// </summary>
public partial class PresetsPage : Page
{
    private readonly string? _highlightId;
    private List<PresetItem> _items = [];

    public PresetsPage()
        : this(null)
    {
    }

    /// <param name="highlightPresetId">
    /// Id пресета, который нужно подсветить (переход из глобального поиска Ctrl+K).
    /// </param>
    public PresetsPage(string? highlightPresetId)
    {
        InitializeComponent();

        _highlightId = highlightPresetId;

        SystemInfoText.Text = LocalizationService.TF(
            "{0} · пресеты подбираются под вашу сборку, недоступные шаги пропускаются.",
            WindowsVersionService.DescribeSystem());
        FooterHintText.Text = LocalizationService.T(
            "Снимок настроек создаётся до изменений: вернуть всё назад можно в разделе «Система и восстановление».");

        SnapshotToggle.IsChecked = true;
        AvailableOnlyToggle.IsChecked = false;

        Rebuild();
    }

    /// <summary>Пересобирает список пресетов по фильтру и с учётом доступности на этой системе.</summary>
    private void Rebuild()
    {
        var query = FilterBox.Text?.Trim() ?? string.Empty;
        var availableOnly = AvailableOnlyToggle.IsChecked == true;

        _items = [];

        foreach (var preset in PresetCatalog.All)
        {
            var availability = PresetCatalog.CheckAvailability(preset);

            if (availableOnly && !availability.IsAvailable)
                continue;

            if (query.Length > 0 && !Matches(preset, query))
                continue;

            _items.Add(new PresetItem(
                preset,
                availability,
                string.Equals(preset.Id, _highlightId, StringComparison.OrdinalIgnoreCase)));
        }

        PresetList.ItemsSource = _items;
        EmptyText.Visibility = _items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        CountText.Text = LocalizationService.TF(
            "Показано пресетов: {0} из {1}.",
            _items.Count,
            PresetCatalog.All.Count);
    }

    /// <summary>Все слова фильтра должны найтись в названии, описании или ключевых словах.</summary>
    private static bool Matches(PresetDefinition preset, string query)
    {
        var haystack = string.Join(' ',
            preset.Title,
            LocalizationService.T(preset.Title),
            preset.Category,
            LocalizationService.T(preset.Category),
            preset.Description,
            LocalizationService.T(preset.Description),
            preset.Keywords).ToLowerInvariant();

        return query
            .ToLowerInvariant()
            .Split([' ', ',', ';'], StringSplitOptions.RemoveEmptyEntries)
            .All(token => haystack.Contains(token, StringComparison.Ordinal));
    }

    private void FilterBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) => Rebuild();

    private void Filter_Toggled(object sender, RoutedEventArgs e) => Rebuild();

    private void ToggleSteps_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is PresetItem item)
            item.ToggleSteps();
    }

    private async void ApplyPreset_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is PresetItem item)
            await ApplyPresetAsync(item);
    }

    /// <summary>Применяет пресет: подтверждение → снимок → шаги в окне прогресса.</summary>
    private async Task ApplyPresetAsync(PresetItem item)
    {
        var preset = item.Preset;
        var availability = PresetCatalog.CheckAvailability(preset);

        if (!availability.IsAvailable)
        {
            await Dialogs.ShowInfoAsync(
                LocalizationService.T("Пресет недоступен"),
                availability.Reason);
            return;
        }

        var steps = PresetCatalog.StepsFor(preset);

        if (steps.Count == 0)
        {
            await Dialogs.ShowInfoAsync(
                LocalizationService.T("Нет доступных шагов"),
                LocalizationService.TF(
                    "Для пресета «{0}» на этой системе нет доступных шагов.",
                    LocalizationService.T(preset.Title)));
            return;
        }

        var snapshot = SnapshotToggle.IsChecked == true;
        var question = LocalizationService.TF(
                "Применить пресет «{0}»? Шагов к выполнению: {1}.",
                LocalizationService.T(preset.Title),
                steps.Count)
            + Environment.NewLine + Environment.NewLine
            + LocalizationService.T(preset.Description)
            + Environment.NewLine + Environment.NewLine
            + (snapshot
                ? LocalizationService.T("Перед изменениями будет создан снимок настроек: откатить всё можно в разделе «Система и восстановление».")
                : LocalizationService.T("Снимок настроек создаваться не будет — откатить изменения будет сложнее."));

        if (!await Dialogs.ShowConfirmAsync(LocalizationService.T("Применение пресета"), question))
            return;

        if (snapshot)
        {
            try
            {
                await TweakBackupService.CreateAsync();
            }
            catch
            {
                // Снимок — страховка: если его не удалось сделать, пресет всё равно применяем.
            }
        }

        var settings = AppSettings.Load();
        var plan = steps
            .Select(step => (
                Label: LocalizationService.T(step.Label),
                Step: (Func<Task>)(() => step.Apply(settings))))
            .ToList();

        var (ok, error) = await ProgressWindow.RunAsync(
            Window.GetWindow(this),
            LocalizationService.TF("Пресет: {0}", LocalizationService.T(preset.Title)),
            plan);

        // Сохраняем флаги всегда: они отражают шаги, которые мы реально пытались выполнить.
        settings.Save();

        if (ok)
        {
            await Dialogs.ShowInfoAsync(
                LocalizationService.T("Пресет применён"),
                LocalizationService.TF(
                    "Пресет «{0}» применён. Выполнено шагов: {1}.",
                    LocalizationService.T(preset.Title),
                    steps.Count));
        }
        else if (!string.IsNullOrWhiteSpace(error))
        {
            await Dialogs.ShowErrorAsync(LocalizationService.T("Ошибка применения"), error!);
        }
    }
}

/// <summary>Элемент списка пресетов: тексты, бейдж риска, доступность и раскрываемый список шагов.</summary>
public sealed class PresetItem : INotifyPropertyChanged
{
    private bool _stepsVisible;

    public PresetItem(PresetDefinition preset, WindowsVersionService.Availability availability, bool highlighted)
    {
        Preset = preset;
        Availability = availability;
        IsHighlighted = highlighted;

        var steps = PresetCatalog.StepsFor(preset);
        StepCount = steps.Count;
        Steps = steps.Select(step => LocalizationService.T(step.Label)).ToList();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public PresetDefinition Preset { get; }

    public WindowsVersionService.Availability Availability { get; }

    public bool IsHighlighted { get; }

    public int StepCount { get; }

    public IReadOnlyList<string> Steps { get; }

    public SymbolRegular IconSymbol => IconHelper.Parse(Preset.Icon);

    public string Title => LocalizationService.T(Preset.Title);

    public string Category => LocalizationService.T(Preset.Category);

    public string Description => LocalizationService.T(Preset.Description);

    public string RiskText => PresetCatalog.RiskLabel(Preset.Risk);

    public Brush RiskBackground => RiskBrush(Preset.Risk, true);

    public Brush RiskForeground => RiskBrush(Preset.Risk, false);

    public bool CanApply => Availability.IsAvailable && StepCount > 0;

    public Visibility UnavailableVisibility => Availability.IsAvailable ? Visibility.Collapsed : Visibility.Visible;

    public string AvailabilityText => Availability.IsAvailable ? string.Empty : Availability.Reason;

    public Visibility HighlightVisibility => IsHighlighted ? Visibility.Visible : Visibility.Collapsed;

    public string HighlightText => IsHighlighted
        ? LocalizationService.T("Пресет открыт из результатов поиска (Ctrl+K).")
        : string.Empty;

    public Visibility StepsVisibility => _stepsVisible ? Visibility.Visible : Visibility.Collapsed;

    public string StepsButtonText => _stepsVisible
        ? LocalizationService.T("Скрыть список шагов")
        : LocalizationService.TF("Что изменится ({0})", StepCount);

    /// <summary>Разворачивает или сворачивает список шагов пресета.</summary>
    public void ToggleSteps()
    {
        _stepsVisible = !_stepsVisible;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StepsVisibility)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StepsButtonText)));
    }

    private static readonly Brush SafeBackground = Frozen("#2E63D6A0");
    private static readonly Brush SafeForeground = Frozen("#7BDCB0");
    private static readonly Brush ModerateBackground = Frozen("#2EE3A94A");
    private static readonly Brush ModerateForeground = Frozen("#E9B463");
    private static readonly Brush AggressiveBackground = Frozen("#2EE86A6A");
    private static readonly Brush AggressiveForeground = Frozen("#F08A8A");

    private static Brush RiskBrush(PresetRisk risk, bool background) => risk switch
    {
        PresetRisk.Aggressive => background ? AggressiveBackground : AggressiveForeground,
        PresetRisk.Moderate => background ? ModerateBackground : ModerateForeground,
        _ => background ? SafeBackground : SafeForeground,
    };

    private static Brush Frozen(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)!);
        brush.Freeze();
        return brush;
    }
}

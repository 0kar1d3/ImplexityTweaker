using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ImplexityTweaker.Services;
using Wpf.Ui.Common;

namespace ImplexityTweaker.Windows;

/// <summary>
/// Окно глобального поиска (Ctrl+K): один список из разделов, твиков, пресетов и быстрых
/// действий. Enter открывает раздел/пресет или выполняет действие, стрелки — навигация,
/// Esc — закрытие.
/// </summary>
public partial class SearchWindow : Window
{
    private List<SearchRow> _rows = [];
    private int _selected = -1;

    /// <summary>Сколько результатов было в прошлый раз — чтобы не мигать счётчиком на каждом символе.</summary>
    private int _lastResultCount = -1;

    public SearchWindow()
    {
        InitializeComponent();

        HintText.Text = LocalizationService.T(
            "Enter — открыть или выполнить, ↑↓ — выбрать, Esc — закрыть. Быстрые действия выполняются прямо из поиска.");
        CountText.Text = string.Empty;

        // Мягкое появление окна — как у остальных окон твикера.
        UiAnimation.PrepareWindowOpen(this);

        Loaded += (_, _) =>
        {
            UiAnimation.AnimateWindowOpen(this);
            QueryBox.Focus();
        };

        Refresh();
    }

    /// <summary>Главное окно: через него выполняются переходы по разделам и пресетам.</summary>
    public MainWindow? Host { get; init; }

    /// <summary>Перестраивает список результатов под текущий запрос.</summary>
    private void Refresh()
    {
        var hits = TweakCatalog.Search(QueryBox.Text);

        _rows = hits.Select(hit => new SearchRow(hit)).ToList();
        ResultList.ItemsSource = _rows;

        Select(_rows.Count > 0 ? 0 : -1);

        EmptyText.Visibility = _rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        CountText.Text = string.IsNullOrWhiteSpace(QueryBox.Text)
            ? LocalizationService.T("Начните вводить название твика, раздела или действия.")
            : LocalizationService.TF("Найдено: {0}.", _rows.Count);

        // Счётчик проявляется только при смене количества результатов: иначе при быстром
        // вводе текст всё время оставался бы полупрозрачным и «дрожал».
        if (_lastResultCount != _rows.Count)
            UiAnimation.FadeSlideIn(CountText, offsetY: 3, durationMs: 160);

        _lastResultCount = _rows.Count;

        AnimateResults();
    }

    /// <summary>
    /// Проявляет список результатов каскадом, как карточки на страницах твикера.
    /// Контейнеры строк создаются во время раскладки, поэтому эффект запускается
    /// с приоритетом Loaded — уже после того, как строки получили своё место.
    /// </summary>
    private void AnimateResults()
    {
        if (!UiAnimation.IsActive)
            return;

        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            ResultList.UpdateLayout();

            var containers = _rows
                .Select(row => ResultList.ItemContainerGenerator.ContainerFromItem(row) as FrameworkElement)
                .ToList();

            // Тайминги короче, чем у карточек: список перестраивается на каждом символе.
            UiAnimation.StaggerElements(containers, offsetY: 8, durationMs: 170, staggerMs: 22, maxStagger: 7);

            if (EmptyText.Visibility == Visibility.Visible)
                UiAnimation.FadeSlideIn(EmptyText, offsetY: 4, durationMs: 160);
        }));
    }

    /// <summary>Выделяет строку по индексу и прокручивает к ней список.</summary>
    private void Select(int index, bool animate = false)
    {
        if (_selected >= 0 && _selected < _rows.Count)
            _rows[_selected].IsSelected = false;

        _selected = index;

        if (_selected < 0 || _selected >= _rows.Count)
            return;

        _rows[_selected].IsSelected = true;

        // ItemsControl не умеет ScrollIntoView — прокручиваем созданный контейнер строки.
        if (ResultList.ItemContainerGenerator.ContainerFromItem(_rows[_selected]) is FrameworkElement container)
        {
            container.BringIntoView();

            // Лёгкий «pop» при переходе стрелками — тот же отклик, что у вкладок навигации.
            if (animate)
                UiAnimation.PulseScale(container, 0.98, 180);
        }
    }

    /// <summary>Сдвигает выделение по кругу.</summary>
    private void Move(int delta)
    {
        if (_rows.Count == 0)
            return;

        var next = _selected < 0 ? 0 : _selected + delta;

        if (next < 0)
            next = _rows.Count - 1;
        else if (next >= _rows.Count)
            next = 0;

        Select(next, animate: true);
    }

    /// <summary>Стрелки, Enter и Esc работают независимо от того, где находится фокус.</summary>
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                e.Handled = true;
                Close();
                return;

            case Key.Down:
                e.Handled = true;
                Move(1);
                return;

            case Key.Up:
                e.Handled = true;
                Move(-1);
                return;

            case Key.Enter:
                e.Handled = true;
                ActivateSelected();
                return;
        }

        base.OnPreviewKeyDown(e);
    }

    private void QueryBox_TextChanged(object sender, TextChangedEventArgs e) => Refresh();

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    /// <summary>Окно без рамки — перетаскиваем его за любое свободное место.</summary>
    private void Root_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed)
            return;

        // Клик по кнопке, полю ввода или списку результатов не должен тянуть окно.
        if (IsInteractive(e.OriginalSource as DependencyObject))
            return;

        try
        {
            DragMove();
        }
        catch
        {
            // Окно потеряло фокус во время перетаскивания — не мешаем.
        }
    }

    /// <summary>Проверяет, что клик пришёлся по интерактивному элементу внутри окна.</summary>
    private static bool IsInteractive(DependencyObject? node)
    {
        while (node is not null)
        {
            if (node is ButtonBase or TextBoxBase or ScrollViewer or ItemsControl or Wpf.Ui.Controls.Button)
                return true;

            node = node is Visual ? VisualTreeHelper.GetParent(node) : null;
        }

        return false;
    }

    private void Result_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement element && element.Tag is SearchRow row)
            ActivateRow(row);
    }

    /// <summary>Наведение на строку: она мягко сдвигается вправо — как кнопки навигации.</summary>
    private void Result_MouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is FrameworkElement element)
            if (element.Tag is SearchRow row && _rows.IndexOf(row) is int idx and >= 0) Select(idx);
    }

    /// <summary>Курсор ушёл со строки — возвращаем её на место.</summary>
    private void Result_MouseLeave(object sender, MouseEventArgs e)
    {
        if (sender is FrameworkElement element)
            { }
    }

    private void ActivateSelected()
    {
        if (_selected >= 0 && _selected < _rows.Count)
            ActivateRow(_rows[_selected]);
    }

    /// <summary>Переходит в раздел или пресет либо выполняет быстрое действие.</summary>
    private async void ActivateRow(SearchRow row)
    {
        var entry = row.Entry;
        var availability = row.Availability;

        if (!availability.IsAvailable)
        {
            await Dialogs.ShowInfoAsync(
                "Недоступно на этой системе",
                string.IsNullOrWhiteSpace(availability.Reason)
                    ? "Этот пункт недоступен на вашей версии Windows."
                    : availability.Reason);
            return;
        }

        if (entry.Kind == TweakKind.Action)
        {
            await RunActionAsync(entry);
            return;
        }

        Close();

        if (!string.IsNullOrWhiteSpace(entry.PresetId))
            Host?.OpenPreset(entry.PresetId);
        else if (!string.IsNullOrWhiteSpace(entry.Route))
            Host?.NavigateByTag(entry.Route);
    }

    /// <summary>Выполняет быстрое действие, выбранное в поиске.</summary>
    private static async Task RunActionAsync(TweakEntry entry)
    {
        if (entry.Run is null)
            return;

        if (entry.NeedsConfirm &&
            !await Dialogs.ShowConfirmAsync(
                entry.Title,
                LocalizationService.TF("Выполнить действие «{0}»?", LocalizationService.T(entry.Title))))
        {
            return;
        }

        try
        {
            await entry.Run();

            if (entry.NotifyOnSuccess)
                await Dialogs.ShowInfoAsync(
                    "Готово",
                    LocalizationService.TF("Действие «{0}» выполнено.", LocalizationService.T(entry.Title)));
        }
        catch (Exception ex)
        {
            await Dialogs.ShowErrorAsync("Ошибка", ex.Message);
        }
    }

}

/// <summary>Строка результата поиска: подписи, иконка и признак выделения.</summary>
public sealed class SearchRow : INotifyPropertyChanged
{
    private bool _isSelected;

    public SearchRow(TweakSearchHit hit)
    {
        Entry = hit.Entry;
        Availability = hit.Availability;

        Title = LocalizationService.T(hit.Entry.Title);

        var parts = new List<string> { TweakCatalog.KindLabel(hit.Entry.Kind) };

        if (!string.IsNullOrWhiteSpace(hit.Entry.Category))
            parts.Add(LocalizationService.T(hit.Entry.Category));

        if (!string.IsNullOrWhiteSpace(hit.Entry.Description))
            parts.Add(LocalizationService.T(hit.Entry.Description));

        Subtitle = string.Join(" · ", parts);

        State = !hit.Availability.IsAvailable
            ? hit.Availability.Reason
            : string.IsNullOrWhiteSpace(hit.State) ? string.Empty : LocalizationService.T(hit.State);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public TweakEntry Entry { get; }

    public WindowsVersionService.Availability Availability { get; }

    public string Title { get; }

    public string Subtitle { get; }

    /// <summary>Текущее состояние твика или причина недоступности.</summary>
    public string State { get; }

    /// <summary>Иконка по типу записи: раздел, пресет, твик или действие.</summary>
    public SymbolRegular Icon => Entry.Kind switch
    {
        TweakKind.Section => SymbolRegular.AppsList24,
        TweakKind.Preset => SymbolRegular.Flash24,
        TweakKind.Action => SymbolRegular.Play24,
        _ => SymbolRegular.Wrench24,
    };

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
                return;

            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }
}


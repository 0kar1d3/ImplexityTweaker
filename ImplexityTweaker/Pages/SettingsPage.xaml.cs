using System.Windows;
using System.Windows.Controls;
using ImplexityTweaker.Services;

namespace ImplexityTweaker.Pages;

public partial class SettingsPage : Page
{
    private bool _loaded;
    private bool _initThemeCombo;

    public SettingsPage()
    {
        InitializeComponent();
        Loaded += SettingsPage_Loaded;
    }

    private void SettingsPage_Loaded(object sender, RoutedEventArgs e)
    {
        if (_loaded)
            return;
        _loaded = true;
        _initThemeCombo = true;
        ThemeCombo.Items.Clear();
        foreach (var (id, title, _) in ThemeApplier.ThemeCatalog)
            ThemeCombo.Items.Add(new ThemeItem(id, title));

        var settings = AppSettings.Load();
        foreach (ThemeItem item in ThemeCombo.Items)
        {
            if (item.Id == settings.ThemeId)
            {
                ThemeCombo.SelectedItem = item;
                break;
            }
        }
        ThemeCombo.SelectedItem ??= ThemeCombo.Items[0];
        _initThemeCombo = false;
    }

    private void ThemeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initThemeCombo || ThemeCombo.SelectedItem is not ThemeItem item)
            return;
        ThemeApplier.Apply(item.Id);
    }

    private void SaveTheme_Click(object sender, RoutedEventArgs e)
    {
        if (ThemeCombo.SelectedItem is not ThemeItem item)
            return;
        var s = AppSettings.Load();
        s.ThemeId = item.Id;
        s.Save();
        ThemeApplier.Apply(item.Id);
        _ = Dialogs.ShowInfoAsync("Сохранено", "Тема сохранена в файл настроек и уже применена к интерфейсу.");
    }

    private void ExportConfig_Click(object sender, RoutedEventArgs e)
    {
        // Сохраняем текущую выбранную тему в настройки перед экспортом.
        if (ThemeCombo.SelectedItem is ThemeItem item)
        {
            var s = AppSettings.Load();
            s.ThemeId = item.Id;
            s.Save();
        }

        var settings = AppSettings.Load();
        if (settings.ExportWithDialog())
            _ = Dialogs.ShowInfoAsync("Экспорт", "Настройки успешно сохранены в файл .implexity.");
        else
            _ = Dialogs.ShowInfoAsync("Экспорт", "Экспорт отменён или не удался.");
    }

    private void ImportConfig_Click(object sender, RoutedEventArgs e)
    {
        var settings = AppSettings.Load();
        if (!settings.ImportWithDialog())
        {
            _ = Dialogs.ShowErrorAsync("Импорт", "Не удалось загрузить файл. Убедитесь, что это корректный файл конфигурации .implexity.");
            return;
        }

        // Применяем импортированную тему и сохраняем в локальные настройки.
        settings.Save();
        ThemeApplier.Apply(settings.ThemeId);

        // Обновляем выбранный элемент в комбобоксе.
        _initThemeCombo = true;
        foreach (ThemeItem t in ThemeCombo.Items)
        {
            if (t.Id == settings.ThemeId)
            {
                ThemeCombo.SelectedItem = t;
                break;
            }
        }
        _initThemeCombo = false;

        _ = Dialogs.ShowInfoAsync("Импорт", "Настройки успешно загружены из файла .implexity и применены.");
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (Application.Current.MainWindow is MainWindow mw)
            mw.NavigateByTag("firstsetup");
    }

    private sealed record ThemeItem(string Id, string Title)
    {
        public override string ToString() => Title;
    }
}

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

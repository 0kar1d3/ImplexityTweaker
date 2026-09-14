using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ImplexityTweaker.Services;
using Microsoft.Win32;

namespace ImplexityTweaker.Pages;

public partial class SettingsPage : Page
{
    private bool _loaded;
    private bool _initThemeCombo;
    private bool _initLanguageCombo;
    private string? _selectedConfigPath;

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

        _initLanguageCombo = true;
        LanguageCombo.Items.Clear();

        // Пункт «Системный язык (авто)» — определяется из языка Windows.
        LanguageCombo.Items.Add(new LanguageItem(null, LocalizationService.T("Системный язык (авто)")));
        foreach (var lang in LocalizationService.Languages)
            LanguageCombo.Items.Add(new LanguageItem(lang.Code, lang.NativeName));

        // Если язык не задан — выбран пункт «авто».
        var current = LocalizationService.CurrentCode;
        var hasExplicit = false;
        foreach (LanguageItem item in LanguageCombo.Items)
        {
            if (item.Code != null && item.Code == current)
            {
                LanguageCombo.SelectedItem = item;
                hasExplicit = true;
                break;
            }
        }
        if (!hasExplicit)
            LanguageCombo.SelectedItem = LanguageCombo.Items[0];
        _initLanguageCombo = false;

        RefreshConfigs();
        RefreshAccountCard();
        AboutVersionText.Text = LocalizationService.TF("Версия {0} · WPF-UI Fluent · .NET 8",
            System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.5.0");
        AccountService.AccountChanged += Account_Changed;
        Unloaded += SettingsPage_Unloaded;
    }

    private void SettingsPage_Unloaded(object sender, RoutedEventArgs e)
    {
        AccountService.AccountChanged -= Account_Changed;
    }

    private void Account_Changed(object? sender, EventArgs e)
    {
        try { Dispatcher.BeginInvoke(RefreshAccountCard); } catch { }
    }

    // ===================== Аккаунт =====================

    private void RefreshAccountCard()
    {
        var account = AccountService.Current;
        if (account == null)
        {
            AccLoggedIn.Visibility = Visibility.Collapsed;
            AccLoggedOut.Visibility = Visibility.Visible;
            AccOpenProfileBtn.Visibility = Visibility.Collapsed;
            AccLogoutBtn.Visibility = Visibility.Collapsed;
            AccLoginBtn.Visibility = Visibility.Visible;
            return;
        }

        AccLoggedIn.Visibility = Visibility.Visible;
        AccLoggedOut.Visibility = Visibility.Collapsed;
        AccOpenProfileBtn.Visibility = Visibility.Visible;
        AccLogoutBtn.Visibility = Visibility.Visible;
        AccLoginBtn.Visibility = Visibility.Collapsed;

        AccName.Text = account.DisplayName;
        AccName.Foreground = MakeBrush(AccountService.GetPrefixColorHex(account.PrefixColor));

        var hasPrefix = !string.IsNullOrWhiteSpace(account.Prefix);
        AccPrefixBadge.Visibility = hasPrefix ? Visibility.Visible : Visibility.Collapsed;
        if (hasPrefix)
        {
            AccPrefix.Text = "⚡ " + account.Prefix;
            AccPrefix.Foreground = MakeBrush(AccountService.GetPrefixColorHex(account.PrefixColor));
            AccPrefixBadge.Background = MakeBrush(AccountService.GetPrefixBadgeColorHex(account.PrefixColor), 0x1A);
            AccPrefixBadge.BorderBrush = MakeBrush(AccountService.GetPrefixBadgeColorHex(account.PrefixColor), 0x33);
            AccPrefixBadge.BorderThickness = new Thickness(1);
        }

        AccNumber.Text = account.AccountNumber;
        AccEmail.Text = account.Email ?? "";
        AccAvatarBrush.ImageSource = null;

        _ = LoadAvatarAsync(account.AvatarUrl);
    }

    private static SolidColorBrush MakeBrush(string hex, byte alpha = 0xFF)
    {
        var c = (Color)ColorConverter.ConvertFromString(hex)!;
        c.A = alpha;
        var brush = new SolidColorBrush(c);
        brush.Freeze();
        return brush;
    }

    private async Task LoadAvatarAsync(string? url)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(url))
                return;

            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            var bytes = await client.GetByteArrayAsync(url);
            using var ms = new MemoryStream(bytes);

            var bi = new BitmapImage();
            bi.BeginInit();
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.StreamSource = ms;
            bi.EndInit();
            bi.Freeze();

            // Аватар мог смениться/аккаунт мог выйти, пока шла загрузка, — не подставляем чужой.
            if (AccountService.Current?.AvatarUrl == url)
                AccAvatarBrush.ImageSource = bi;
        }
        catch
        {
            // Оставляем иконку-заглушку.
        }
    }

    private void AccLogin_Click(object sender, RoutedEventArgs e)
    {
        var login = new Windows.LoginWindow { Owner = Application.Current.MainWindow };
        login.ShowDialog();
    }

    private async void AccLogout_Click(object sender, RoutedEventArgs e)
    {
        var confirm = await Dialogs.ShowConfirmAsync(
            "Выход из аккаунта",
            LocalizationService.TF("Выйти из аккаунта «{0}»?", AccountService.Current?.DisplayName ?? ""));
        if (confirm)
            AccountService.Logout();
    }

    private void AccOpenProfile_Click(object sender, RoutedEventArgs e)
    {
        if (AccountService.Current is { } account)
        {
            try
            {
                Process.Start(new ProcessStartInfo(AccountService.GetProfileUrl(account)) { UseShellExecute = true });
            }
            catch
            {
                /* ignore */
            }
        }
    }

    /// <summary>Открывает ссылку на соцсеть (телеграм/дискорд/ютуб).</summary>
    private void Social_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not string url || string.IsNullOrWhiteSpace(url))
            return;

        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
            /* ignore */
        }
    }

    // ===================== Конфиги =====================

    private void RefreshConfigs()
    {
        ConfigsFolderText.Text = LocalizationService.TF("Папка конфигов: {0}", AppSettings.ConfigsFolder);

        try
        {
            Directory.CreateDirectory(AppSettings.ConfigsFolder);
            var files = Directory.EnumerateFiles(AppSettings.ConfigsFolder, "*" + AppSettings.ConfigExtension)
                .Select(f => new FileInfo(f))
                .OrderByDescending(f => f.LastWriteTime)
                .Select(f => new ConfigItem(
                    f.Name,
                    f.FullName,
                    $"{FormatSize(f.Length)} · {f.LastWriteTime:dd.MM.yyyy HH:mm}",
                    string.Equals(f.FullName, _selectedConfigPath, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            ConfigsList.ItemsSource = files;
            ConfigsEmptyText.Visibility = files.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch
        {
            ConfigsList.ItemsSource = null;
            ConfigsEmptyText.Visibility = Visibility.Visible;
        }
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} {LocalizationService.T("Б")}";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:0.#} {LocalizationService.T("КБ")}";
        return $"{bytes / 1024.0 / 1024.0:0.##} {LocalizationService.T("МБ")}";
    }

    private void OpenConfigsFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(AppSettings.ConfigsFolder);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{AppSettings.ConfigsFolder}\"")
            {
                UseShellExecute = true
            });
        }
        catch
        {
            /* ignore */
        }
    }

    private void RefreshConfigs_Click(object sender, RoutedEventArgs e) => RefreshConfigs();

    private async void ApplyConfig_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is string path)
            await ApplyConfigFileAsync(path);
    }

    private async Task ApplyConfigFileAsync(string path)
    {
        try
        {
            var settings = AppSettings.Load();
            if (!settings.ImportFromFile(path))
            {
                await Dialogs.ShowErrorAsync("Импорт",
                    "Не удалось загрузить файл. Убедитесь, что это корректный файл конфигурации .implexity.");
                return;
            }

            settings.Save();
            ThemeApplier.Apply(settings.ThemeId);

            // Обновляем выбранную тему в комбобоксе.
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

            await Dialogs.ShowInfoAsync("Импорт", "Настройки успешно загружены из файла .implexity и применены.");
        }
        catch (Exception ex)
        {
            await Dialogs.ShowErrorAsync("Импорт", ex.Message);
        }
    }

    /// <summary>Создаёт новый .implexity-файл из текущих настроек.</summary>
    private async void CreateConfig_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(AppSettings.ConfigsFolder);
            var settings = AppSettings.Load();

            var dialog = new SaveFileDialog
            {
                InitialDirectory = AppSettings.ConfigsFolder,
                Filter = "Implexity Config (*.implexity)|*.implexity",
                FileName = $"config_{DateTime.Now:yyyy-MM-dd_HH-mm}.implexity",
                DefaultExt = ".implexity",
                AddExtension = true
            };
            if (dialog.ShowDialog() != true)
                return;

            if (!settings.ExportToFile(dialog.FileName))
            {
                await Dialogs.ShowErrorAsync("Ошибка", "Не удалось создать файл конфигурации.");
                return;
            }

            _selectedConfigPath = dialog.FileName;
            RefreshConfigs();
            await Dialogs.ShowInfoAsync("Конфиги", "Конфиг создан.");
        }
        catch (Exception ex)
        {
            await Dialogs.ShowErrorAsync("Ошибка", ex.Message);
        }
    }

    /// <summary>Перезаписывает выбранный в списке конфиг текущими настройками.</summary>
    private async void SaveConfig_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedConfigPath == null || !File.Exists(_selectedConfigPath))
        {
            await Dialogs.ShowInfoAsync("Конфиги", "Выберите конфиг из списка.");
            return;
        }

        var settings = AppSettings.Load();
        if (!settings.ExportToFile(_selectedConfigPath))
        {
            await Dialogs.ShowErrorAsync("Ошибка", "Не удалось сохранить файл конфигурации.");
            return;
        }

        RefreshConfigs();
        await Dialogs.ShowInfoAsync("Конфиги", "Конфиг сохранён.");
    }

    /// <summary>Загружает конфиг .implexity из любого места и применяет его.</summary>
    private async void LoadConfig_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new OpenFileDialog
            {
                Filter = LocalizationService.T("Implexity Config (*.implexity)|*.implexity|Все файлы (*.*)|*.*"),
                Title = LocalizationService.T("Загрузить конфиг")
            };
            if (dialog.ShowDialog() != true)
                return;

            await ApplyConfigFileAsync(dialog.FileName);
        }
        catch (Exception ex)
        {
            await Dialogs.ShowErrorAsync("Ошибка", ex.Message);
        }
    }

    /// <summary>Выбор конфига в списке (для «Сохранить конфиг»).</summary>
    private void ConfigItem_Click(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is string path)
        {
            _selectedConfigPath = path;
            RefreshConfigs();
        }
    }

    /// <summary>Публикует конфиг на сайт (с выбором: публично или приватно).</summary>
    private async void PublishConfig_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not string path)
            return;

        if (AccountService.Current == null)
        {
            await Dialogs.ShowErrorAsync("Опубликовать на сайте", "Для публикации конфига войдите в аккаунт.");
            return;
        }

        // Выбор видимости: публично / приватно / отмена.
        var dialog = new Windows.PublishConfigWindow(Path.GetFileName(path)) { Owner = Application.Current.MainWindow };
        dialog.ShowDialog();
        if (dialog.ResultIsPrivate == null)
            return;
        var isPrivate = dialog.ResultIsPrivate.Value;

        var (ok, error) = await AccountService.PublishConfigAsync(path, isPrivate);
        if (ok)
            await Dialogs.ShowInfoAsync("Опубликовать на сайте",
                isPrivate ? "Конфиг опубликован как приватный." : "Конфиг опубликован на сайте.");
        else
            await Dialogs.ShowErrorAsync("Опубликовать на сайте",
                "Не удалось опубликовать конфиг." + (string.IsNullOrEmpty(error) ? "" : $" {error}"));
    }

    private async void DeleteConfig_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not string path)
            return;

        var fileName = Path.GetFileName(path);
        var confirm = await Dialogs.ShowConfirmAsync("Удаление файла", $"Удалить файл «{fileName}»?");
        if (!confirm)
            return;

        try
        {
            File.Delete(path);
            RefreshConfigs();
        }
        catch (Exception ex)
        {
            await Dialogs.ShowErrorAsync("Ошибка", ex.Message);
        }
    }

    private void LanguageCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initLanguageCombo || LanguageCombo.SelectedItem is not LanguageItem item)
            return;

        var s = AppSettings.Load();

        if (item.Code == null)
        {
            // «Авто»: если язык уже определён системой — ничего не меняем.
            if (string.IsNullOrEmpty(s.Language))
                return;
            s.Language = null;
            s.Save();
            LocalizationService.ChangeLanguage(LocalizationService.ResolveSystemLanguage());
            return;
        }

        if (item.Code == LocalizationService.CurrentCode)
            return;

        s.Language = item.Code;
        s.Save();

        // Меняет язык и пересобирает интерфейс (MainWindow пересоздаёт текущую страницу).
        LocalizationService.ChangeLanguage(item.Code);
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

    // ===================== Обновления =====================

    private async void CheckUpdates_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.5.0";
            VersionText.Text = LocalizationService.TF("Текущая версия: {0}. Проверяем обновления…", version);

            var result = await UpdateCheckerService.CheckAsync(version);

            switch (result.Status)
            {
                case UpdateCheckerService.UpdateStatus.UpdateAvailable:
                    VersionText.Text = LocalizationService.TF("Доступна новая версия: {0}", result.LatestVersion);
                    await Dialogs.ShowInfoAsync("Доступно обновление",
                        LocalizationService.TF("Вышла версия {0}. Сейчас откроется страница загрузки.", result.LatestVersion));

                    if (!string.IsNullOrWhiteSpace(result.Url))
                    {
                        try
                        {
                            Process.Start(new ProcessStartInfo(result.Url) { UseShellExecute = true });
                        }
                        catch
                        {
                            // браузер не открылся — не критично
                        }
                    }
                    break;

                case UpdateCheckerService.UpdateStatus.UpToDate:
                    VersionText.Text = LocalizationService.TF("У вас последняя версия ({0}).", version);
                    break;

                default:
                    VersionText.Text = LocalizationService.TF("Не удалось проверить обновления (версия {0}).", version);
                    break;
            }
        }
        catch (Exception ex)
        {
            VersionText.Text = ex.Message;
        }
    }

    private sealed record ThemeItem(string Id, string Title)
    {
        public override string ToString() => Title;
    }

    private sealed record LanguageItem(string? Code, string NativeName)
    {
        public override string ToString() => NativeName;
    }

    private sealed record ConfigItem(string Name, string Path, string Meta, bool IsSelected = false);
}

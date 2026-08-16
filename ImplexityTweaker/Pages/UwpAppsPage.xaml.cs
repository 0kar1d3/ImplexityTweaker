using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using ImplexityTweaker.Services;

namespace ImplexityTweaker.Pages;

public partial class UwpAppsPage : Page
{
    public ObservableCollection<PackageInfo> Packages { get; } = [];

    public UwpAppsPage()
    {
        InitializeComponent();
        AppListBox.ItemsSource = Packages;
        _ = LoadPackagesAsync();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => _ = LoadPackagesAsync();

    // Курируемый список безопасных для удаления встроенных приложений Microsoft.
    // Удаление этих пакетов не ломает систему. Ключ — PackageName из Get-AppxPackage.
    private static readonly (string DisplayName, string PackageName)[] KnownApps =
    [
        ("Калькулятор", "Microsoft.WindowsCalculator"),
        ("Почта и календарь", "microsoft.windowscommunicationsapps"),
        ("Карты", "Microsoft.WindowsMaps"),
        ("Новости", "Microsoft.BingNews"),
        ("Погода", "Microsoft.BingWeather"),
        ("Xbox Game Bar", "Microsoft.XboxGamingOverlay"),
        ("Кино и ТВ", "Microsoft.ZuneVideo"),
        ("Музыка", "Microsoft.ZuneMusic"),
        ("Камера", "Microsoft.WindowsCamera"),
        ("Фотографии", "Microsoft.Windows.Photos"),
        ("Запись голоса", "Microsoft.WindowsSoundRecorder"),
        ("Будильники и часы", "Microsoft.WindowsAlarms"),
        ("Диктофон", "Microsoft.WindowsVoiceRecorder"),
        ("Советы", "Microsoft.GetHelp"),
        ("Обратная связь", "Microsoft.WindowsFeedbackHub"),
        ("Быстрая помощь", "MicrosoftCorporationII.QuickAssist"),
        ("Skype", "Microsoft.SkypeApp"),
        ("OneNote", "Microsoft.Office.OneNote"),
        ("To Do", "Microsoft.Todos"),
        ("Xbox", "Microsoft.XboxApp"),
        ("Xbox Console Companion", "Microsoft.XboxIdentityProvider"),
        ("3D Viewer", "Microsoft.Microsoft3DViewer"),
        ("Paint 3D", "Microsoft.MSPaint"),
        ("Snip & Sketch / Ножницы", "Microsoft.ScreenSketch"),
        ("Ваш телефон", "Microsoft.YourPhone"),
        ("Solitaire Collection", "Microsoft.MicrosoftSolitaireCollection"),
        ("Подсказки", "Microsoft.People"),
        ("Смешанная реальность", "Microsoft.MixedReality.Portal"),
        ("Office Hub", "Microsoft.MicrosoftOfficeHub"),
    ];

    private async System.Threading.Tasks.Task LoadPackagesAsync()
    {
        try { LoadingIndicator.Visibility = Visibility.Visible; } catch { }

        // Получаем список установленных пакетов, соответствующих нашему курируемому списку.
        // Выводим по одной строке "PackageName<TAB>PackageFullName", чтобы легко распарсить.
        var r = await CommandRunner.RunPowerShellAsync(
            "Get-AppxPackage | Where-Object { $_.IsFramework -eq $false } | ForEach-Object { $_.Name + \"`t\" + $_.PackageFullName }");

        // Множество установленных имён пакетов.
        var installed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var fullNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (r.ExitCode == 0 && !string.IsNullOrWhiteSpace(r.Output))
        {
            foreach (var line in r.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmed = line.Trim();
                if (trimmed.Length == 0) continue;

                var parts = trimmed.Split('\t');
                var name = parts.Length > 0 ? parts[0].Trim() : "";
                var id = parts.Length > 1 ? parts[1].Trim() : "";
                if (string.IsNullOrWhiteSpace(name)) continue;

                installed.Add(name);
                if (!string.IsNullOrWhiteSpace(id) && !fullNames.ContainsKey(name))
                    fullNames[name] = id;
            }
        }

        // Показываем только те безопасные приложения Microsoft, которые реально установлены.
        var items = new List<PackageInfo>();
        foreach (var (display, pkg) in KnownApps)
        {
            if (installed.Contains(pkg))
            {
                var id = fullNames.TryGetValue(pkg, out var fid) ? fid : pkg;
                items.Add(new PackageInfo(display, id));
            }
        }

        // Сортируем по имени и обновляем UI в потоке диспетчера.
        items = items.OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).ToList();

        Packages.Clear();
        foreach (var p in items)
            Packages.Add(p);

        try { LoadingIndicator.Visibility = Visibility.Collapsed; } catch { }
    }

    private async void RemoveApp_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.Tag is string id)
        {
            await RemovePackageAsync(id);
        }
    }

    private async void RemoveManual_Click(object sender, RoutedEventArgs e)
    {
        var id = ManualPackageBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(id))
            return;
        await RemovePackageAsync(id);
    }

    private async System.Threading.Tasks.Task RemovePackageAsync(string id)
    {
        var confirm = MessageBox.Show(
            $"Удалить пакет: {id}?\n\nЭто действие удалит приложение для текущего пользователя.",
            "Подтверждение",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes)
            return;

        var r = await CommandRunner.RunPowerShellAsync($"Get-AppxPackage *{id}* | Remove-AppxPackage");
        if (r.ExitCode == 0)
            await Dialogs.ShowInfoAsync("Готово", "Приложение удалено для текущего пользователя.");
        else
            await Dialogs.ShowErrorAsync("Ошибка", r.Error + r.Output);
    }

    public class PackageInfo
    {
        public string Name { get; }
        public string Id { get; }
        public PackageInfo(string name, string id) { Name = name; Id = id; }
    }
}
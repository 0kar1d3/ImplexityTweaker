using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using ImplexityTweaker.Services;

namespace ImplexityTweaker.Pages;

public partial class UwpAppsPage : Page
{
    private static readonly IReadOnlyList<(string Name, string Id)> Packages =
    [
        ("Калькулятор", "Microsoft.WindowsCalculator"),
        ("Почта и календарь", "microsoft.windowscommunicationsapps"),
        ("Карты", "Microsoft.WindowsMaps"),
        ("Новости", "Microsoft.BingNews"),
        ("Погода", "Microsoft.BingWeather"),
        ("Xbox", "Microsoft.XboxGamingOverlay"),
        ("Кино и ТВ", "Microsoft.ZuneVideo"),
        ("Музыка", "Microsoft.ZuneMusic")
    ];

    public UwpAppsPage()
    {
        InitializeComponent();
        LoadList();
    }

    private void LoadList()
    {
        PackageBox.Items.Clear();
        foreach (var (name, id) in Packages)
            PackageBox.Items.Add($"{name} — {id}");
        if (PackageBox.Items.Count > 0)
            PackageBox.SelectedIndex = 0;
    }

    private async void Remove_Click(object sender, RoutedEventArgs e)
    {
        var id = ManualPackageBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(id))
        {
            if (PackageBox.SelectedItem is not string s)
                return;
            id = s.Split('—', 2)[1].Trim();
        }

        var r = await CommandRunner.RunPowerShellAsync($"Get-AppxPackage *{id}* | Remove-AppxPackage");
        if (r.ExitCode == 0)
            await Dialogs.ShowInfoAsync("Готово", "Приложение удалено для текущего пользователя.");
        else
            await Dialogs.ShowErrorAsync("Ошибка", r.Error + r.Output);
    }

    private void ReloadList_Click(object sender, RoutedEventArgs e) => LoadList();
}

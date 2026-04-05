using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows.Controls;
using Microsoft.Win32;
using ImplexityTweaker.Services;

namespace ImplexityTweaker.Pages;

public partial class ContextMenuPage : Page
{
    private const string OldContextClsid = @"CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32";
    private const string DesktopKey = @"Control Panel\Desktop";
    private const string TerminalMenuKey = @"Directory\shell\OpenInTerminalHere";

    public ContextMenuPage()
    {
        InitializeComponent();
        Refresh();
    }

    private void Refresh()
    {
        // старое контекстное меню
        try
        {
            using var key = Registry.ClassesRoot.OpenSubKey(OldContextClsid, false);
            OldContextMenuToggle.IsChecked = key != null;
        }
        catch
        {
            OldContextMenuToggle.IsChecked = false;
        }

        
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(DesktopKey, false);
            var v = key?.GetValue("MenuShowDelay") as int? ?? 400;
            RemoveContextDelayToggle.IsChecked = v == 0;
        }
        catch
        {
            RemoveContextDelayToggle.IsChecked = false;
        }

        
        try
        {
            using var key = Registry.ClassesRoot.OpenSubKey(TerminalMenuKey, false);
            OpenInTerminalToggle.IsChecked = key != null;
        }
        catch
        {
            OpenInTerminalToggle.IsChecked = false;
        }
    }

    private async void Apply_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        var oldToggle = OldContextMenuToggle.IsChecked == true;
        var delayToggle = RemoveContextDelayToggle.IsChecked == true;
        var termToggle = OpenInTerminalToggle.IsChecked == true;

        try
        {
            
            if (oldToggle)
            {
                using var key = Registry.ClassesRoot.CreateSubKey(OldContextClsid);
                key?.SetValue("", "", RegistryValueKind.String);
            }
            else
            {
                Registry.ClassesRoot.DeleteSubKeyTree(OldContextClsid, throwOnMissingSubKey: false);
            }

           
            using (var desk = Registry.CurrentUser.CreateSubKey(DesktopKey))
                desk?.SetValue("MenuShowDelay", delayToggle ? 0 : 400, RegistryValueKind.DWord);

            
            if (termToggle)
            {
                using var shell = Registry.ClassesRoot.CreateSubKey(TerminalMenuKey);
                shell?.SetValue(null, "Open in Windows Terminal", RegistryValueKind.String);
                using var cmd = shell?.CreateSubKey(@"command");
                cmd?.SetValue(null, "wt.exe -d \"%V\"", RegistryValueKind.String);
            }
            else
            {
                Registry.ClassesRoot.DeleteSubKeyTree(TerminalMenuKey, throwOnMissingSubKey: false);
            }

            
            var unsupported = (ShareToggle.IsChecked == true) ||
                               (RestoreOldVersionToggle.IsChecked == true) ||
                               (SendToggle.IsChecked == true) ||
                               (CopyPathToggle.IsChecked == true) ||
                               (PinStartToggle.IsChecked == true) ||
                               (PinTaskbarToggle.IsChecked == true);

            if (unsupported)
                await Dialogs.ShowInfoAsync("Готово", "Часть переключателей пока не реализована. Применены только:\n- старое контекстное меню\n- задержка контекстного меню\n- пункт «Открыть в терминале».");
            else
                await Dialogs.ShowInfoAsync("Готово", "Параметры контекстного меню применены. Может потребоваться перезапуск проводника.");

            Refresh();
        }
        catch (System.Exception ex)
        {
            await Dialogs.ShowErrorAsync("Ошибка", ex.Message);
        }
    }
}

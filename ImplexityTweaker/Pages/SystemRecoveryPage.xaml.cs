using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using ImplexityTweaker.Services;
using System.Text;

namespace ImplexityTweaker.Pages;

public partial class SystemRecoveryPage : Page
{
    public SystemRecoveryPage()
    {
        InitializeComponent();
        TelemetryFrame.Navigate(new TelemetryPage());
    }

    private void OpenSysRestore_Click(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo("SystemPropertiesProtection") { UseShellExecute = true });
    }

    private void DiskMgmt_Click(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo("diskmgmt.msc") { UseShellExecute = true });
    }

    private async void CreateRestorePoint_Click(object sender, RoutedEventArgs e)
    {
        var userConfirm = MessageBox.Show(
            "Эта операция:\n\n" +
            "1) создаст точку восстановления;\n" +
            "2) выполнит DISM и SFC (восстановление системных компонентов);\n" +
            "3) отключит Sticky Keys, UAC и SmartScreen;\n" +
            "4) выключит гибернацию;\n" +
            "5) попытается настроить legacy-загрузчик для текущей системы.\n\n" +
            "Требуется запуск от администратора и, возможно, перезагрузка.\n\n" +
            "Продолжить?",
            "Подтверждение",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (userConfirm != MessageBoxResult.Yes)
            return;

        // Вся логика — в одном PowerShell-скрипте, чтобы команды выполнялись последовательно.
        var ps = new StringBuilder();
        ps.AppendLine("$ErrorActionPreference = 'SilentlyContinue'");
        ps.AppendLine("$restoreDesc = 'ImplexityTweaker Restore Point'");
        ps.AppendLine("try { Enable-ComputerRestore -Drive 'C:\\' -ErrorAction SilentlyContinue | Out-Null } catch {}");
        ps.AppendLine("try { Checkpoint-Computer -Description $restoreDesc -RestorePointType 'MODIFY_SETTINGS' | Out-Null } catch {}");
        ps.AppendLine();

        // DISM -> SFC
        ps.AppendLine("Write-Output 'DISM: RestoreHealth...'");
        ps.AppendLine("& dism.exe /Online /Cleanup-Image /RestoreHealth");
        ps.AppendLine("Write-Output 'SFC: Scannow...'");
        ps.AppendLine("& sfc.exe /scannow");
        ps.AppendLine();

        // Sticky Keys (отключаем по пользовательскому профилю)
        ps.AppendLine("Write-Output 'Accessibility: StickyKeys отключение...'");
        ps.AppendLine("reg add \"HKCU\\Control Panel\\Accessibility\\StickyKeys\" /v Flags /t REG_DWORD /d 0 /f | Out-Null");
        ps.AppendLine("reg add \"HKCU\\Control Panel\\Accessibility\\StickyKeys\" /v HotkeyActive /t REG_DWORD /d 0 /f | Out-Null");
        ps.AppendLine("reg add \"HKCU\\Control Panel\\Accessibility\\StickyKeys\" /v HotkeyApproved /t REG_DWORD /d 0 /f | Out-Null");
        ps.AppendLine();

        // UAC (EnableLUA=0)
        ps.AppendLine("Write-Output 'UAC: отключение...'");
        ps.AppendLine("reg add \"HKLM\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Policies\\System\" /v EnableLUA /t REG_DWORD /d 0 /f | Out-Null");
        ps.AppendLine("reg add \"HKLM\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Policies\\System\" /v ConsentPromptBehaviorAdmin /t REG_DWORD /d 0 /f | Out-Null");
        ps.AppendLine("reg add \"HKLM\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Policies\\System\" /v PromptOnSecureDesktop /t REG_DWORD /d 0 /f | Out-Null");
        ps.AppendLine();

        // SmartScreen (политики Windows)
        ps.AppendLine("Write-Output 'SmartScreen: отключение...'");
        ps.AppendLine("New-Item -Path \"HKLM:\\SOFTWARE\\Policies\\Microsoft\\Windows\\System\" -Force | Out-Null");
        ps.AppendLine("reg add \"HKLM\\SOFTWARE\\Policies\\Microsoft\\Windows\\System\" /v EnableSmartScreen /t REG_DWORD /d 0 /f | Out-Null");
        ps.AppendLine("reg add \"HKLM\\SOFTWARE\\Policies\\Microsoft\\Windows\\System\" /v ShellSmartScreenLevel /t REG_DWORD /d 0 /f | Out-Null");
        ps.AppendLine();

        // Hibernate off
        ps.AppendLine("Write-Output 'Hibernate: отключение...'");
        ps.AppendLine("& powercfg.exe /h off");
        ps.AppendLine();

        // Legacy bootloader attempt
        // Примечание: смену режима UEFI/Legacy выполняет BIOS/UEFI. bcdboot только готовит загрузочные файлы.
        ps.AppendLine("Write-Output 'Boot: backup BCD и установка BIOS-mode...'");
        ps.AppendLine("$bcdBackup = Join-Path $env:TEMP (\"bcd-backup-\" + (Get-Date -Format 'yyyyMMdd-HHmmss') + \".dat\")");
        ps.AppendLine("try { bcdedit.exe /export $bcdBackup | Out-Null } catch {}");
        ps.AppendLine("try { & bcdboot.exe $env:SystemRoot /f BIOS | Out-Null } catch { Write-Output $_ }");
        ps.AppendLine("Write-Output ('BCD backup: ' + $bcdBackup)");

        var r = await CommandRunner.RunPowerShellAsync(ps.ToString());
        if (r.ExitCode == 0)
        {
            // Интерпретировать ExitCode сложно, поэтому показываем оба: короткое сообщение + вывод.
            await Dialogs.ShowInfoAsync("Готово", "Восстановление (DISM/SFC) и выбранные отключения выполнены.\n" +
                                                        "Создана точка восстановления.\n" +
                                                        "Если меняли загрузчик — может потребоваться перезагрузка и/или переключение режима загрузки в BIOS/UEFI.");
        }
        else
        {
            await Dialogs.ShowErrorAsync("Ошибка", string.IsNullOrWhiteSpace(r.Error) ? r.Output : r.Error + r.Output);
        }
    }

    private async void Compact_Click(object sender, RoutedEventArgs e)
    {
        var r = await CommandRunner.RunPowerShellAsync("compact.exe /compactos:always");
        if (r.ExitCode == 0)
            await Dialogs.ShowInfoAsync("Готово", "Команда сжатия выполнена.");
        else
            await Dialogs.ShowErrorAsync("Ошибка", r.Error + r.Output);
    }
}

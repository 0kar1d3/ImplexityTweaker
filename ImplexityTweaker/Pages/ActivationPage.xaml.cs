using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using ImplexityTweaker.Services;

namespace ImplexityTweaker.Pages
{
    public partial class ActivationPage : Page
    {
        
        private readonly Dictionary<string, string> _kmsKeys = new()
        {
            { "Professional", "W269N-WFGWX-YVC9B-4J6C9-T83GX" },
            { "Core", "TX9XD-98N7V-6WMQ6-BX7FG-H8Q99" },
            { "CoreSingleLanguage", "TX9XD-98N7V-6WMQ6-BX7FG-H8Q99" },
            { "Education", "NW6C2-QMPVW-D7KKK-3GKT6-VCFB2" },
            { "ProEducation", "NW6C2-QMPVW-D7KKK-3GKT6-VCFB2" },
            { "Enterprise", "NPPR9-FWDCX-D2C8J-H872K-2YT43" },
            { "IoTEnterprise", "NPPR9-FWDCX-D2C8J-H872K-2YT43" },
            { "EnterpriseS", "M7XTQ-FN8P6-TTKYV-9D4CC-J462D" },
            { "IoTEnterpriseS", "KBN8V-HFGQ4-MGXVD-347P6-PDQGT" }
        };

        public ActivationPage()
        {
            InitializeComponent();
        }

        // кнопка активации
        private async void Check_Click(object sender, RoutedEventArgs e)
        {
            string edition = GetWindowsEdition();

            if (_kmsKeys.TryGetValue(edition, out string? kmskey))
            {
                // цепочка команд 
               
                string commands = $"/c slmgr.vbs /ipk {kmskey} & slmgr.vbs /skms kms.digiboy.ir & slmgr.vbs /ato";

                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = commands,
                        Verb = "runas", // права администратора
                        UseShellExecute = true,
                        WindowStyle = ProcessWindowStyle.Normal
                    });

                    await Dialogs.ShowInfoAsync("Активация", $"Система: {edition}\nПрименяется ключ: {kmskey}\n\nПроцесс запущен в консоли. Дождитесь уведомления от системы.");
                }
                catch (Exception ex)
                {
                    await Dialogs.ShowErrorAsync("Ошибка", "Не удалось запустить процесс от имени администратора: " + ex.Message);
                }
            }
            else
            {
                await Dialogs.ShowErrorAsync("Ошибка", $"Редакция системы '{edition}' не найдена в базе ключей.");
            }
        }

        // проверить активацию
        private void Settings_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo("ms-settings:activation") { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show("Не удалось открыть настройки: " + ex.Message);
            }
        }

        // стор"
        private void Store_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo("ms-windows-store://activation") { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show("Не удалось открыть Store: " + ex.Message);
            }
        }

        // получение издания виндовс 
        private string GetWindowsEdition()
        {
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                {
                    if (key != null)
                    {
                        object? edition = key.GetValue("EditionID");
                        if (edition != null)
                        {
                            return edition.ToString() ?? "Unknown";
                        }
                    }
                }
            }
            catch
            {
               
            }
            return "Unknown";
        }
    }
}
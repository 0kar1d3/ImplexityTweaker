using System.IO;
using System.Windows;
using System.Windows.Threading;
using ImplexityTweaker.Services;

namespace ImplexityTweaker
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Ни одно исключение из обработчиков (в т.ч. async void) не должно закрывать приложение.
            DispatcherUnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += (_, args) => WriteErrorLog(args.ExceptionObject as Exception);
            TaskScheduler.UnobservedTaskException += (_, args) => args.SetObserved();

            AppSettings.EnsureConfigsFolder();
            AccountService.Initialize();
            var settings = AppSettings.Load();
            LocalizationService.Initialize(settings.Language);
            ThemeApplier.Apply(settings.ThemeId);
            ThemeApplier.ApplyUiStyle(settings.UiStyle);

            // Пользовательский выключатель анимаций (null = включены).
            UiAnimation.Enabled = settings.EnableAnimations ?? true;

            // Наблюдатель твиков процессов, игровой оверлей и точный таймер 0.5 мс.
            ProcessPriorityWatcher.Refresh();
            MouseTweaksService.InitializeTimerResolutionAtStartup(settings.HighPrecisionTimer);
            OverlayService.InitializeAtStartup();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            MouseTweaksService.ReleaseTimerResolution();
            base.OnExit(e);
        }

        /// <summary>Показывает понятную ошибку вместо аварийного завершения приложения.</summary>
        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            e.Handled = true;
            WriteErrorLog(e.Exception);

            try
            {
                MessageBox.Show(
                    LocalizationService.T("Произошла непредвиденная ошибка: ") + e.Exception.Message,
                    LocalizationService.T("Ошибка"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            catch
            {
                // Сообщение показать не удалось — ошибка уже записана в журнал.
            }
        }

        /// <summary>Пишет текст ошибки в %LOCALAPPDATA%\Implexity\errors.log.</summary>
        private static void WriteErrorLog(Exception? ex)
        {
            if (ex == null)
                return;

            AppLogger.Error("Crash", "Unhandled exception", ex);

            try
            {
                var dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Implexity");
                Directory.CreateDirectory(dir);

                var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}{Environment.NewLine}";
                File.AppendAllText(Path.Combine(dir, "errors.log"), line);
            }
            catch
            {
                // журнал — вспомогательный механизм, его сбой не должен влиять на приложение
            }
        }
    }
}

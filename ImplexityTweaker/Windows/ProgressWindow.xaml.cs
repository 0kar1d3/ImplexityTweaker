using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using ImplexityTweaker.Services;

namespace ImplexityTweaker.Windows
{
    /// <summary>
    /// Окно модального/немодального прогресса с подписью текущего шага и процентами.
    /// Поддерживает режимы:
    ///  - <see cref="RunAsync"/>: пошаговый прогресс по списку действий (контекстное меню,
    ///    телеметрия, «первая настройка» и т.п.).
    ///  - <see cref="StartProgress"/> + <see cref="SetProgress"/>: прогресс, сообщаемый
    ///    из рабочего потока сервисом (чистка кэша, ОЗУ).
    ///  - <see cref="SetIndeterminate"/>: «бегущая» полоса для операций без точного процента
    ///    (CompactOS, DISM, SFC и т.п.).
    ///  - <see cref="CancellationToken"/>: позволяет пользователю прервать долгую операцию.
    /// </summary>
    public partial class ProgressWindow : Window
    {
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE_OLD = 19;
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        private bool _isClosed;
        private readonly CancellationTokenSource _cts = new();

        public bool IsClosed => _isClosed;

        /// <summary>True, если все шаги прошли без исключения.</summary>
        public bool ResultOk { get; private set; }

        /// <summary>Сообщение последней ошибки (null — без ошибок).</summary>
        public string? ResultError { get; private set; }

        /// <summary>True, если пользователь нажал «Отмена».</summary>
        public bool WasCancelled { get; private set; }

        /// <summary>Токен отмены для передачи в долгие операции.</summary>
        public CancellationToken CancellationToken => _cts.Token;

        public ProgressWindow(string title)
        {
            InitializeComponent();
            TitleText.Text = title;
            PercentText.Text = "0%";
            StepText.Text = "";
            Closed += (_, _) => _isClosed = true;
            SourceInitialized += (_, _) => SetDarkTitleBar();
        }

        /// <summary>
        /// Пошаговый режим: задаёт позицию (индекс шага / всего) и подпись.
        /// Индекс 0..total → 0..100%. Индекс total → 100%.
        /// </summary>
        public void Report(int currentIndex, int total, string? label = null)
        {
            int percent = total <= 0 ? 100 : (int)Math.Round((double)currentIndex / total * 100.0);
            Apply(percent, label);
        }

        /// <summary>
        /// Прямой режим: задаёт готовый процент 0..100 и подпись.
        /// </summary>
        public void SetProgress(int percent, string? label = null)
        {
            Apply(Math.Clamp(percent, 0, 100), label);
        }

        /// <summary>
        /// Включает/выключает «бегущую» полосу (marquee) для операций без точного процента.
        /// В indeterminate-режиме процент скрывается.
        /// </summary>
        public void SetIndeterminate(bool indeterminate, string? label = null)
        {
            if (_isClosed)
                return;

            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => SetIndeterminate(indeterminate, label));
                return;
            }

            Progress.IsIndeterminate = indeterminate;
            PercentText.Visibility = indeterminate ? Visibility.Collapsed : Visibility.Visible;
            if (!string.IsNullOrWhiteSpace(label))
            {
                StepText.Text = label;
                StepText.Visibility = Visibility.Visible;
            }
        }

        /// <summary>Показывает кнопку «Отмена» (для отменяемых операций).</summary>
        public void EnableCancel()
        {
            if (_isClosed)
                return;

            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(EnableCancel);
                return;
            }

            CancelButton.Visibility = Visibility.Visible;
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            WasCancelled = true;
            try { _cts.Cancel(); } catch { /* ignore */ }

            // Блокируем повторные нажатия и сообщаем пользователю, что идёт остановка.
            CancelButton.IsEnabled = false;
            StepText.Text = LocalizationService.T("Остановка…");
            StepText.Visibility = Visibility.Visible;
        }

        private void Apply(int percent, string? label)
        {
            if (_isClosed)
                return;

            // Сервисы часто сообщают прогресс из фонового потока — маршаллируем в UI.
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => Apply(percent, label));
                return;
            }

            Progress.IsIndeterminate = false;
            PercentText.Visibility = Visibility.Visible;
            Progress.Value = percent;
            PercentText.Text = percent + "%";
            StepText.Text = string.IsNullOrWhiteSpace(label) ? "" : label;
            StepText.Visibility = string.IsNullOrWhiteSpace(label) ? Visibility.Collapsed : Visibility.Visible;
        }

        private void SetDarkTitleBar()
        {
            try
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                if (hwnd == IntPtr.Zero)
                    return;

                var value = 1;
                foreach (var attr in new[] { DWMWA_USE_IMMERSIVE_DARK_MODE_OLD, DWMWA_USE_IMMERSIVE_DARK_MODE })
                    DwmSetWindowAttribute(hwnd, attr, ref value, sizeof(int));
            }
            catch
            {
                // DWM недоступен — остаётся системный заголовок.
            }
        }

        /// <summary>
        /// Показывает модальное окно и поочерёдно исполняет шаги, обновляя прогресс.
        /// Возвращает (Ok=true) при успешном завершении, (Ok=false, Error) при ошибке шага,
        /// (Ok=false, Error=null) при отмене пользователем (кнопка «Отмена»/«Закрыть»).
        /// </summary>
        public static Task<(bool Ok, string? Error)> RunAsync(
            Window? owner,
            string title,
            IReadOnlyList<(string Label, Func<Task> Step)> steps)
        {
            var window = new ProgressWindow(title) { Owner = owner };

            // Запускаем работу в фоне; ShowDialog создаёт модальный цикл сообщений,
            // в котором продолжения await-ов будут диспатчиться на UI‑поток.
            _ = RunStepsAsync(window, steps);
            window.ShowDialog();

            return Task.FromResult((window.ResultOk, window.ResultError));
        }

        private static async Task RunStepsAsync(ProgressWindow window, IReadOnlyList<(string Label, Func<Task> Step)> steps)
        {
            var total = Math.Max(1, steps.Count);
            try
            {
                for (int i = 0; i < steps.Count; i++)
                {
                    if (window.IsClosed || window.WasCancelled)
                        break;

                    window.Report(i, total, steps[i].Label);
                    await steps[i].Step();
                }

                if (!window.WasCancelled)
                {
                    window.Report(total, total, "");
                    window.ResultOk = true;
                }
            }
            catch (OperationCanceledException)
            {
                window.ResultOk = false;
                window.ResultError = null;
            }
            catch (Exception ex)
            {
                window.ResultOk = false;
                window.ResultError = ex.Message;
            }
            finally
            {
                try { window.Close(); } catch { }
            }
        }

        /// <summary>
        /// Немодальный режим для долгих сервисов, сообщающих прогресс через IProgress&lt;int&gt;.
        /// Вызывающий после работы обязан вызвать <see cref="Close"/> и/или ShowNotification.
        /// </summary>
        public static ProgressWindow StartProgress(Window? owner, string title)
        {
            var window = new ProgressWindow(title) { Owner = owner };
            window.Show();
            return window;
        }

        /// <summary>
        /// Модальный режим для одной долгой операции без точного процента (CompactOS, DISM, SFC).
        /// Показывает «бегущую» полосу и кнопку «Отмена». Возвращает (Ok, Error, Cancelled).
        /// </summary>
        /// <param name="work">Работа; получает токен отмены и окно для обновления подписи.</param>
        public static Task<(bool Ok, string? Error, bool Cancelled)> RunIndeterminateAsync(
            Window? owner,
            string title,
            string initialLabel,
            Func<ProgressWindow, CancellationToken, Task> work)
        {
            var window = new ProgressWindow(title) { Owner = owner };
            window.SetIndeterminate(true, initialLabel);
            window.EnableCancel();

            _ = RunIndeterminateCoreAsync(window, work);
            window.ShowDialog();

            return Task.FromResult((window.ResultOk, window.ResultError, window.WasCancelled));
        }

        private static async Task RunIndeterminateCoreAsync(
            ProgressWindow window,
            Func<ProgressWindow, CancellationToken, Task> work)
        {
            try
            {
                await work(window, window.CancellationToken);

                if (!window.WasCancelled)
                    window.ResultOk = true;
            }
            catch (OperationCanceledException)
            {
                window.ResultOk = false;
                window.ResultError = null;
            }
            catch (Exception ex)
            {
                window.ResultOk = false;
                window.ResultError = ex.Message;
            }
            finally
            {
                try { window.Close(); } catch { }
            }
        }
    }
}

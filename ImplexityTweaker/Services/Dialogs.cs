using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using ImplexityTweaker.Windows;

namespace ImplexityTweaker.Services;

public static class Dialogs
{
    /// <summary>Показывает информационное сообщение в стиле Fluent (текст и заголовок переводятся на текущий язык).</summary>
    public static Task ShowInfoAsync(string title, string message)
        => ShowAsync(title, message, DialogKind.Info);

    public static Task ShowErrorAsync(string title, string message)
        => ShowAsync(title, message, DialogKind.Error);

    public static Task<bool> ShowConfirmAsync(string title, string message)
        => ShowAsync(title, message, DialogKind.Confirm);

    private static Task<bool> ShowAsync(string title, string message, DialogKind kind)
    {
        var app = Application.Current;
        if (app == null)
            return Task.FromResult(false);

        if (!app.Dispatcher.CheckAccess())
            return app.Dispatcher.InvokeAsync(() => ShowCore(title, message, kind)).Task;

        return Task.FromResult(ShowCore(title, message, kind));
    }

    private static bool ShowCore(string title, string message, DialogKind kind)
    {
        var dlg = new MessageDialogWindow(
            LocalizationService.T(title),
            LocalizationService.T(message),
            kind);

        var owner = Application.Current?.Windows
            .OfType<Window>()
            .FirstOrDefault(w => w.IsVisible && w.IsActive)
            ?? (Application.Current?.MainWindow is { IsVisible: true } mw ? mw : null);

        if (owner != null && !ReferenceEquals(owner, dlg))
        {
            dlg.Owner = owner;
            dlg.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
        else
        {
            dlg.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        dlg.ShowDialog();
        return dlg.Confirmed;
    }
}

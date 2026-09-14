using System.Windows;

namespace ImplexityTweaker.Services;

public static class Dialogs
{
    /// <summary>Показывает информационное сообщение (текст и заголовок переводятся на текущий язык).</summary>
    public static Task ShowInfoAsync(string title, string message)
    {
        MessageBox.Show(
            LocalizationService.T(message),
            LocalizationService.T(title),
            MessageBoxButton.OK,
            MessageBoxImage.Information);
        return Task.CompletedTask;
    }

    public static Task ShowErrorAsync(string title, string message)
    {
        MessageBox.Show(
            LocalizationService.T(message),
            LocalizationService.T(title),
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        return Task.CompletedTask;
    }

    public static Task<bool> ShowConfirmAsync(string title, string message)
    {
        var result = MessageBox.Show(
            LocalizationService.T(message),
            LocalizationService.T(title),
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        return Task.FromResult(result == MessageBoxResult.Yes);
    }
}

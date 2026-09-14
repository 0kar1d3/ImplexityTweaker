using System.Windows.Markup;
using ImplexityTweaker.Services;

namespace ImplexityTweaker.Localization;

/// <summary>
/// Разметка-расширение для локализации XAML-строк:
/// Text="{loc:Loc 'Привет'}" — вернёт перевод на текущий язык.
/// </summary>
public sealed class LocExtension : MarkupExtension
{
    public LocExtension()
    {
    }

    public LocExtension(string text)
    {
        Text = text;
    }

    /// <summary>Русская строка-ключ (она же fallback).</summary>
    public string? Text { get; set; }

    public override object? ProvideValue(IServiceProvider serviceProvider)
        => LocalizationService.T(Text ?? string.Empty);
}

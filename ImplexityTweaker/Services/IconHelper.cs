using System;
using Wpf.Ui.Common;

namespace ImplexityTweaker.Services;

/// <summary>
/// Разбор имён иконок WPF-UI: каталоги хранят название символа строкой,
/// а привязки XAML ждут значение перечисления <see cref="SymbolRegular"/>.
/// </summary>
internal static class IconHelper
{
    /// <summary>Возвращает иконку по названию символа или нейтральную иконку-заглушку.</summary>
    public static SymbolRegular Parse(string? name)
        => !string.IsNullOrWhiteSpace(name) && Enum.TryParse<SymbolRegular>(name, true, out var symbol)
            ? symbol
            : SymbolRegular.Info24;
}

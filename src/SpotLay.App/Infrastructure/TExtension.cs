using System.Windows.Markup;
using SpotLay.Core.Localization;

namespace SpotLay.App.Infrastructure;

/// <summary>Texto traducido en XAML: <c>Text="{l:T 'Texto en español'}"</c>.</summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class TExtension(string text) : MarkupExtension
{
    public string Text { get; set; } = text;

    public override object ProvideValue(IServiceProvider serviceProvider) => Tr.T(Text);
}

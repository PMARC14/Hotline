using Microsoft.UI.Xaml.Markup;

namespace Hotline.App;

/// <summary>Interface text in XAML: <c>Text="{local:Loc Key=Composer_Send}"</c> (a key in Hotline.Core's Strings.resx).</summary>
[MarkupExtensionReturnType(ReturnType = typeof(string))]
public sealed partial class Loc : MarkupExtension
{
    public string Key { get; set; } = "";

    protected override object ProvideValue() => L.Get(Key);
}

using System.Windows.Data;
using System.Windows.Markup;

namespace Pauth40.Localization;

/// <summary>
/// XAML markup extension: <c>Text="{loc:Loc main_current_code}"</c>. It returns a
/// binding to the localization indexer, so every text updates automatically
/// when the language changes.
/// </summary>
[MarkupExtensionReturnType(typeof(object))]
public sealed class LocExtension : MarkupExtension
{
    public LocExtension()
    {
    }

    public LocExtension(string key)
    {
        Key = key;
    }

    [ConstructorArgument("key")]
    public string Key { get; set; } = "";

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var binding = new Binding($"[{Key}]")
        {
            Source = LocalizationManager.Instance,
            Mode = BindingMode.OneWay
        };

        return binding.ProvideValue(serviceProvider);
    }
}

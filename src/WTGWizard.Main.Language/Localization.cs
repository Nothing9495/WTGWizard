using System.Globalization;
using System.Resources;

namespace WTGWizard.Main.Language;

public static class Localization
{
    private static readonly ResourceManager _resourceManager = new("WTGWizard.Main.Language.Lang", typeof(Localization).Assembly);

    public static string GetString(string name)
    {
        return _resourceManager.GetString(name, CultureInfo.CurrentUICulture) ?? name;
    }

    public static string GetString(string name, CultureInfo culture)
    {
        return _resourceManager.GetString(name, culture) ?? name;
    }
}

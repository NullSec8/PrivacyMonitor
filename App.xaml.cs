using System.Windows;

namespace PrivacyMonitor;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private ResourceDictionary? _themeDictionary;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        SystemThemeDetector.WatchTheme();
        ApplySystemTheme();
        SystemThemeDetector.ThemeChanged += (_, _) => ApplySystemTheme();
    }

    private void ApplySystemTheme()
    {
        bool isDark = SystemThemeDetector.IsDarkMode;
        var uri = new System.Uri(
            $"pack://application:,,,/PrivacyMonitor;component/Themes/{(isDark ? "Dark" : "Light")}.xaml",
            System.UriKind.Absolute);

        if (_themeDictionary != null)
        {
            Resources.MergedDictionaries.Remove(_themeDictionary);
        }

        _themeDictionary = new ResourceDictionary { Source = uri };
        Resources.MergedDictionaries.Add(_themeDictionary);
    }
}


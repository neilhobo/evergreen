using System;
using System.ComponentModel;
using System.Windows;

namespace Globeweigh.Touch
{
    public enum WbTheme
    {
        Normal,
        Sunlight,
        Dark
    }

    /// <summary>
    /// Runtime theme switch for the outdoor weighbridge screens.
    /// Normal   = the app as originally styled (no override dictionary).
    /// Sunlight = SunlightResources.xaml (white surfaces, near-black text).
    /// Dark     = DarkResources.xaml (dark surfaces, white text — the on-screen
    ///            keyboard look the drivers said they can read).
    /// The active dictionary is merged last into the application resources so its keys
    /// shadow the defaults in CommonColours.xaml; everything themed via DynamicResource
    /// updates live. The choice persists per machine in HKCU\SOFTWARE\Globeweigh.
    /// </summary>
    public sealed class ThemeManager : INotifyPropertyChanged
    {
        private const string RegistryPath = @"SOFTWARE\Globeweigh";
        private const string RegistryValueName = "WbTheme";
        private const string LegacyRegistryValueName = "SunlightMode";

        private static readonly Uri SunlightUri =
            new Uri("pack://application:,,,/Globeweigh.Touch;component/Resources/SunlightResources.xaml");
        private static readonly Uri DarkUri =
            new Uri("pack://application:,,,/Globeweigh.Touch;component/Resources/DarkResources.xaml");

        public static ThemeManager Instance { get; } = new ThemeManager();

        private ResourceDictionary _sunlightDictionary;
        private ResourceDictionary _darkDictionary;
        private WbTheme _theme;

        private ThemeManager()
        {
        }

        public WbTheme Theme
        {
            get { return _theme; }
            set
            {
                if (_theme == value) return;
                _theme = value;
                Apply();
                Save();
                RaiseChanged();
            }
        }

        /// <summary>Checkbox binding: checking selects Sunlight, unchecking returns to Normal.</summary>
        public bool IsSunlight
        {
            get { return _theme == WbTheme.Sunlight; }
            set { Theme = value ? WbTheme.Sunlight : (_theme == WbTheme.Sunlight ? WbTheme.Normal : _theme); }
        }

        /// <summary>Checkbox binding: checking selects Dark, unchecking returns to Normal.</summary>
        public bool IsDark
        {
            get { return _theme == WbTheme.Dark; }
            set { Theme = value ? WbTheme.Dark : (_theme == WbTheme.Dark ? WbTheme.Normal : _theme); }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>Call once at startup (UI thread) to restore the persisted choice.</summary>
        public void Initialize()
        {
            _theme = LoadSaved();
            Apply();
            RaiseChanged();
        }

        private void RaiseChanged()
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Theme)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSunlight)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsDark)));
        }

        private void Apply()
        {
            var merged = Application.Current.Resources.MergedDictionaries;

            if (_sunlightDictionary != null) merged.Remove(_sunlightDictionary);
            if (_darkDictionary != null) merged.Remove(_darkDictionary);

            switch (_theme)
            {
                case WbTheme.Sunlight:
                    if (_sunlightDictionary == null) _sunlightDictionary = new ResourceDictionary { Source = SunlightUri };
                    merged.Add(_sunlightDictionary);
                    break;
                case WbTheme.Dark:
                    if (_darkDictionary == null) _darkDictionary = new ResourceDictionary { Source = DarkUri };
                    merged.Add(_darkDictionary);
                    break;
            }
        }

        private static WbTheme LoadSaved()
        {
            try
            {
                using (var regKey = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RegistryPath))
                {
                    if (regKey == null) return WbTheme.Normal;

                    var value = regKey.GetValue(RegistryValueName) as string;
                    if (value != null && Enum.TryParse(value, out WbTheme theme)) return theme;

                    // Fall back to the original boolean sunlight setting if present.
                    var legacy = regKey.GetValue(LegacyRegistryValueName);
                    if (legacy != null && Convert.ToBoolean(legacy.ToString())) return WbTheme.Sunlight;

                    return WbTheme.Normal;
                }
            }
            catch
            {
                return WbTheme.Normal;
            }
        }

        private void Save()
        {
            try
            {
                using (var regKey = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RegistryPath))
                {
                    regKey?.SetValue(RegistryValueName, _theme.ToString());
                }
            }
            catch
            {
                // Non-fatal: the theme still applies for this session.
            }
        }
    }
}

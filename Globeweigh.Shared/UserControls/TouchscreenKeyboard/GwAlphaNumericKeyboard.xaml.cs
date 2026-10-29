using System.Windows;

namespace Globeweigh.Shared.UI.TouchScreenKeyboard
{
    /// <summary>
    /// Interaction logic for GwAlphaNumericKeyboard.xaml
    /// </summary>
    public partial class GwAlphaNumericKeyboard : IToggleKeyboard
    {
        public static readonly DependencyProperty ShowHttpProperty = DependencyProperty.Register("ShowHttp", typeof(bool), typeof(GwAlphaNumericKeyboard), new PropertyMetadata(false));
        public static readonly DependencyProperty KeyFontSizeProperty = DependencyProperty.Register("KeyFontSize", typeof(double), typeof(GwAlphaNumericKeyboard), new UIPropertyMetadata(18.0));

        public bool ShowHttp
        {
            get { return (bool)GetValue(ShowHttpProperty); }
            set { SetValue(ShowHttpProperty, value); }
        }

        public double KeyFontSize
        {
            get { return (double)GetValue(KeyFontSizeProperty); }
            set { SetValue(KeyFontSizeProperty, value); }
        }

        public static IToggleKeyboard CurrentKeyboard;
        public GwAlphaNumericKeyboard()
        {
            InitializeComponent();
//            onScreenWebKeyboard.ShowHttp = ShowHttp;
//            onScreenWebKeyboard.KeyFontSize = KeyFontSize;
            onScreenWebKeyboard.BeginInit();
            Loaded += (s, e) => CurrentKeyboard = this;
            Unloaded += (s, e) => CurrentKeyboard = null;
            VisualStateManager.GoToState(this, "VisibleKeyboard", false);
        }

        public void ToggleKeyboard()
        {
         //   Visibility = Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
//            var fe = CurrentKeyboard as FrameworkElement;
//            if (fe == null) return;
//            var cs = VisualStateGroup.CurrentState;
//            var t = cs == null ? "HiddenKeyboard" : cs.Name;
//            VisualStateManager.GoToState(this, t == "HiddenKeyboard" ? "VisibleKeyboard" : "HiddenKeyboard", true);
        }
    }

    public interface IToggleKeyboard
    {
        void ToggleKeyboard();
    }
}

using System.Windows;
using System.Windows.Controls;

namespace Globeweigh.Admin.Controls
{
    public class MenuListBoxItem : ListBoxItem
    {
        public static readonly DependencyProperty ImageSourceProperty = DependencyProperty.Register("ImageSource", typeof(string), typeof(MenuListBoxItem));

        public string ImageSource
        {
            get { return (string)GetValue(ImageSourceProperty); }
            set { SetValue(ImageSourceProperty, value); }
        }

        public static readonly DependencyProperty SecondaryContentProperty = DependencyProperty.Register("SecondaryContent", typeof(string), typeof(MenuListBoxItem));

        public string SecondaryContent
        {
            get { return (string)GetValue(SecondaryContentProperty); }
            set { SetValue(SecondaryContentProperty, value); }
        }
    }
}

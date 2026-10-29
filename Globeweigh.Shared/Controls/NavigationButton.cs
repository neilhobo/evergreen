using System;
using System.Windows;
using System.Windows.Controls;

namespace Globeweigh.Shared.Controls
{
    public class NavigationButton : Button
    {
        public static readonly DependencyProperty ImageSourceProperty = DependencyProperty.Register("ImageSource", typeof(Uri), typeof(NavigationButton));

        public Uri ImageSource
        {
            get { return (Uri)GetValue(ImageSourceProperty); }
            set { SetValue(ImageSourceProperty, value); }
        }
    }
}

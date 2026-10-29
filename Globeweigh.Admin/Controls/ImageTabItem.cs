using System.Windows;
using System.Windows.Controls;

namespace Globeweigh.Admin.Controls
{
    public class ImageTabItem : TabItem
    {
        public static readonly DependencyProperty ImageSourceProperty = DependencyProperty.Register("ImageSource", typeof(string), typeof(ImageTabItem));

        public string ImageSource
        {
            get { return (string)GetValue(ImageSourceProperty); }
            set { SetValue(ImageSourceProperty, value); }
        }


    }
}

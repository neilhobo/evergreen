using System.Windows;
using System.Windows.Controls;

namespace Globeweigh.Shared.Controls
{
    public class TextBlockContentControl : ContentControl
    {
        public static readonly DependencyProperty TextProperty = DependencyProperty.Register("Text", typeof(string), typeof(TextBlockContentControl));

        public string Text
        {
            get { return (string)GetValue(TextProperty); }
            set { SetValue(TextProperty, value); }
        }

        public static readonly DependencyProperty TextBlockHorizontalAlignmentProperty = DependencyProperty.Register("TextBlockHorizontalAlignment", typeof(HorizontalAlignment), typeof(TextBlockContentControl));
        public HorizontalAlignment TextBlockHorizontalAlignment
        {
            get { return (HorizontalAlignment)GetValue(TextBlockHorizontalAlignmentProperty); }
            set { SetValue(TextBlockHorizontalAlignmentProperty, value); }
        }

        public static readonly DependencyProperty TextBlockVerticalAlignmentProperty = DependencyProperty.Register("TextBlockVerticalAlignment", typeof(VerticalAlignment), typeof(TextBlockContentControl));
        public VerticalAlignment TextBlockVerticalAlignment
        {
            get { return (VerticalAlignment)GetValue(TextBlockVerticalAlignmentProperty); }
            set { SetValue(TextBlockVerticalAlignmentProperty, value); }
        }

        public static readonly DependencyProperty TextWrappingProperty = DependencyProperty.Register("TextWrapping", typeof(TextWrapping), typeof(TextBlockContentControl));
        public TextWrapping TextWrapping
        {
            get { return (TextWrapping)GetValue(TextWrappingProperty); }
            set { SetValue(TextWrappingProperty, value); }
        }




    }
}

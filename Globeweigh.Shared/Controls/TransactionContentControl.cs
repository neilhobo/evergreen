using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Globeweigh.Shared.Controls
{
    public class TransactionContentControl : ContentControl
    {
        public static readonly DependencyProperty SelectedItemProperty = DependencyProperty.Register("SelectedItem", typeof(object), typeof(TransactionContentControl));
        public object SelectedItem
        {
            get { return (object)GetValue(SelectedItemProperty); }
            set { SetValue(SelectedItemProperty, value); }
        }


        public static readonly DependencyProperty TextProperty = DependencyProperty.Register("Text", typeof(string), typeof(TransactionContentControl));
        public string Text
        {
            get { return (string)GetValue(TextProperty); }
            set { SetValue(TextProperty, value); }
        }

        public static readonly DependencyProperty IsMandatoryProperty = DependencyProperty.Register("IsMandatory", typeof(bool), typeof(TransactionContentControl));
        public bool IsMandatory
        {
            get { return (bool)GetValue(IsMandatoryProperty); }
            set { SetValue(IsMandatoryProperty, value); }
        }

        public static readonly DependencyProperty HideLabelProperty = DependencyProperty.Register("HideLabel", typeof(bool), typeof(TransactionContentControl));
        public bool HideLabel
        {
            get { return (bool)GetValue(HideLabelProperty); }
            set { SetValue(HideLabelProperty, value); }
        }

        public static readonly DependencyProperty IsHorizontalProperty = DependencyProperty.Register("IsHorizontal", typeof(bool), typeof(TransactionContentControl));
        public bool IsHorizontal
        {
            get { return (bool)GetValue(IsHorizontalProperty); }
            set { SetValue(IsHorizontalProperty, value); }
        }

        public static readonly DependencyProperty HideBorderProperty = DependencyProperty.Register("HideBorder", typeof(bool), typeof(TransactionContentControl));
        public bool HideBorder
        {
            get { return (bool)GetValue(HideBorderProperty); }
            set { SetValue(HideBorderProperty, value); }
        }

        public static readonly DependencyProperty LabelTextProperty = DependencyProperty.Register("LabelText", typeof(string), typeof(TransactionContentControl));
        public string LabelText
        {
            get { return (string)GetValue(LabelTextProperty); }
            set { SetValue(LabelTextProperty, value); }
        }

        public static readonly DependencyProperty NullTextProperty = DependencyProperty.Register("NullText", typeof(string), typeof(TransactionContentControl));
        public string NullText
        {
            get { return (string)GetValue(NullTextProperty); }
            set { SetValue(NullTextProperty, value); }
        }


        public static readonly DependencyProperty LabelTextForegroundProperty = DependencyProperty.Register("LabelTextForeground", typeof(Brush), typeof(TransactionContentControl));
        public Brush LabelTextForeground
        {
            get { return (Brush)GetValue(LabelTextForegroundProperty); }
            set { SetValue(LabelTextForegroundProperty, value); }
        }

        public static readonly DependencyProperty FocusedBorderBrushProperty =DependencyProperty.Register("FocusedBorderBrush", typeof(Brush), typeof(TransactionContentControl));
        public Brush FocusedBorderBrush
        {
            get { return (Brush)GetValue(FocusedBorderBrushProperty); }
            set { SetValue(FocusedBorderBrushProperty, value); }
        }

        public static readonly DependencyProperty TextBlockHorizontalAlignmentProperty = DependencyProperty.Register("TextBlockHorizontalAlignment", typeof(HorizontalAlignment), typeof(TransactionContentControl));
        public HorizontalAlignment TextBlockHorizontalAlignment
        {
            get { return (HorizontalAlignment)GetValue(TextBlockHorizontalAlignmentProperty); }
            set { SetValue(TextBlockHorizontalAlignmentProperty, value); }
        }

        public static readonly DependencyProperty TextBlockVerticalAlignmentProperty = DependencyProperty.Register("TextBlockVerticalAlignment", typeof(VerticalAlignment), typeof(TransactionContentControl));
        public VerticalAlignment TextBlockVerticalAlignment
        {
            get { return (VerticalAlignment)GetValue(TextBlockVerticalAlignmentProperty); }
            set { SetValue(TextBlockVerticalAlignmentProperty, value); }
        }

        public static readonly DependencyProperty TextWrappingProperty = DependencyProperty.Register("TextWrapping", typeof(TextWrapping), typeof(TransactionContentControl));
        public TextWrapping TextWrapping
        {
            get { return (TextWrapping)GetValue(TextWrappingProperty); }
            set { SetValue(TextWrappingProperty, value); }
        }

        public static readonly DependencyProperty EditValueProperty = DependencyProperty.Register("EditValue", typeof(int), typeof(TransactionContentControl));
        public int EditValue
        {
            get { return (int)GetValue(EditValueProperty); }
            set { SetValue(EditValueProperty, value); }
        }

        public static readonly DependencyProperty TextDataFontSizeProperty = DependencyProperty.Register("TextDataFontSize", typeof(double), typeof(TransactionContentControl));
        public double TextDataFontSize
        {
            get { return (double)GetValue(TextDataFontSizeProperty); }
            set { SetValue(TextDataFontSizeProperty, value); }
        }

        public static readonly DependencyProperty LabelWidthProperty = DependencyProperty.Register("LabelWidth", typeof(GridLength), typeof(TransactionContentControl), new PropertyMetadata(GridLength.Auto));
        public GridLength LabelWidth
        {
            get { return (GridLength)GetValue(LabelWidthProperty); }
            set { SetValue(LabelWidthProperty, value); }
        }

        public static readonly DependencyProperty LabelTextFontSizeProperty = DependencyProperty.Register("LabelTextFontSize", typeof(double), typeof(TransactionContentControl));
        public double LabelTextFontSize
        {
            get { return (double)GetValue(LabelTextFontSizeProperty); }
            set { SetValue(LabelTextFontSizeProperty, value); }
        }

        public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register("ItemsSource", typeof(IEnumerable), typeof(TransactionContentControl));
        public IEnumerable ItemsSource
        {
            get { return (IEnumerable)GetValue(ItemsSourceProperty); }
            set { SetValue(ItemsSourceProperty, value); }
        }

        public static readonly DependencyProperty DisplayMemberProperty = DependencyProperty.Register("DisplayMember", typeof(string), typeof(TransactionContentControl));
        public string DisplayMember
        {
            get { return (string)GetValue(DisplayMemberProperty); }
            set { SetValue(DisplayMemberProperty, value); }
        }

        public static readonly DependencyProperty ValueMemberProperty = DependencyProperty.Register("ValueMember", typeof(string), typeof(TransactionContentControl));
        public string ValueMember
        {
            get { return (string)GetValue(ValueMemberProperty); }
            set { SetValue(ValueMemberProperty, value); }
        }


    }
}

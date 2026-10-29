using System;
using System.Windows;
using System.Windows.Controls;
using GalaSoft.MvvmLight.Command;

namespace Globeweigh.Touch.UserControls
{
    /// <summary>
    /// Interaction logic for DateFilter.xaml
    /// </summary>
    public partial class LoggedInUserControl : UserControl
    {
//        public static readonly DependencyProperty FilterDateProperty = DependencyProperty.Register("FilterDate", typeof(DateTime), typeof(DateFilter));
//        public DateTime FilterDate
//        {
//            get { return (DateTime)GetValue(FilterDateProperty); }
//            set { SetValue(FilterDateProperty, value); }
//        }
//
        public static readonly DependencyProperty FontSizeProperty = DependencyProperty.Register("FontSize", typeof(double), typeof(LoggedInUserControl));
        public double FontSize
        {
            get { return (double)GetValue(FontSizeProperty); }
            set { SetValue(FontSizeProperty, value); }
        }
//
//        public static readonly DependencyProperty FilterDateBackCommandProperty = DependencyProperty.Register("FilterDateBackCommand", typeof(RelayCommand), typeof(DateFilter));
//        public RelayCommand FilterDateBackCommand
//        {
//            get { return (RelayCommand)GetValue(FilterDateBackCommandProperty); }
//            set { SetValue(FilterDateBackCommandProperty, value); }
//        }
//
//        public static readonly DependencyProperty FilterDateForwardCommandProperty = DependencyProperty.Register("FilterDateForwardCommand", typeof(RelayCommand), typeof(DateFilter));
//        public RelayCommand FilterDateForwardCommand
//        {
//            get { return (RelayCommand)GetValue(FilterDateForwardCommandProperty); }
//            set { SetValue(FilterDateForwardCommandProperty, value); }
//        }

        public LoggedInUserControl()
        {
            InitializeComponent();
        }
    }
}

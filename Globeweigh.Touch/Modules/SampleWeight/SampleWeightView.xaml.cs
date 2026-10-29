using System;
using System.Windows;
using DevExpress.Xpf.Grid;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using DevExpress.Xpf.Core.Native;

namespace Globeweigh.Touch
{
    /// <summary>
    /// Interaction logic for PalletsView.xaml
    /// </summary>
    public partial class SampleWeightView : UserControl
    {
        public SampleWeightView()
        {
            InitializeComponent();
        }



        void GrdPallets_OnLoaded(object sender, RoutedEventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                ScrollBar scrollBar =
                    LayoutHelper.FindElementByName(sender as FrameworkElement, "PART_VerticalScrollBar") as ScrollBar;
                if (scrollBar != null) scrollBar.Width = 40;
            }));
        }
    }
}
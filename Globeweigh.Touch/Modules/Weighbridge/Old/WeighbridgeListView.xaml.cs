using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using DevExpress.Xpf.Core.Native;
using Globeweigh.Shared.Helpers;

namespace Globeweigh.Touch
{
    /// <summary>
    /// Interaction logic for PalletsView.xaml
    /// </summary>
    public partial class WeighbridgeListView : UserControl
    {
        public WeighbridgeListView()
        {
            InitializeComponent();

//            if (UtilitiesShared.IsMyMachine) batchButton.IsEnabled = true;
        }

        private void Grd_OnLoaded(object sender, RoutedEventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                ScrollBar scrollBar = LayoutHelper.FindElementByName(sender as FrameworkElement, "PART_VerticalScrollBar") as ScrollBar;
                scrollBar.Width = 40;
            }));
        }
    }
}

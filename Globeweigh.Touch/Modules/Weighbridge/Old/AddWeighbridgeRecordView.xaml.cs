using System;
using System.Windows;
using System.Windows.Controls.Primitives;
using DevExpress.Xpf.Core.Native;
using Globeweigh.Shared.Helpers;

namespace Globeweigh.Touch
{
    /// <summary>
    /// Interaction logic for Window1.xaml
    /// </summary>
    public partial class AddWeighbridgeRecordView
    {
        public AddWeighbridgeRecordView()
        {
            InitializeComponent();
//            WindowState = UtilitiesShared.IsMyMachine ? WindowState.Normal : WindowState.Maximized;
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

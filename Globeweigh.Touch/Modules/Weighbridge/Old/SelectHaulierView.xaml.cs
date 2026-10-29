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
    public partial class SelectHaulierView
    {
        public SelectHaulierView()
        {
            InitializeComponent();
        }

        private void GrdProducts_OnLoaded(object sender, RoutedEventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                ScrollBar scrollBar = LayoutHelper.FindElementByName(sender as FrameworkElement, "PART_VerticalScrollBar") as ScrollBar;
                scrollBar.Width = 40;
            }));
        }
    }
}

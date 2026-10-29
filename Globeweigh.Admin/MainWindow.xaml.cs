using System;
using System.Windows;
using System.Windows.Controls;
using DevExpress.Xpf.Core;
using Globeweigh.Shared;
using Globeweigh.Shared.Helpers;
using Globeweigh.Model;
using MahApps.Metro.Controls;

namespace Globeweigh.Admin
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : MetroWindow
    {
        public MainWindow()
        {
            InitializeComponent();
            if (UtilitiesShared.IsMyMachine)
                WindowState = WindowState.Normal;
            else
                WindowState = WindowState.Maximized;

        }

        private void FrameworkElement_OnLoaded(object sender, RoutedEventArgs e)
        {
            foreach (var item in list.Items)
            {
                if (((ListBoxItem)item).Visibility == Visibility.Visible)
                {
                    ((ListBoxItem)item).IsSelected = true;
                    return;
                }
            }

        }
    }
}

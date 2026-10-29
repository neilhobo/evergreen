using Globeweigh.Shared.Helpers;
using MahApps.Metro.Controls;
using Microsoft.Win32;
using System;
using System.Windows.Input;
using System.Windows;

namespace Globeweigh.Touch
{
    public partial class MainWindow : MetroWindow
    {

        private readonly bool _kioskMode;
        private bool _allowCloseWithAltF4;


        public MainWindow()
        {
            InitializeComponent();
            _kioskMode = !UtilitiesShared.IsMyMachine;

            if (!_kioskMode)
                SetDevWindowChrome();
        }

        private void MainWindow_OnLoaded(object sender, RoutedEventArgs e)
        {
            UtilitiesShared.MainWindow = this;

            if (_kioskMode)
                ForceFullscreen();   // one-time on load only
            // else do nothing; stays Normal on your machine
        }

        private void MainWindow_OnSourceInitialized(object sender, EventArgs e)
        {
            if (_kioskMode)
            {
                SystemEvents.DisplaySettingsChanged += SystemEvents_DisplaySettingsChanged;
                SystemEvents.UserPreferenceChanged += SystemEvents_UserPreferenceChanged;
            }
        }

        private void SystemEvents_DisplaySettingsChanged(object sender, EventArgs e)
        {
            if (_kioskMode) ForceFullscreen();
        }

        private void SystemEvents_UserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (_kioskMode &&
                (e.Category == UserPreferenceCategory.Window || e.Category == UserPreferenceCategory.General))
            {
                ForceFullscreen();
            }
        }

        private void MainWindow_OnStateChanged(object sender, EventArgs e)
        {
            if (_kioskMode && WindowState != WindowState.Maximized)
                ForceFullscreen();
        }

        private void MainWindow_OnLocationOrSizeChanged(object sender, EventArgs e)
        {
            if (!_kioskMode) return;

            double sw = SystemParameters.PrimaryScreenWidth;
            double sh = SystemParameters.PrimaryScreenHeight;
            const double tol = 1.0;

            if (Math.Abs(Left) > tol || Math.Abs(Top) > tol ||
                Width < sw - tol || Height < sh - tol ||
                WindowState != WindowState.Maximized)
            {
                ForceFullscreen();
            }
        }

        private void ForceFullscreen()
        {
            if (!_kioskMode) return; // critical: never maximize on your machine

            WindowStartupLocation = WindowStartupLocation.Manual;
            Topmost = true;
            Left = 0;
            Top = 0;

            ResizeMode = ResizeMode.NoResize;
            WindowStyle = WindowStyle.None;

            double sw = SystemParameters.PrimaryScreenWidth;
            double sh = SystemParameters.PrimaryScreenHeight;

            Width = sw;
            Height = sh;

            // toggle helps after display changes
            WindowState = WindowState.Normal;
            WindowState = WindowState.Maximized;
        }

        private void SetDevWindowChrome()
        {
            // make local dev pleasant and never kiosk-like
            Topmost = false;
            WindowStyle = WindowStyle.SingleBorderWindow;
            ResizeMode = ResizeMode.CanResize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            WindowState = WindowState.Normal;
        }

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            base.OnPreviewKeyDown(e);

            if (!_kioskMode)
                return;

            // Alt+F4 is usually reported via SystemKey
            bool isAltF4 =
                (e.Key == Key.F4 || e.SystemKey == Key.F4) &&
                (Keyboard.Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt;

            if (isAltF4)
            {
                _allowCloseWithAltF4 = true;
                Close();
            }
        }


    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (_kioskMode && !_allowCloseWithAltF4)
            {
                e.Cancel = true;
                return;
            }

            base.OnClosing(e);
        }

    }
}

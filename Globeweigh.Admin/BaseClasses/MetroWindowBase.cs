using System.Windows;
using DevExpress.XtraRichEdit.Model;
using Globeweigh.Shared.Helpers;
using MahApps.Metro.Controls;

namespace Globeweigh.Admin
{
    public class MetroWindowBase : MetroWindow
    {
        public static readonly DependencyProperty NoOpacityChangeProperty = DependencyProperty.Register("NoOpacityChange", typeof(bool), typeof(MetroWindowBase), new UIPropertyMetadata(false));
        public bool NoOpacityChange
        {
            get { return (bool)GetValue(NoOpacityChangeProperty); }
            set { SetValue(NoOpacityChangeProperty, value); }
        }

        public static readonly DependencyProperty IsOwnerSizeProperty = DependencyProperty.Register("IsOwnerSize", typeof(bool), typeof(MetroWindowBase), new UIPropertyMetadata(false));
        public bool IsOwnerSize
        {
            get { return (bool)GetValue(IsOwnerSizeProperty); }
            set { SetValue(IsOwnerSizeProperty, value); }
        }

        public MetroWindowBase()
        {
            Style style = this.FindResource("MetroWindowBaseStyle") as Style;
            this.Style = style;

            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            WindowState = WindowState.Normal;

            SourceInitialized += (s, e) =>
            {
                if (this.Owner == null) return;
                if (!NoOpacityChange) this.Owner.Opacity = 0.4;
                if (IsOwnerSize)
                {
                    if (this.Margin == new Thickness(0))
                    {
                        if (UtilitiesShared.IsMyMachine)
                        {
                            this.Width = this.Owner.Width - this.Margin.Left - this.Margin.Right;
                            this.Height = this.Owner.Height - this.Margin.Top - this.Margin.Bottom;
                            this.Left = this.Owner.Left + this.Margin.Left;
                            this.Top = this.Owner.Top + this.Margin.Top;
                        }
                        else
                        {
                            WindowState = WindowState.Maximized;
                        }
                    }
                    else
                    {
                        this.Width = UtilitiesShared.MainWindow.Width - this.Margin.Left - this.Margin.Right;
                        this.Height = UtilitiesShared.MainWindow.Height - this.Margin.Top - this.Margin.Bottom;
                        this.Left = UtilitiesShared.MainWindow.Left + this.Margin.Left;
                        this.Top = UtilitiesShared.MainWindow.Top + this.Margin.Top;
                    }
                }
            };
            Closed += (s, e) =>
            {
                if (this.Owner == null) return;
                this.Owner.Opacity = 1;
            };
        }
    }
}

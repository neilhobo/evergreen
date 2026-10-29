using System;
using System.Windows;

namespace Globeweigh.Shared
{
    /// <summary>
    /// Interaction logic for UserControl1.xaml
    /// </summary>
    public partial class frmError : Window
    {

        public frmError(string heading, Exception exception)
        {
            InitializeComponent();

            txtHeading.Text = heading ?? "Unkown Error";

            txtError.Text = "MESSAGE" + Environment.NewLine;
            txtError.Text += exception.Message;


            if (exception.InnerException != null)
            {
                txtError.Text += Environment.NewLine;
                txtError.Text += "MESSAGE" + Environment.NewLine;
                txtError.Text += exception.InnerException.ToString();
            }
        }


        public frmError(string heading, string errorMessage)
        {
            InitializeComponent();

            if (!string.IsNullOrEmpty(heading))
            {
                txtHeading.Text = heading;
            }
            txtError.Text = errorMessage;
        }

        private void ButtonBase_OnClick(object sender, RoutedEventArgs e)
        {
            this.DialogResult = true;
        }
    }
}

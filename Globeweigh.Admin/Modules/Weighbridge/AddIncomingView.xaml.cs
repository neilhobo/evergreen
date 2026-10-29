using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DevExpress.Xpf.Core.Native;
using DevExpress.Xpf.DocumentViewer;
using DevExpress.Xpf.Printing.PreviewControl.Native;

namespace Globeweigh.Admin
{
    /// <summary>
    /// Interaction logic for Window1.xaml
    /// </summary>
    public partial class AddIncomingView
    {
        public AddIncomingView()
        {
            InitializeComponent();
        }

        private void NumberValidationTextBox(object sender, TextCompositionEventArgs e)
        {
            Regex regex = new Regex("[^0-9]+");
            e.Handled = regex.IsMatch(e.Text);
        }
    }
}

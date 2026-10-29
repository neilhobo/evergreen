using System.Text.RegularExpressions;
using System.Windows.Input;

namespace Globeweigh.Admin
{
    /// <summary>
    /// Interaction logic for AddManualWeighbridgeView.xaml
    /// </summary>
    public partial class AddManualWeighbridgeView
    {
        public AddManualWeighbridgeView()
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

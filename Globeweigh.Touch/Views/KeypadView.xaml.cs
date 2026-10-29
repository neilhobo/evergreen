using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using DevExpress.Xpf.Core;
using DevExpress.Xpf.Editors;
using Globeweigh.Shared;

namespace Globeweigh.Touch
{
    /// <summary>
    /// Interaction logic for Window1.xaml
    /// </summary>
    public partial class KeypadView
    {
        public KeypadView()
        {
            InitializeComponent();
//            if (GlobalVariables.IsMyMachine)
//                WindowState = WindowState.Normal;
//            else
//                WindowState = WindowState.Maximized;
        }
    }
}

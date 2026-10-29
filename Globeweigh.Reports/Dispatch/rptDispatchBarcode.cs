using DevExpress.XtraReports.UI;
using System;
using System.Collections;
using System.ComponentModel;
using System.Drawing;
using Globeweigh.Model;

namespace Globeweigh.Reports.Dispatch
{
    public partial class rptDispatchBarcode : DevExpress.XtraReports.UI.XtraReport
    {
        public rptDispatchBarcode()
        {
            InitializeComponent();

        }

        public rptDispatchBarcode(int dispatchOrderId)
        {
            InitializeComponent();
            barcode.Text = dispatchOrderId + @"[" + (int)eBarcodeType.DispatchDocket + @"]";
        }

    }
}

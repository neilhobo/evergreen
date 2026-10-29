using System;
using System.Drawing;
using System.Collections;
using System.ComponentModel;
using System.Threading.Tasks;
using DevExpress.XtraReports.UI;
using GalaSoft.MvvmLight.Ioc;
using Globeweigh.Model;
using Globeweigh.Shared.Services;

namespace Globeweigh.Reports.Dispatch
{
    public partial class rptMultiplePalletLabelTest : DevExpress.XtraReports.UI.XtraReport
    {
        private readonly IDispatchOrderRepository _dispatchRepo = SimpleIoc.Default.GetInstance<IDispatchOrderRepository>();


        public rptMultiplePalletLabelTest(vwDispatchOrder dispatchOrder)
        {
            InitializeComponent();
            Task.Run(() => LoadData(dispatchOrder));
        }

        private async void LoadData(vwDispatchOrder dispatchOrder)
        {
            var order =await _dispatchRepo.GetDispatchOrder(dispatchOrder.id);
            // dispatchOrder.SignatureBytesTempOffice = order.DriverSignature;
            objectDataSource1.DataSource = dispatchOrder;
            var items = _dispatchRepo.GetDispatchOrderItemViews(dispatchOrder.id);
            objectDataSource3.DataSource = items;
            barcode.Text = dispatchOrder.id + @"[" + (int)eBarcodeType.DispatchDocket + @"]";

        }
    }
}

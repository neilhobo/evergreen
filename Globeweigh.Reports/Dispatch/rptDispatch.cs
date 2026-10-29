using DevExpress.XtraReports.UI;
using GalaSoft.MvvmLight.Ioc;
using Globeweigh.Model;
using Globeweigh.Shared.Services;

namespace Globeweigh.Reports.Dispatch
{
    public partial class rptDispatch :XtraReport
    {
        public rptDispatch()
        {
            
        }

        public rptDispatch(vwDispatchOrder dispatchOrder)
        {
            InitializeComponent();
            objectDataSource1.DataSource = dispatchOrder;
            objectDataSource3.DataSource = dispatchOrder.DispatchOrderItems;
        }


        
    }
}

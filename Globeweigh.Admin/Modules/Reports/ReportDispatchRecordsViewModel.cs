using System.Collections.Generic;
using Globeweigh.Shared;
using Globeweigh.Shared.Services;
using System.Windows;
using GalaSoft.MvvmLight.CommandWpf;
using Globeweigh.Model;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using DevExpress.Xpf.Core;
using DevExpress.Xpf.Grid;
using DevExpress.XtraReports.UI;
using GalaSoft.MvvmLight.Ioc;
using MvvmDialogs;

namespace Globeweigh.Admin
{
    public class ReportDispatchRecordsViewModel : AdminViewModelBase, IViewModel
    {
        #region private fields

        private readonly IReportRepository _reportRepo = SimpleIoc.Default.GetInstance<IReportRepository>();
        private readonly IDialogService _dialogService = SimpleIoc.Default.GetInstance<IDialogService>();
        private readonly ICustomerSupplierRepository _customerRepo = SimpleIoc.Default.GetInstance<ICustomerSupplierRepository>();

        #endregion

        #region Properties

        // private List<vwDispatchRecord> _DispatchRecordList;
        // public List<vwDispatchRecord> DispatchRecordList
        // {
        //     get { return _DispatchRecordList; }
        //     set { Set(ref _DispatchRecordList, value); }
        // }

        private DateTime _DateFrom;
        public DateTime DateFrom
        {
            get { return _DateFrom; }
            set { Set(ref _DateFrom, value); }
        }

        private DateTime _DateTo;
        public DateTime DateTo
        {
            get { return _DateTo; }
            set { Set(ref _DateTo, value); }
        }

        #endregion

        #region Commands

        public RelayCommand SearchCommand => new RelayCommand(OnRefresh);
        public RelayCommand<object> GenerateReportCommand => new RelayCommand<object>(OnGenerateReport);



        #endregion

        private async void OnGenerateReport(object param)
        {
            // if (DispatchRecordList == null || !DispatchRecordList.Any()) return;
            //
            //
            //
            // var tableView = (TableView)param;
            // List<vwDispatchRecord> finalList = new List<vwDispatchRecord>();
            // foreach (var item in tableView.DataControl.VisibleItems)
            // {
            //     var itemToAdd = (vwDispatchRecord) item;
            //     finalList.Add(itemToAdd);
            // }
            //
            //
            //
            // var report = new rptDispatchRecords(finalList);
            // report.ShowPrintMarginsWarning = false;
            // report.ShowPreviewDialog();
            return;
        }


        private async void OnRefresh()
        {
            await RefreshData();
        }


        public async void Load(FrameworkElement element)
        {
            DateFrom = DateTime.Today.AddMonths(-1);
            DateTo = DateTime.Today;
        }

        public async Task RefreshData()
        {
            // DispatchRecordList = new List<vwDispatchRecord>( await _reportRepo.GetDispatchRecordsData(DateFrom, DateTo));
        }

        public void Unload(FrameworkElement element)
        {
            // DispatchRecordList = null;
        }
    }
}

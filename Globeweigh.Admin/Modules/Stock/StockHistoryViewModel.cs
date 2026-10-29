using System.Collections.Generic;
using Globeweigh.Shared;
using Globeweigh.Shared.Services;
using System.Windows;
using GalaSoft.MvvmLight.CommandWpf;
using Globeweigh.Model;
using System;
using System.Threading.Tasks;
using System.Windows.Controls;
using DevExpress.Xpf.Grid;
using GalaSoft.MvvmLight.Ioc;
using Globeweigh.Admin.Helpers;
using Globeweigh.Model.Custom;
using MvvmDialogs;

namespace Globeweigh.Admin
{
    public class StockHistoryViewModel : AdminViewModelBase, IViewModel
    {
        #region private fields

        private readonly IStockRepository _stockRepo = SimpleIoc.Default.GetInstance<IStockRepository>();
//        private readonly IDialogService _dialogService = SimpleIoc.Default.GetInstance<IDialogService>();

        #endregion

        #region Properties

//        private List<vwStockHistory> _StockHistoryList;
//        public List<vwStockHistory> StockHistoryList
//        {
//            get { return _StockHistoryList; }
//            set { Set(ref _StockHistoryList, value); }
//        }

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

        public RelayCommand RefreshCommand => new RelayCommand(async ()=> await RefreshStock());

        #endregion


        private async Task RefreshStock()
        {
//            StockHistoryList = new List<vwStockHistory>(await _stockRepo.GetStockHistory(DateFrom.Date, DateTo.Date));
        }

        public async void Load(FrameworkElement element)
        {
            DateFrom = DateTime.Today.AddMonths(-1);
            DateTo = DateTime.Today.AddDays(1);
            await RefreshStock();
        }

        public void Unload(FrameworkElement element)
        {
//            StockHistoryList = null;
        }
    }
}

using Globeweigh.Shared;
using Globeweigh.Shared.Services;
using System.Windows;
using GalaSoft.MvvmLight.CommandWpf;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using GalaSoft.MvvmLight.Ioc;
using Globeweigh.Model;
using MvvmDialogs;

namespace Globeweigh.Admin
{
    public class ReportDispatchInStockViewModel : AdminViewModelBase, IViewModel
    {
        #region private fields

        private readonly IReportRepository _reportRepo = SimpleIoc.Default.GetInstance<IReportRepository>();
        private readonly IDialogService _dialogService = SimpleIoc.Default.GetInstance<IDialogService>();
        private readonly ICustomerSupplierRepository _customerRepo = SimpleIoc.Default.GetInstance<ICustomerSupplierRepository>();

        #endregion

        #region Properties

        // private List<vwDispatchStockReport> _DispatchStockList;
        // public List<vwDispatchStockReport> DispatchStockList
        // {
        //     get { return _DispatchStockList; }
        //     set { Set(ref _DispatchStockList, value); }
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

        private List<CustomerSupplier> _CustomerList;
        public List<CustomerSupplier> CustomerList
        {
            get { return _CustomerList; }
            private set { Set(ref _CustomerList, value); }
        }

        private CustomerSupplier _SelectedCustomer;
        public CustomerSupplier SelectedCustomer
        {
            get { return _SelectedCustomer; }
            set{Set(ref _SelectedCustomer, value);}
        }

        #endregion

        #region Commands

        public RelayCommand SearchCommand => new RelayCommand(OnRefresh, CanRefresh);

        private bool CanRefresh()
        {
            if (SelectedCustomer == null) return false;
            return true;
        }

        #endregion

        private async void OnRefresh()
        {
            await RefreshData();
        }


        public async void Load(FrameworkElement element)
        {
            DateFrom = DateTime.Today.AddMonths(-1);
            DateTo = DateTime.Today;

            CustomerList = new List<CustomerSupplier>(await _customerRepo.GetCustomersSuppliers((int)eRefDataType.Customer, false, false));
        }

        public async Task RefreshData()
        {
            // DispatchStockList = new List<vwDispatchStockReport>( await _reportRepo.GetDispatchStockReportData(DateFrom, DateTo, SelectedCustomer.id));
        }

        public void Unload(FrameworkElement element)
        {
            // DispatchStockList = null;
        }
    }
}

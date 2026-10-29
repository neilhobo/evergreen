using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using Globeweigh.Shared.Services;
using Globeweigh.Shared;
using System.Windows;
using GalaSoft.MvvmLight.CommandWpf;
using Globeweigh.Shared.Helpers;
using Globeweigh.Model;
using GalaSoft.MvvmLight.Ioc;
using IDialogService = MvvmDialogs.IDialogService;
using System.Threading.Tasks;
using System.Windows.Controls;
using Globeweigh.Model.Custom;
using DevExpress.Utils.About;

namespace Globeweigh.Admin
{
    public class CostingPriceListViewModel : AdminViewModelBase, IViewModel
    {
        #region private fields

        private readonly ICostingRepository _costingRepo = SimpleIoc.Default.GetInstance<ICostingRepository>();
        private readonly IProductRepository _productRepo = SimpleIoc.Default.GetInstance<IProductRepository>();
        private readonly IDialogService _dialogService = SimpleIoc.Default.GetInstance<IDialogService>();

        #endregion

        #region Properties

        private List<vwProduct> _ProductList;
        public List<vwProduct> ProductList
        {
            get { return _ProductList; }
            set { Set(ref _ProductList, value); }
        }

        private vwProduct _SelectedProduct;
        public vwProduct SelectedProduct
        {
            get { return _SelectedProduct; }
            set
            {
                Set(ref _SelectedProduct, value);
                SelectedProductChanged();
            }
        }

        private List<CostingZonePriceHistory> _HistoryList;
        public List<CostingZonePriceHistory> HistoryList
        {
            get { return _HistoryList; }
            set { Set(ref _HistoryList, value); }
        }

        private List<vwCostingZonePrice> _ZonePricingList;
        public List<vwCostingZonePrice> ZonePricingList
        {
            get { return _ZonePricingList; }
            set { Set(ref _ZonePricingList, value); }
        }

        private vwCostingZonePrice _SelectedZonePrice;
        public vwCostingZonePrice SelectedZonePrice
        {
            get { return _SelectedZonePrice; }
            set
            {
                Set(ref _SelectedZonePrice, value);
                RefreshHistory();
            }
        }


        #endregion

        #region Commands

        public RelayCommand EditZonePriceCommand => new RelayCommand(OnEditZonePrice);
        public RelayCommand<object> RowUpdatedCommand => new RelayCommand<object>(OnRowUpdated);

        #endregion

        private async void OnEditZonePrice()
        {
            var dialogViewModel = SimpleIoc.Default.GetInstanceWithoutCaching<EditZonePriceViewModel>();
            dialogViewModel.SelectedProduct = SelectedProduct;
            dialogViewModel.SelectedZonePrice = SelectedZonePrice;
            bool? success = _dialogService.ShowDialog<EditZonePriceView>(this, dialogViewModel);
            if (success == true)
            {
                await RefreshZonePricingList();
            }
        }

        private async void OnRowUpdated(object parameter)
        {
            // var result = await _productRepo.UpdateProductFromProductView((Product)parameter);
        }

        private async void SelectedProductChanged()
        {
            await RefreshZonePricingList();
            HistoryList = null;
        }

        private async Task RefreshZonePricingList()
        {
            if (SelectedProduct == null) return;
            ZonePricingList = new List<vwCostingZonePrice>(await _costingRepo.GetProductCostingZonesView(SelectedProduct.id));
            SelectedZonePrice = null;
        }

        private async Task RefreshProducts()
        {
            IsBusy = true;
            ProductList = new List<vwProduct>(await _productRepo.GetProductViewsAsync((int)eProductType.FinishedProduct, false));
            IsBusy = false;
        }


        private async Task RefreshHistory()
        {
            if(SelectedZonePrice == null)return;
            HistoryList = new List<CostingZonePriceHistory>(await _costingRepo.GetCostingZonePriceHistory(SelectedZonePrice.id));
        }

        public async void Load(FrameworkElement element)
        {
            if (UtilitiesShared.InDesignMode) return;
            await RefreshProducts();

        }

        public void Unload(FrameworkElement element)
        {
            ProductList = null;
            SelectedProduct = null;
            IsUnloading = false;
        }

    }
}

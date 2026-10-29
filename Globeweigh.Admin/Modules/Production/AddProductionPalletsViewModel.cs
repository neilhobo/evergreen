using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Globeweigh.Shared;
using Globeweigh.Shared.Services;
using MvvmDialogs;
using System.Windows;
using DevExpress.XtraReports.UI;
using GalaSoft.MvvmLight.CommandWpf;
using GalaSoft.MvvmLight.Ioc;
using Globeweigh.Model;
using Globeweigh.Shared.Helpers;

namespace Globeweigh.Admin
{
    public class AddProductionPalletsViewModel : DialogViewModelBase, IModalDialogViewModel, IViewModel
    {
        #region private fields

        private readonly IStockLocationRepository _stockLocationRepo = SimpleIoc.Default.GetInstance<IStockLocationRepository>();
        private readonly IPalletRepository _palletRepo = SimpleIoc.Default.GetInstance<IPalletRepository>();
        private readonly IDialogService _dialogService = SimpleIoc.Default.GetInstance<IDialogService>();
        private readonly ICustomerSupplierRepository _supplierRepo = SimpleIoc.Default.GetInstance<ICustomerSupplierRepository>();
        private static readonly IProductRepository _productRepo = SimpleIoc.Default.GetInstance<IProductRepository>();
        #endregion

        #region Properties

        public bool IsRepeatPallet { get; set; }

        private int _PalletId;
        public int PalletId
        {
            get { return _PalletId; }
            set { Set(ref _PalletId, value); }
        }

        private int? _NoOfPallets;
        public int? NoOfPallets
        {
            get { return _NoOfPallets; }
            set { Set(ref _NoOfPallets, value); }
        }

        private Pallet _CurrentPallet;
        public Pallet CurrentPallet
        {
            get { return _CurrentPallet; }
            set { Set(ref _CurrentPallet, value); }
        }

        private List<Product> _ProductList;
        public List<Product> ProductList
        {
            get { return _ProductList; }
            set { Set(ref _ProductList, value); }
        }

        private List<StockLocation> _LocationList;
        public List<StockLocation> LocationList
        {
            get { return _LocationList; }
            set { Set(ref _LocationList, value); }
        }
        //
        // private StockLocation _SelectedLocation;
        // public StockLocation SelectedLocation
        // {
        //     get { return _SelectedLocation; }
        //     set { Set(ref _SelectedLocation, value); }
        // }

        private Product _SelectedProduct;
        public Product SelectedProduct
        {
            get { return _SelectedProduct; }
            set
            {
                Set(ref _SelectedProduct, value);
                if(SelectedProduct == null)return;
                CurrentPallet.NoOfUnits = SelectedProduct.Upp;
            }
        }

        #endregion

        #region Commands

        public RelayCommand SaveCommand => new RelayCommand(OnSave, CanSave);

        #endregion

        private async void OnSave()
        {
            if (CurrentPallet.id == 0)
            {
                var palletAdded = await _palletRepo.AddPallets(CurrentPallet, (int)NoOfPallets);
            }
            else
            {
                await _palletRepo.AddUpdatePalletAsync(CurrentPallet);
            }

            DialogResult = true;
        }

        private bool CanSave()
        {
            if (CurrentPallet == null) return false;
            if (CurrentPallet.ProductId == 0) return false;
            if (CurrentPallet.id == 0)
            {
                if (CurrentPallet.LocationId == 0) return false;
                if (NoOfPallets == null || NoOfPallets <= 0) return false;
                if (CurrentPallet.NoOfUnits == null || CurrentPallet.NoOfUnits <= 0) return false;
            }

            return true;
        }

        public async void Load(FrameworkElement element)
        {
            DialogResult = null;
            ProductList = new List<Product>(await _productRepo.GetProductsAsync((int)eProductType.FinishedProduct));
            LocationList = new List<StockLocation>(await _stockLocationRepo.GetStockLocations());

            if (PalletId == 0)
            {
                CurrentPallet = new Pallet();
                CurrentPallet.PalletNo = _palletRepo.GetNextPalletNo();
                CurrentPallet.DateOfProduction = DateTime.Today;
                CurrentPallet.LocationId = 3;
            }
            else
            {
                CurrentPallet = await _palletRepo.GetPalletAsync(PalletId);
                SelectedProduct = ProductList.FirstOrDefault(a => a.id == CurrentPallet.ProductId);
                if (IsRepeatPallet)
                {
                    CurrentPallet.id = 0;
                    CurrentPallet.PalletNo = _palletRepo.GetNextPalletNo();
                    // CurrentPallet.LocationId = 0;
                }
                CurrentPallet.RaisePropertyChanges();
            }
            SaveCommand.RaiseCanExecuteChanged();


        }

        public void Unload(FrameworkElement element)
        {
            DialogResult = null;
            CurrentPallet = null;
            PalletId = 0;
            ProductList = null;
            // SelectedProduct = null;
            IsRepeatPallet = false;
            NoOfPallets = null;
        }
    }
}

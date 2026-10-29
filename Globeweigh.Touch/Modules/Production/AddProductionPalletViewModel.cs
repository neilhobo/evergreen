using System.Collections.Generic;
using Globeweigh.Shared;
using Globeweigh.Model;
using Globeweigh.Shared.Services;
using MvvmDialogs;
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using DevExpress.XtraReports.UI;
using GalaSoft.MvvmLight.CommandWpf;
using GalaSoft.MvvmLight.Ioc;
using Globeweigh.Shared.Helpers;

namespace Globeweigh.Touch
{
    public class AddProductionPalletViewModel : DialogViewModelBase, IModalDialogViewModel, IViewModel
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
        public DateTime? ProductionDate { get; set; }

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

        private List<vwFinishedProductWithStock> _ProductList;
        public List<vwFinishedProductWithStock> ProductList
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


        private vwFinishedProductWithStock _SelectedProduct;
        public vwFinishedProductWithStock SelectedProduct
        {
            get { return _SelectedProduct; }
            set
            {
                Set(ref _SelectedProduct, value);
                if(SelectedProduct == null)return;
                if(IsLoading)return;
                CurrentPallet.NoOfUnits = SelectedProduct.Upp;
            }
        }

        private bool _IsPrinting;
        public bool IsPrinting
        {
            get { return _IsPrinting; }
            private set { Set(ref _IsPrinting, value); }
        }

        private bool _CancelButtonEnabled;
        public bool CancelButtonEnabled
        {
            get { return _CancelButtonEnabled; }
            private set { Set(ref _CancelButtonEnabled, value); }
        }


        #endregion

        #region Commands

        public RelayCommand SaveCommand => new RelayCommand(OnSave, CanSave);
        //public RelayCommand CancelAllPrintJobsCommand => new RelayCommand(CancelAllPrintJobs);

        #endregion

        public async void MouseUpCommand(string name)
        {
            if (name == "Product")
            {
                // if (CurrentPallet.CustomerId == 0) return;
                var dialogViewModel = SimpleIoc.Default.GetInstance<SelectProductViewModel>();
                bool? success = _dialogService.ShowDialog<SelectProductView>(this, dialogViewModel);
                if (success == true)
                {
                    SelectedProduct = dialogViewModel.SelectedProduct;
                    CurrentPallet.ProductId = SelectedProduct.ProductId;
                }
            }
            else if (name == "DateOfProduction")
            {
                var dialogViewModel = SimpleIoc.Default.GetInstance<SelectDateViewModel>();
                dialogViewModel.HeaderText = "Select Production Date";
                bool? success = _dialogService.ShowDialog<SelectDateView>(this, dialogViewModel);
                if (success == true)
                {
                    CurrentPallet.DateOfProduction = dialogViewModel.SelectedDate;
                }
            }
            else if (name == "Location")
            {
                var dialogViewModel = SimpleIoc.Default.GetInstance<SelectRefDataViewModel>();
                dialogViewModel.HeaderText = "Select Location";
                var customerList = (from t in LocationList select new ReferenceData() { id = t.id, DisplayName = t.LocationName }).ToList();
                dialogViewModel.GenericRefDataList = new List<ReferenceData>(customerList);
                bool? success = _dialogService.ShowDialog<SelectRefDataView>(this, dialogViewModel);
                if (success == true)
                {
                    CurrentPallet.LocationId = dialogViewModel.SelectedRefData.id;
                    // SelectedLocation = LocationList.FirstOrDefault(a => a.id == CurrentPallet.LocationId);
                }
            }
            else if (name == "NoOfUnits")
            {
                var dialogViewModel = SimpleIoc.Default.GetInstance<KeypadViewModel>();
                dialogViewModel.HeaderText = "Enter No. Of Pallets";
                bool? success = _dialogService.ShowDialog<KeypadView>(this, dialogViewModel);
                if (success == true)
                {
                    CurrentPallet.NoOfUnits = Convert.ToInt32(dialogViewModel.KeypadValue);
                }
            }
            else if (name == "NoOfPallets")
            {
                var dialogViewModel = SimpleIoc.Default.GetInstance<KeypadViewModel>();
                dialogViewModel.HeaderText = "Enter No. Of Pallets";
                bool? success = _dialogService.ShowDialog<KeypadView>(this, dialogViewModel);
                if (success == true)
                {
                    NoOfPallets = Convert.ToInt32(dialogViewModel.KeypadValue);
                }
            }


            SaveCommand.RaiseCanExecuteChanged();
        }

        private async void OnSave()
        {
            if (CurrentPallet.id == 0)
            {
                var palletAdded = await _palletRepo.AddPallets(CurrentPallet, (int)NoOfPallets);
                // if (!SelectedLocation.NoLabelsRequired)
                // {
                //     IsPrinting = true;
                //
                //     await Task.Run(() => PrintPalletLabels(palletAdded));
                //     //await Task.Run(() => PrintStatus());
                //     IsPrinting = false;
                // }

            }
            else
            {
                await _palletRepo.AddUpdatePalletAsync(CurrentPallet);
            }

            DialogResult = true;
        }

        private async Task PrintPalletLabels(List<Pallet> palletsToPrint)
        {
            try
            {
                XtraReport multiPageReport = new XtraReport();

                foreach (var pallet in palletsToPrint)
                {

                    // var report = new rptIntakePalletLabel(pallet.id, pallet.PalletNo, SelectedProduct.ProductName);
                    // report.CreateDocument();
                    // multiPageReport.Pages.Add(report.Pages[0]);
                }


                using (ReportPrintTool printTool = new ReportPrintTool(multiPageReport))
                {
                    printTool.PrintingSystem.ShowMarginsWarning = false;
                    printTool.PrintingSystem.ShowPrintStatusDialog = false;
                    if (UtilitiesShared.IsMyMachine)
                    {
                        //printTool.Print();
                    }
                    else
                    {
                        printTool.Print("GlobeweighPrinter");
                    }
                    //await PrintStatus();
                }
            }
            catch (Exception ex)
            {
                //_ = DispatcherHelper.UIDispatcher.BeginInvoke(new Action(() =>
                //  {
                //      UtilitiesTouch.ShowOkCancelMessageView(this, ex.Message, true);
                //  }));
                IsPrinting = false;
                DialogResult = true;
            }
        }



        private bool CanSave()
        {
            if (CurrentPallet == null) return false;
            if (CurrentPallet.ProductId == 0) return false;
            if (CurrentPallet.LocationId == 0) return false;
            if (CurrentPallet.id == 0)
            {
                // if (NoOfPallets == null) return false;
                // if (NoOfPallets <= 0) return false;
            }

            return true;
        }

        public async void Load(FrameworkElement element)
        {
            IsLoading = true;
            DialogResult = null;
            ProductList = new List<vwFinishedProductWithStock>(await _productRepo.GetFinishedProductsWithStock());
            LocationList = new List<StockLocation>(await _stockLocationRepo.GetStockLocations());


            if (PalletId == 0)
            {
                CurrentPallet = new Pallet();
                CurrentPallet.PalletNo = _palletRepo.GetNextPalletNo();
                CurrentPallet.DateOfProduction =(DateTime) ProductionDate;
                CurrentPallet.LocationId = 3;
                // var defaultCustomer = CustomerList.FirstOrDefault(a => a.DefaultCustomer);
                // if (defaultCustomer != null) CurrentPallet.CustomerId = defaultCustomer.id;
            }
            else
            {
                CurrentPallet = await _palletRepo.GetPalletAsync(PalletId);
                SelectedProduct = ProductList.FirstOrDefault(a => a.ProductId == CurrentPallet.ProductId);
                if (IsRepeatPallet)
                {
                    CurrentPallet.id = 0;
                    CurrentPallet.PalletNo = _palletRepo.GetNextPalletNo();
                    CurrentPallet.LocationId = 0;
                }
                CurrentPallet.RaisePropertyChanges();
            }
            SaveCommand.RaiseCanExecuteChanged();

            CancelButtonEnabled = true;

            IsLoading = false;

        }

        public void Unload(FrameworkElement element)
        {
            DialogResult = null;
            ProductionDate = null;
            CurrentPallet = null;
            IsPrinting = false;
            PalletId = 0;
            ProductList = null;
            SelectedProduct = null;
            IsRepeatPallet = false;
            CancelButtonEnabled = true;
            NoOfPallets = null;

        }
    }
}

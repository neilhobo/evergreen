using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using MvvmDialogs;
using System.Windows;
using System.Windows.Threading;
using GalaSoft.MvvmLight.CommandWpf;
using Globeweigh.Model;
using GalaSoft.MvvmLight.Ioc;
using GalaSoft.MvvmLight.Threading;
using Globeweigh.Shared;
using Globeweigh.Shared.Helpers;
using Globeweigh.Shared.Services;

namespace Globeweigh.Admin
{
    public class EditWeighbrideRecordViewModel : DialogViewModelBase, IModalDialogViewModel, IViewModel
    {
        #region private fields

        private readonly IWeighbridgeRepository _weighbridgeRepo = SimpleIoc.Default.GetInstance<IWeighbridgeRepository>();
        private readonly IReferenceDataRepository _refDataRepo = SimpleIoc.Default.GetInstance<IReferenceDataRepository>();
        private readonly IHaulierRepository _haulierRepo = SimpleIoc.Default.GetInstance<IHaulierRepository>();
        private readonly IDialogService _dialogService = SimpleIoc.Default.GetInstance<IDialogService>();
        private readonly IProductRepository _productRepo = SimpleIoc.Default.GetInstance<IProductRepository>();
        private SerialPort _serialCom1;
        private DispatcherTimer _timer;
        #endregion

        #region Properties

        public int WeighbridgeRecordId { get; set; }

        private bool _IsOutgoing;
        public bool IsOutgoing
        {
            get { return _IsOutgoing; }
            set { Set(ref _IsOutgoing, value); }
        }

        private WeighbridgeRecord _CurrentWeighbridgeRecord;
        public WeighbridgeRecord CurrentWeighbridgeRecord
        {
            get { return _CurrentWeighbridgeRecord; }
            private set { Set(ref _CurrentWeighbridgeRecord, value); }
        }

        private List<Haulier> _HaulierList;
        public List<Haulier> HaulierList
        {
            get { return _HaulierList; }
            set { Set(ref _HaulierList, value); }
        }

        private Haulier _SelectedHaulier;
        public Haulier SelectedHaulier
        {
            get { return _SelectedHaulier; }
            set
            {
                Set(ref _SelectedHaulier, value);
            }
        }

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
                if (SelectedProduct == null) return;
                RefreshSupplierProductList();
            }
        }

        private List<vwProduct_Supplier> _ProductSupplierList;
        public List<vwProduct_Supplier> ProductSupplierList
        {
            get { return _ProductSupplierList; }
            private set { Set(ref _ProductSupplierList, value); }
        }

        private vwProduct_Supplier _SelectedProductSupplier;
        public vwProduct_Supplier SelectedProductSupplier
        {
            get { return _SelectedProductSupplier; }
            set
            {
                Set(ref _SelectedProductSupplier, value);
                if (SelectedProductSupplier == null) return; RefreshSupplierBogList();
            }
        }

        private List<Supplier_Bog> _BogList;
        public List<Supplier_Bog> BogList
        {
            get { return _BogList; }
            private set { Set(ref _BogList, value); }
        }


        #endregion

        #region Commands

        public RelayCommand EditSaveCommand => new RelayCommand(OnEditSave);
        public RelayCommand SaveCommand => new RelayCommand(OnSave, CanSave);

        #endregion

        private async Task RefreshProducts()
        {
            ProductList = new List<vwProduct>(await _productRepo.GetProductViewsAsync((int)eProductType.RawMaterial, false));
        }

        private async Task RefreshSupplierProductList()
        {
            ProductSupplierList = new List<vwProduct_Supplier>(await _productRepo.GetProductSupplierList(SelectedProduct.id));
        }

        private async Task RefreshSupplierBogList()
        {
            BogList = new List<Supplier_Bog>(await _productRepo.GetSupplier_BogList(SelectedProductSupplier.SupplierId));
        }

        private async void OnEditSave()
        {
            await _weighbridgeRepo.AddUpdateAsync(CurrentWeighbridgeRecord);
            DialogResult = true;
        }

        private async void OnSave()
        {
            // CurrentWeighbridgeRecord.HaulierName = SelectedHaulier.Name;
            await _weighbridgeRepo.AddUpdateAsync(CurrentWeighbridgeRecord);
            DialogResult = true;
        }

        private bool CanSave()
        {
            if (CurrentWeighbridgeRecord == null) return false;
            if (CurrentWeighbridgeRecord.HaulierId == 0) return false;
            if (SelectedHaulier == null) return false;
            return true;
        }

        public async void Load(FrameworkElement element)
        {
            if (UtilitiesShared.InDesignMode) return;
            HaulierList = new List<Haulier>(await _haulierRepo.GetHaulierList());
            CurrentWeighbridgeRecord = await _weighbridgeRepo.GetWeighbridgeRecord(WeighbridgeRecordId,  true, false);

            await RefreshProducts();
        }

        public void Unload(FrameworkElement element)
        {
            CurrentWeighbridgeRecord = null;
            WeighbridgeRecordId = 0;
            DialogResult = null;

            SelectedHaulier = null;
            IsOutgoing = false;
        }
    }
}

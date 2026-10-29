using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using GalaSoft.MvvmLight.CommandWpf;
using GalaSoft.MvvmLight.Ioc;
using Globeweigh.Model;
using Globeweigh.Shared;
using Globeweigh.Shared.Helpers;
using Globeweigh.Shared.Services;
using IDialogService = MvvmDialogs.IDialogService;
using IModalDialogViewModel = MvvmDialogs.IModalDialogViewModel;

namespace Globeweigh.Admin
{
    /// <summary>
    /// Creates a weighbridge record manually from the Weighbridge Records screen.
    /// Mirrors the Touch flow: choose Delivery/Collection, then (for Delivery) pick the
    /// haulier/product/supplier/bog like <c>DeliveryInViewModel</c>. Unlike the Touch
    /// flow the whole record is captured on one screen - the weigh-in and weigh-out
    /// weights and dates are entered manually. Collection is not implemented yet.
    /// </summary>
    public class AddManualWeighbridgeViewModel : DialogViewModelBase, IModalDialogViewModel, IViewModel
    {
        #region steps

        private const int StepChooseType = 0;
        private const int StepDetails = 1;

        private int _step = StepChooseType;
        private int Step
        {
            get { return _step; }
            set
            {
                _step = value;
                RaisePropertyChanged(() => IsChooseTypeStep);
                RaisePropertyChanged(() => IsDetailsStep);
            }
        }

        public bool IsChooseTypeStep => _step == StepChooseType;
        public bool IsDetailsStep => _step == StepDetails;

        #endregion

        #region private fields

        private readonly IWeighbridgeRepository _weighbridgeRepo = SimpleIoc.Default.GetInstance<IWeighbridgeRepository>();
        private readonly IHaulierRepository _haulierRepo = SimpleIoc.Default.GetInstance<IHaulierRepository>();
        private readonly IProductRepository _productRepo = SimpleIoc.Default.GetInstance<IProductRepository>();
        private readonly IDialogService _dialogService = SimpleIoc.Default.GetInstance<IDialogService>();

        #endregion

        #region Properties

        // When set (with WeighbridgeRecordId), the dialog opens straight on the details
        // screen to edit an existing record rather than create a new one.
        public bool IsEditMode { get; set; }
        public int WeighbridgeRecordId { get; set; }

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
            set { Set(ref _SelectedHaulier, value); }
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
                if (SelectedProductSupplier == null) return;
                RefreshSupplierBogList();
            }
        }

        private List<Supplier_Bog> _BogList;
        public List<Supplier_Bog> BogList
        {
            get { return _BogList; }
            private set { Set(ref _BogList, value); }
        }

        private Supplier_Bog _SelectedBog;
        public Supplier_Bog SelectedBog
        {
            get { return _SelectedBog; }
            set { Set(ref _SelectedBog, value); }
        }

        #endregion

        #region Commands

        public RelayCommand SelectDeliveryCommand => new RelayCommand(OnSelectDelivery);
        public RelayCommand SelectCollectionCommand => new RelayCommand(() => { }, () => false);
        public RelayCommand BackCommand => new RelayCommand(OnBack);
        public RelayCommand SaveCommand => new RelayCommand(OnSave, CanSave);

        #endregion

        #region list loading

        private async Task RefreshHauliers()
        {
            // New delivery -> intake hauliers only (mirrors Touch). Editing an existing
            // record -> the full list so the record's current haulier always resolves.
            HaulierList = IsEditMode
                ? new List<Haulier>(await _haulierRepo.GetHaulierList())
                : new List<Haulier>(await _haulierRepo.GetIntakeHaulierList());
        }

        private async Task RefreshProducts()
        {
            ProductList = new List<vwProduct>(await _productRepo.GetProductViewsAsync((int)eProductType.RawMaterial, false));
            SelectedProduct = null;
            SelectedProductSupplier = null;
            SelectedBog = null;
            ProductSupplierList = null;
            BogList = null;
        }

        private async Task RefreshSupplierProductList()
        {
            ProductSupplierList = new List<vwProduct_Supplier>(await _productRepo.GetProductSupplierList(SelectedProduct.id));
            SelectedProductSupplier = null;
            SelectedBog = null;
        }

        private async Task RefreshSupplierBogList()
        {
            BogList = new List<Supplier_Bog>(await _productRepo.GetSupplier_BogList(SelectedProductSupplier.SupplierId));
            SelectedBog = null;
        }

        #endregion

        #region command handlers

        private void OnSelectDelivery()
        {
            // A manual delivery is an incoming record, matching the Touch DeliveryIn flow.
            CurrentWeighbridgeRecord.IsOutgoing = false;
            Step = StepDetails;
        }

        private void OnBack()
        {
            if (_step == StepDetails) Step = StepChooseType;
        }

        private bool CanSave()
        {
            if (CurrentWeighbridgeRecord == null) return false;
            if (SelectedHaulier == null) return false;
            if (SelectedProduct == null) return false;
            if (ProductSupplierList != null && ProductSupplierList.Count > 0 && SelectedProductSupplier == null) return false;
            if (BogList != null && BogList.Count > 0 && SelectedBog == null) return false;
            // A weigh-in is the minimum needed to create the record.
            if (CurrentWeighbridgeRecord.DateWeightIn == null) return false;
            return true;
        }

        private async void OnSave()
        {
            // Copy the selections onto the record (mirrors DeliveryInViewModel.OnNext).
            if (SelectedHaulier != null)
            {
                CurrentWeighbridgeRecord.HaulierName = SelectedHaulier.Name;
                CurrentWeighbridgeRecord.HaulierId = SelectedHaulier.id;
            }
            if (SelectedProduct != null)
            {
                CurrentWeighbridgeRecord.Product = SelectedProduct.Description;
            }
            if (SelectedProductSupplier != null)
            {
                CurrentWeighbridgeRecord.Supplier = SelectedProductSupplier.Name;
            }
            if (SelectedBog != null)
            {
                CurrentWeighbridgeRecord.BogName = SelectedBog.Name;
            }

            // WeightIn/DateWeightIn/WeightOut/DateWeightOut are bound directly to the record.
            await _weighbridgeRepo.AddUpdateAsync(CurrentWeighbridgeRecord);
            DialogResult = true;
        }

        #endregion

        #region Lifecycle

        public async void Load(FrameworkElement element)
        {
            if (UtilitiesShared.InDesignMode) return;

            await RefreshHauliers();
            ProductList = new List<vwProduct>(await _productRepo.GetProductViewsAsync((int)eProductType.RawMaterial, false));

            if (IsEditMode && WeighbridgeRecordId != 0)
            {
                if (string.IsNullOrEmpty(HeaderText)) HeaderText = "Edit Record";
                CurrentWeighbridgeRecord = await _weighbridgeRepo.GetWeighbridgeRecord(WeighbridgeRecordId, true, false);
                await PopulateSelectionsForEdit();
                Step = StepDetails;   // skip the Delivery/Collection choice when editing
            }
            else
            {
                if (string.IsNullOrEmpty(HeaderText)) HeaderText = "Add Manual Record";
                CurrentWeighbridgeRecord = new WeighbridgeRecord();
                CurrentWeighbridgeRecord.DateWeightIn = DateTime.Now;
                CurrentWeighbridgeRecord.DateWeightOut = DateTime.Now;
                SelectedHaulier = null;
                Step = StepChooseType;
            }
        }

        /// <summary>
        /// Pre-selects the haulier/product/supplier/bog from the loaded record, awaiting
        /// each cascade so the dependent lists are populated before the next selection.
        /// The product/supplier/bog backing fields are set directly to avoid re-triggering
        /// the setter cascade (which would reset the downstream selections).
        /// </summary>
        private async Task PopulateSelectionsForEdit()
        {
            if (CurrentWeighbridgeRecord == null) return;

            SelectedHaulier = HaulierList?.FirstOrDefault(h => h.id == CurrentWeighbridgeRecord.HaulierId);

            var product = ProductList?.FirstOrDefault(p => string.Equals(p.Description, CurrentWeighbridgeRecord.Product));
            _SelectedProduct = product;
            RaisePropertyChanged(() => SelectedProduct);
            if (product == null) return;

            await RefreshSupplierProductList();
            var supplier = ProductSupplierList?.FirstOrDefault(s => string.Equals(s.Name, CurrentWeighbridgeRecord.Supplier));
            _SelectedProductSupplier = supplier;
            RaisePropertyChanged(() => SelectedProductSupplier);
            if (supplier == null) return;

            await RefreshSupplierBogList();
            var bog = BogList?.FirstOrDefault(b => string.Equals(b.Name, CurrentWeighbridgeRecord.BogName));
            _SelectedBog = bog;
            RaisePropertyChanged(() => SelectedBog);
        }

        public void Unload(FrameworkElement element)
        {
            CurrentWeighbridgeRecord = null;
            DialogResult = null;

            HaulierList = null;
            ProductList = null;
            ProductSupplierList = null;
            BogList = null;

            SelectedHaulier = null;
            SelectedProduct = null;
            SelectedProductSupplier = null;
            SelectedBog = null;
            Step = StepChooseType;

            IsEditMode = false;
            WeighbridgeRecordId = 0;
        }

        #endregion
    }
}

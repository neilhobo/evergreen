using System;
using GalaSoft.MvvmLight.CommandWpf;
using GalaSoft.MvvmLight.Ioc;
using Globeweigh.Model;
using Globeweigh.Shared;
using Globeweigh.Shared.Services;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using IDialogService = MvvmDialogs.IDialogService;

namespace Globeweigh.Touch
{
    public class DeliveryInViewModel : TouchViewModelBase, IViewModel
    {
        #region private fields


        private readonly IHaulierRepository _haulierRepo = SimpleIoc.Default.GetInstance<IHaulierRepository>();
        private readonly IProductRepository _productRepo = SimpleIoc.Default.GetInstance<IProductRepository>();
        private readonly IDialogService _dialogService = SimpleIoc.Default.GetInstance<IDialogService>();
        private readonly IReferenceDataRepository _refRepo = SimpleIoc.Default.GetInstance<IReferenceDataRepository>();
        private readonly IScaleService _scaleService;

        #endregion

        #region Properties

        private WeighbridgeRecord _CurrentWeighbridgeRecord;
        public WeighbridgeRecord CurrentWeighbridgeRecord
        {
            get { return _CurrentWeighbridgeRecord; }
            set { Set(ref _CurrentWeighbridgeRecord, value); }
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
                if (SelectedProductSupplier == null) return; RefreshSupplierBogList();
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

        private string _LiveWeight;
        public string LiveWeight
        {
            get { return _LiveWeight; }
            set { Set(ref _LiveWeight, value); }
        }

        #endregion

        #region Commands

        public RelayCommand BackCommand => new RelayCommand(() => SimpleIoc.Default.GetInstance<MainWindowViewModel>().CurrentViewModel = SimpleIoc.Default.GetInstance<WeighbridgeInViewModel>());
        public RelayCommand NextCommand => new RelayCommand(OnNext, CanNext);



        #endregion

        public DeliveryInViewModel(IScaleService scaleService)
        {
            _scaleService = scaleService;
            _scaleService.WeightChanged += OnWeightChanged;
        }

        private void OnWeightChanged(object sender, string weight)
        {
            LiveWeight = weight;
        }

        private async Task RefreshHaulierList()
        {
            HaulierList = new List<Haulier>(await _haulierRepo.GetIntakeHaulierList());
        }
        private bool CanNext()
        {
            if (SelectedHaulier == null) return false;
            if (SelectedProduct == null) return false;
            if (ProductSupplierList == null || ProductSupplierList.Any())
            {
                if (SelectedProductSupplier == null) return false;
            }
            if (BogList == null || BogList.Any())
            {
                if (SelectedBog == null) return false;
            }

            return true;
        }

        private void OnNext()
        {
            CurrentWeighbridgeRecord.HaulierName = SelectedHaulier.Name;
            CurrentWeighbridgeRecord.HaulierId = SelectedHaulier.id;
            CurrentWeighbridgeRecord.Product = SelectedProduct.Description;
            if (SelectedProductSupplier != null)
            {
                CurrentWeighbridgeRecord.Supplier = SelectedProductSupplier.Name;
            }
            if (SelectedBog != null)
            {
                CurrentWeighbridgeRecord.BogName = SelectedBog.Name;
            }


            var vm = SimpleIoc.Default.GetInstance<WeighbridgeWeighViewModel>();
            vm.CurrentWeighbridgeRecord = CurrentWeighbridgeRecord;
            SimpleIoc.Default.GetInstance<MainWindowViewModel>().CurrentViewModel = vm;
        }

        private async Task RefreshProducts()
        {
            ProductList = new List<vwProduct>(await _productRepo.GetProductViewsAsync((int)eProductType.RawMaterial, false));
            SelectedProduct = null;
            SelectedProductSupplier = null;
            SelectedBog= null;
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

        public async void Load(FrameworkElement element)
        {
            if (CurrentWeighbridgeRecord == null)
            {
                CurrentWeighbridgeRecord = new WeighbridgeRecord();
                await RefreshHaulierList();
                await RefreshProducts();
                SelectedHaulier = null;
                SelectedProduct = null;
                SelectedProductSupplier = null;
                SelectedBog = null;
                ProductSupplierList = null;
                BogList = null;
                SelectedProduct = null;
            }

            LiveWeight = _scaleService.CurrentWeight;
        }

        public void Unload(FrameworkElement element)
        {
            _scaleService.WeightChanged -= OnWeightChanged;
            // SelectedHaulier = null;
            // SelectedProduct = null;
            // SelectedProductSupplier = null;
            // SelectedBog = null;
            // ProductList = null;
            // ProductSupplierList = null;
            // BogList = null;
            // HaulierList = null;
            // SelectedProduct = null;
        }
    }
}

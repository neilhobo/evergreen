using System.Collections.Generic;
using Globeweigh.Shared.Services;
using Globeweigh.Shared;
using System.Windows;
using GalaSoft.MvvmLight.CommandWpf;
using Globeweigh.Shared.Helpers;
using Globeweigh.Model;
using GalaSoft.MvvmLight.Ioc;
using IDialogService = MvvmDialogs.IDialogService;
using System.Threading.Tasks;

namespace Globeweigh.Admin
{
    public class RawProductListViewModel : AdminViewModelBase, IViewModel
    {
        #region private fields

        private readonly IProductRepository _productRepo = SimpleIoc.Default.GetInstance<IProductRepository>();
        private readonly IDialogService _dialogService = SimpleIoc.Default.GetInstance<IDialogService>();
        private readonly IReferenceDataRepository _refRepo = SimpleIoc.Default.GetInstance<IReferenceDataRepository>();


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
                if(SelectedProductSupplier == null) return; RefreshSupplierBogList();
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
            set
            {
                Set(ref _SelectedBog, value);
            }
        }

        private bool _ShowDeletes;
        public bool ShowDeletes
        {
            get { return _ShowDeletes; }
            set
            {
                Set(ref _ShowDeletes, value);
                if (IsUnloading) return;
                Task.Run(async () => await RefreshProducts());
            }
        }


        #endregion

        #region Commands

        public RelayCommand AddProductCommand => new RelayCommand(OnAddProduct);
        public RelayCommand EditProductCommand => new RelayCommand(OnEditProduct);
        public RelayCommand DeleteProductCommand => new RelayCommand(OnDeleteProduct);
        public RelayCommand AddProductSupplierCommand => new RelayCommand(OnAddProductSupplier);
        public RelayCommand DeleteProductSupplierCommand => new RelayCommand(OnDeleteProductSupplier);
        public RelayCommand AddBogCommand => new RelayCommand(OnAddBog);
        public RelayCommand EditCommand => new RelayCommand(OnEditBog);
        public RelayCommand DeleteBogCommand => new RelayCommand(OnDeleteBog);

        #endregion

        private async void OnAddBog()
        {
            var dialogViewModel = SimpleIoc.Default.GetInstanceWithoutCaching<AddBogViewModel>();
            dialogViewModel.SupplierId = SelectedProductSupplier.SupplierId;
            bool? success = _dialogService.ShowDialog<AddBogView>(this, dialogViewModel);
            if (success == true)
            {

                await RefreshSupplierBogList();
            }
        }

        private async void OnEditBog()
        {
            if(SelectedBog == null)return;
            var dialogViewModel = SimpleIoc.Default.GetInstanceWithoutCaching<AddBogViewModel>();
            dialogViewModel.SupplierBogId = SelectedBog.id;
            bool? success = _dialogService.ShowDialog<AddBogView>(this, dialogViewModel);
            if (success == true)
            {

                await RefreshSupplierBogList();
            }
        }

        private async void OnDeleteBog()
        {
            if (SelectedBog == null) return;
            var result = _dialogService.ShowMessageBox(this, "Delete Bog?", "", MessageBoxButton.OKCancel);
            if (result == MessageBoxResult.Cancel) return;

            await _productRepo.RemoveSupplier_Bog(SelectedBog.id);
            await RefreshSupplierBogList();

        }

        private async void OnAddProductSupplier()
        {
            var dialogViewModel = SimpleIoc.Default.GetInstanceWithoutCaching<AddRawSupplierViewModel>();
            dialogViewModel.SelectedProduct = SelectedProduct;
            bool? success = _dialogService.ShowDialog<AddRawSupplierView>(this, dialogViewModel);
            if (success == true)
            {

                await RefreshSupplierProductList();
            }
        }

        private async void OnDeleteProductSupplier()
        {
            if (SelectedProductSupplier == null) return;
            if (ShowDeletes)
            {
                var result = _dialogService.ShowMessageBox(this, "Restore Supplier?", "", MessageBoxButton.OKCancel);
                if (result == MessageBoxResult.Cancel) return;
            }
            else
            {

                var result = _dialogService.ShowMessageBox(this, "Remove Supplier?", "", MessageBoxButton.OKCancel);
                if (result == MessageBoxResult.Cancel) return;
            }

            await _productRepo.RemoveProduct_Supplier(SelectedProductSupplier.id);
            await RefreshSupplierProductList();

        }

        private async void OnAddProduct()
        {
            var dialogViewModel = SimpleIoc.Default.GetInstanceWithoutCaching<AddRawProductViewModel>();
            dialogViewModel.ProductId = 0;
            dialogViewModel.ProductTypeId = (int)eProductType.RawMaterial;
            dialogViewModel.HeaderText = "Add New Product";
            bool? success = _dialogService.ShowDialog<AddRawProductView>(this, dialogViewModel);
            if (success == true)
            {
                SelectedProduct = null;
                await RefreshProducts();
            }
        }

        private async void OnEditProduct()
        {
            if (SelectedProduct == null) return;
            var dialogViewModel = SimpleIoc.Default.GetInstanceWithoutCaching<AddRawProductViewModel>();
            dialogViewModel.ProductId = SelectedProduct.id;
            dialogViewModel.ProductTypeId = (int)eProductType.RawMaterial;
            dialogViewModel.HeaderText = "Add New Product";
            bool? success = _dialogService.ShowDialog<AddRawProductView>(this, dialogViewModel);
            if (success == true)
            {
                SelectedProduct = null;
                await RefreshProducts();
            }
        }

        private async void OnDeleteProduct()
        {
            if (ShowDeletes)
            {
                var result = _dialogService.ShowMessageBox(this, "Restore Product?", "", MessageBoxButton.OKCancel);
                if (result == MessageBoxResult.Cancel) return;
            }
            else
            {

                var result = _dialogService.ShowMessageBox(this, "Delete Product?", "", MessageBoxButton.OKCancel);
                if (result == MessageBoxResult.Cancel) return;
            }

            await _productRepo.MarkAsDeletedAsync(SelectedProduct.id);
            await RefreshProducts();
            SelectedProduct = null;
        }

        private async Task RefreshProducts()
        {
            ProductList = new List<vwProduct>(await _productRepo.GetProductViewsAsync((int)eProductType.RawMaterial, ShowDeletes));
        }

        private async Task RefreshSupplierProductList()
        {
            ProductSupplierList = new List<vwProduct_Supplier>(await _productRepo.GetProductSupplierList(SelectedProduct.id));
            SelectedProductSupplier = null;
        }

        private async Task RefreshSupplierBogList()
        {
            BogList = new List<Supplier_Bog>(await _productRepo.GetSupplier_BogList(SelectedProductSupplier.SupplierId));
            SelectedBog = null;
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
            ShowDeletes = false;
            IsUnloading = false;
        }

    }
}

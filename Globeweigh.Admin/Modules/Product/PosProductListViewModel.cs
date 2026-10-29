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
    public class PosProductListViewModel : AdminViewModelBase, IViewModel
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
            set { Set(ref _SelectedProduct, value); }
        }

        private List<ReferenceData> _UnitOfMeasureList;
        public List<ReferenceData> UnitOfMeasureList
        {
            get { return _UnitOfMeasureList; }
            private set { Set(ref _UnitOfMeasureList, value); }
        }

        private bool _ShowDeletes;
        public bool ShowDeletes
        {
            get { return _ShowDeletes; }
            set
            {
                Set(ref _ShowDeletes, value);
                if(IsUnloading)return;
                Task.Run(async () => await RefreshProducts());
            }
        }

        // private List<ProductType> _ProductTypeList;
        // public List<ProductType> ProductTypeList
        // {
        //     get { return _ProductTypeList; }
        //     set { Set(ref _ProductTypeList, value); }
        // }
        //
        // private ProductType _SelectedProductType;
        // public ProductType SelectedProductType
        // {
        //     get { return _SelectedProductType; }
        //     set
        //     {
        //         Set(ref _SelectedProductType, value);
        //         if(SelectedProductType == null)return;
        //         RefreshProducts();
        //     }
        // }

        #endregion

        #region Commands

        public RelayCommand AddCommand => new RelayCommand(OnAdd);
        public RelayCommand EditCommand => new RelayCommand(OnEdit);
        public RelayCommand DeleteCommand => new RelayCommand(OnDelete);
        public RelayCommand<object> RowUpdatedCommand => new RelayCommand<object>(OnRowUpdated);

        #endregion

        private async void OnRowUpdated(object parameter)
        {
            // var result = await _productRepo.UpdateProductFromProductView((Product)parameter);
        }

        private void OnAdd()
        {
            AddEditUserDialog(0);
        }

        private void OnEdit()
        {
            if(SelectedProduct == null)return;
            AddEditUserDialog(SelectedProduct.id);
        }

        private async void OnDelete()
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

        private async void AddEditUserDialog(int productId)
        {
            var dialogViewModel = SimpleIoc.Default.GetInstanceWithoutCaching<AddEditProductViewModel>();
            dialogViewModel.ProductId = productId;
            dialogViewModel.ProductTypeId = (int)eProductType.Pos;
            if (productId == 0) dialogViewModel.HeaderText = "Add New Product";
            else dialogViewModel.HeaderText = "Edit Product";
            bool? success = _dialogService.ShowDialog<AddEditProductView>(this, dialogViewModel);
            if (success == true)
            {
                SelectedProduct = null;
                await RefreshProducts();
            }
        }

        private async Task RefreshProducts()
        {
            ProductList = new List<vwProduct>(await _productRepo.GetProductViewsAsync((int)eProductType.Pos, ShowDeletes));
        }

        public async void Load(FrameworkElement element)
        {
            if (UtilitiesShared.InDesignMode) return;
            await RefreshProducts();
            // ProductTypeList = new List<ProductType>(await _productRepo.GetProductTypeList());
            // SelectedProductType = ProductTypeList.FirstOrDefault();

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

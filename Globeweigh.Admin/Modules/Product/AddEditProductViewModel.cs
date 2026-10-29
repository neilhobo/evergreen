using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Threading.Tasks;
using Globeweigh.Shared;
using Globeweigh.Shared.Services;
using MvvmDialogs;
using System.Windows;
using GalaSoft.MvvmLight.CommandWpf;
using GalaSoft.MvvmLight.Ioc;
using Globeweigh.Model;

namespace Globeweigh.Admin
{
    public class AddEditProductViewModel : DialogViewModelBase, IModalDialogViewModel, IViewModel
    {
        #region private fields

        private readonly IDialogService _dialogService = SimpleIoc.Default.GetInstance<IDialogService>();
        private readonly ICustomerSupplierRepository _customerSupplierRepo = SimpleIoc.Default.GetInstance<ICustomerSupplierRepository>();
        private readonly IProductRepository _ProductRepo = SimpleIoc.Default.GetInstance<IProductRepository>();
        private readonly IReferenceDataRepository _refDataRepo = SimpleIoc.Default.GetInstance<IReferenceDataRepository>();
        private GlobeweighEntities _context = new GlobeweighEntities(GlobalVariables.ConnectionString);

        #endregion

        #region Properties

        public int ProductId { get; set; }
        public int ProductTypeId { get; set; }

        private Product _CurrentProduct;
        public Product CurrentProduct
        {
            get { return _CurrentProduct; }
            private set { Set(ref _CurrentProduct, value); }
        }
        //
        // private ProductType _SelectedProductType;
        // public ProductType SelectedProductType
        // {
        //     get { return _SelectedProductType; }
        //     set
        //     {
        //         Set(ref _SelectedProductType, value);
        //         if(SelectedProductType == null)return;
        //         ProductTypeChanged();
        //     }
        // }

        private List<ProductType> _ProductTypeList;
        public List<ProductType> ProductTypeList
        {
            get { return _ProductTypeList; }
            private set { Set(ref _ProductTypeList, value); }
        }

        private List<Product> _PosProductList;
        public List<Product> PosProductList
        {
            get { return _PosProductList; }
            set { Set(ref _PosProductList, value); }
        }

        private List<ReferenceData> _CategoryList;
        public List<ReferenceData> CategoryList
        {
            get { return _CategoryList; }
            private set { Set(ref _CategoryList, value); }
        }

        private bool _CodeAlreadyExists;
        public bool CodeAlreadyExists
        {
            get { return _CodeAlreadyExists; }
            set { Set(ref _CodeAlreadyExists, value); }
        }



        #endregion

        #region Commands

        public RelayCommand SaveCommand => new RelayCommand(OnSave, CanSave);

        #endregion


        public async void Load(FrameworkElement element)
        {
            CategoryList = new List<ReferenceData>(await _refDataRepo.GetReferenceData(eRefDataType.FinishedProductCategory));
            ProductTypeList = new List<ProductType>(await _ProductRepo.GetProductTypeList());
            PosProductList = new List<Product>(await _ProductRepo.GetProductsAsync((int)eProductType.Pos, false));
            if (ProductId == 0)
            {
                CurrentProduct = new Product();
                CurrentProduct.ProductTypeId = ProductTypeId;
                CurrentProduct.RaisePropertiesChangedManual();
            }
            else
            {
                CurrentProduct = await _ProductRepo.GetProductAsync(_context, ProductId);
                CurrentProduct.RaisePropertiesChangedManual();
            }

            CurrentProduct.PropertyChanged += CurrentProduct_PropertyChanged;
        }

        private async void CurrentProduct_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            HasChanges = true;
            if (e.PropertyName.Equals("ProductTypeId"))
            {
                if (ProductTypeId != (int)eProductType.FinishedProduct)
                {
                    CurrentProduct.PosId = null;
                    CurrentProduct.CategoryId = 0;
                    CurrentProduct.Size = 0;
                    CurrentProduct.Upp = 0;
                }
                CurrentProduct.RaisePropertiesChangedManual();
            }
        }

        private async void OnSave()
        {
            await _ProductRepo.AddUpdateProductAsync(_context, CurrentProduct);
            DialogResult = true;
        }

        private bool CanSave()
        {
            if (CurrentProduct == null) return false;
            if (CodeAlreadyExists) return false;
            if (CurrentProduct.ProductTypeId == 0) return false;
            if (string.IsNullOrEmpty(CurrentProduct.Description)) return false;
            if (CurrentProduct.ProductTypeId == (int) eProductType.FinishedProduct)
            {
                if (CurrentProduct.CategoryId == 0) return false;
            }
            return true;
        }

        public override RelayCommand CancelCommand
        {
            get
            {
                return _CancelCommand ?? (_CancelCommand = new RelayCommand(() =>
                {
                    if (HasChanges)
                    {
                        var dlgRes = MessageBox.Show("Exit without saving changes?", "", MessageBoxButton.OKCancel);
                        if (dlgRes == MessageBoxResult.Cancel) return;
                    }
                    DialogResult = false;
                }));
            }
        }

        public void Unload(FrameworkElement element)
        {
            if (CurrentProduct != null) CurrentProduct.PropertyChanged -= CurrentProduct_PropertyChanged;
            CodeAlreadyExists = false;

            CurrentProduct = null;
            ProductId = 0;
            _context = null;
            DialogResult = null;

        }
    }
}

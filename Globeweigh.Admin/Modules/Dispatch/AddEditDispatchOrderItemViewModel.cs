using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
using Globeweigh.Model.Custom;

namespace Globeweigh.Admin
{
    public class AddEditDispatchOrderItemViewModel : DialogViewModelBase, IModalDialogViewModel, IViewModel
    {
        #region private fields

        private readonly ICostingRepository _costingRepo = SimpleIoc.Default.GetInstance<ICostingRepository>();
        private readonly IProductRepository _productRepo = SimpleIoc.Default.GetInstance<IProductRepository>();
        private DispatchOrderItem _originalObject;
        private List<vwFinishedProductWithStock> _allProducts;
        private List<CostingZonePrice> _costingZonePriceList;

        #endregion

        #region Properties

        public int CostingZoneId { get; set; }
        public GlobeweighEntities Context { get; set; }
        public List<DispatchOrderItem> AlreadySelectedItems{ get; set; }

        private bool _InEditMode;
        public bool InEditMode
        {
            get { return _InEditMode; }
            set { Set(ref _InEditMode, value); }
        }

        private bool _PosChecked;
        public bool PosChecked
        {
            get { return _PosChecked; }
            set { Set(ref _PosChecked, value); }
        }

        private string _CostingZone;
        public string CostingZone
        {
            get { return _CostingZone; }
            set { Set(ref _CostingZone, value); }
        }

        private DispatchOrderItem _CurrentDispatchOrderItem;
        public DispatchOrderItem CurrentDispatchOrderItem
        {
            get { return _CurrentDispatchOrderItem; }
            set { Set(ref _CurrentDispatchOrderItem, value); }
        }

        private List<vwFinishedProductWithStock> _ProductList;
        public List<vwFinishedProductWithStock> ProductList
        {
            get { return _ProductList; }
            set { Set(ref _ProductList, value); }
        }

        private vwFinishedProductWithStock _SelectedProduct;
        public vwFinishedProductWithStock SelectedProduct
        {
            get { return _SelectedProduct; }
            set
            {
                Set(ref _SelectedProduct, value);
                if (SelectedProduct == null) return;
                SelectedProductChanged();
            }
        }

        private List<ProductType> _ProductTypeList;
        public List<ProductType> ProductTypeList
        {
            get { return _ProductTypeList; }
            set { Set(ref _ProductTypeList, value); }
        }

        private ProductType _SelectedProductType;
        public ProductType SelectedProductType
        {
            get { return _SelectedProductType; }
            set
            {
                Set(ref _SelectedProductType, value);
                if (SelectedProductType == null) return;
                RefreshProductList();
            }
        }



        #endregion

        #region Commands

        public RelayCommand SaveCommand => new RelayCommand(OnSave, CanSave);

        #endregion

        public async void Load(FrameworkElement element)
        {
            _costingZonePriceList = await _costingRepo.GetCostingZonePriceList(CostingZoneId);
            var zone = await _costingRepo.GetCostingZone(CostingZoneId);
            if (zone != null)
            {
                CostingZone = zone.Description;
            }

            ProductTypeList = new List<ProductType>(await _productRepo.GetProductTypeList());
            _allProducts = new List<vwFinishedProductWithStock>(await _productRepo.GetFinishedProductsWithStock());

            foreach (var costingZonePrice in _costingZonePriceList)
            {
                var match = _allProducts.FirstOrDefault(a => a.ProductId == costingZonePrice.ProductId);
                if (match != null)
                {
                    match.Price = costingZonePrice.Price;
                }
            }

            foreach (var alreadySelectedItem in AlreadySelectedItems)
            {
                var match = _allProducts.FirstOrDefault(a => a.ProductId == alreadySelectedItem.ProductId);
                if (match != null)
                {
                    match.AlreadySelected = true;
                }
            }

            if (CurrentDispatchOrderItem == null)
            {
                CurrentDispatchOrderItem = new DispatchOrderItem();
            }
            else
            {
                IsLoading = true;
                SelectedProduct = ProductList.FirstOrDefault(a => a.ProductId == CurrentDispatchOrderItem.ProductId);
                if (SelectedProduct == null)
                {
                    IsBusy = true;
                    ProductList = new List<vwFinishedProductWithStock>(await _productRepo.GetFinishedProductsWithStock());
                    SelectedProduct = ProductList.FirstOrDefault(a => a.ProductId == CurrentDispatchOrderItem.ProductId);
                }

                _originalObject = new DispatchOrderItem();
                Context.Entry(_originalObject).State = EntityState.Added;
                Context.Entry(_originalObject).CurrentValues.SetValues(CurrentDispatchOrderItem);
                Context.DispatchOrderItems.Remove(_originalObject);
                HeaderText = "Edit";

                IsLoading = false;
            }
            SelectedProductType = ProductTypeList.FirstOrDefault();

            CurrentDispatchOrderItem.PropertyChanged += CurrentDispatchOrderItem_PropertyChanged;
        }

        private void CurrentDispatchOrderItem_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "Quantity" || e.PropertyName == "Price")
            {
                CurrentDispatchOrderItem.RaisePropertiesChangedManual();
                CurrentDispatchOrderItem.RaisePropertiesChangedManual();
            }
        }

        private async Task SelectedProductChanged()
        {
            CurrentDispatchOrderItem.Price = SelectedProduct.Price;
            CurrentDispatchOrderItem.UppTemp = SelectedProduct.Upp;
            PosChecked = false;
            CurrentDispatchOrderItem.PosQuantity = null;
            CurrentDispatchOrderItem.PosId = 0;
        }

        private async Task RefreshProductList()
        {
            if (IsBusy) return;
            if (_allProducts == null)
            {
                _allProducts = new List<vwFinishedProductWithStock>(await _productRepo.GetFinishedProductsWithStock());
            }

            // foreach (var alreadySelectedItem in AlreadySelectedItems)
            // {
            //     var match = _allProducts.FirstOrDefault(a => a.ProductId == alreadySelectedItem.ProductId);
            //     if (match != null)
            //     {
            //         match.AlreadySelected = true;
            //     }
            // }

            ProductList = new List<vwFinishedProductWithStock>(_allProducts.Where(a=>a.ProductTypeId == SelectedProductType.id));

        }

        private async void OnSave()
        {
            CurrentDispatchOrderItem.ProductId = SelectedProduct.ProductId;
            CurrentDispatchOrderItem.PosId = SelectedProduct.PosId;
            DialogResult = true;
        }

        private bool CanSave()
        {
            if (SelectedProduct == null) return false;
            if (CurrentDispatchOrderItem.Quantity == null) return false;

            return true;
        }

        public override RelayCommand CancelCommand
        {
            get
            {
                return _CancelCommand ?? (_CancelCommand = new RelayCommand(() =>
                {
                    if (_originalObject != null)
                    {
                        Context.Entry(CurrentDispatchOrderItem).CurrentValues.SetValues(_originalObject);
                    }
                    DialogResult = false;
                }));
            }
        }

        public void Unload(FrameworkElement element)
        {
            if(CurrentDispatchOrderItem!=null) CurrentDispatchOrderItem.PropertyChanged -= CurrentDispatchOrderItem_PropertyChanged;
            CurrentDispatchOrderItem = null;
            SelectedProduct = null;
            CostingZone = null;
            ProductList = null;
            CostingZoneId = 0;
            IsBusy = false;
            AlreadySelectedItems = null;
            InEditMode = false;
            DialogResult = null;
        }
    }
}

using System.Collections.Generic;
using System.Linq;
using Globeweigh.Shared;
using System.Windows;
using System.Windows.Controls;
using GalaSoft.MvvmLight.Ioc;
using Globeweigh.Model;
using Globeweigh.Shared.Helpers;
using Globeweigh.Shared.Services;

namespace Globeweigh.Admin
{
    public class StockMasterViewModel : AdminViewModelBase, IViewModel
    {
        private readonly IProductRepository _productRepo = SimpleIoc.Default.GetInstance<IProductRepository>();

        #region Properties

        private object _CurrentViewModel;
        public object CurrentViewModel
        {
            get { return _CurrentViewModel; }
            set { Set(ref _CurrentViewModel, value); }
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
                // SetCurrentView();
            }
        }

        #endregion

        private void SetCurrentView()
        {
            switch (SelectedProductType.id)
            {
                case (int)eProductType.FinishedProduct:
                    CurrentViewModel = SimpleIoc.Default.GetInstance<FinishedProductStockMasterViewModel>();
                    break;
                case (int)eProductType.RawMaterial:
                    CurrentViewModel = SimpleIoc.Default.GetInstance<CurrentStockLocationViewModel>();
                    break;
                case (int)eProductType.Pos:
                    CurrentViewModel = SimpleIoc.Default.GetInstance<CurrentStockSummaryViewModel>();
                    break;
                default:
                    CurrentViewModel = null;
                    break;

                    
            }
        }

        public async void Load(FrameworkElement element)
        {
            if (UtilitiesShared.InDesignMode) return;
            ProductTypeList = new List<ProductType>(await _productRepo.GetProductTypeList());
            SelectedProductType = ProductTypeList.FirstOrDefault();
        }

        public void Unload(FrameworkElement element)
        {
            ProductTypeList = null;
            SelectedProductType = null;
            IsUnloading = false;
        }
    }
}

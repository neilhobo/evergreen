using System.Collections.Generic;
using Globeweigh.Shared;
using Globeweigh.Shared.Services;
using Globeweigh.Model;
using MvvmDialogs;
using System.Windows;
using GalaSoft.MvvmLight.CommandWpf;
using GalaSoft.MvvmLight.Ioc;


namespace Globeweigh.Touch
{
    public class SelectProductViewModel : DialogViewModelBase, IModalDialogViewModel, IViewModel
    {
        #region private fields

        private readonly IProductRepository _productRepo = SimpleIoc.Default.GetInstance<IProductRepository>();

        #endregion

        #region Properties

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
            set { Set(ref _SelectedProduct, value); }
        }


        #endregion

        #region Commands

        public RelayCommand SelectCommand => new RelayCommand(OnSelect, CanSelect);

        #endregion

        private bool CanSelect()
        {
            if (SelectedProduct == null) return false;
            return true;
        }

        private async void RefreshProducts()
        {
            ProductList = new List<vwFinishedProductWithStock>(await _productRepo.GetFinishedProductsWithStock());
        }

        public async void Load(FrameworkElement element)
        {
            RefreshProducts();
        }
        private async void OnSelect()
        {
            DialogResult = true;
        }

        private void OnCancel()
        {
            DialogResult = false;
        }

        public void Unload(FrameworkElement element)
        {
            ProductList = null;
            SelectedProduct = null;
            DialogResult = null;
        }
    }
}

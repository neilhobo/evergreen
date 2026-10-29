using System.Collections.Generic;
using Globeweigh.Shared;
using Globeweigh.Shared.Services;
using MvvmDialogs;
using System.Windows;
using GalaSoft.MvvmLight.CommandWpf;
using GalaSoft.MvvmLight.Ioc;
using Globeweigh.Model;

namespace Globeweigh.Admin
{
    public class AddRawSupplierViewModel : DialogViewModelBase, IModalDialogViewModel, IViewModel
    {
        #region private fields


        private readonly IProductRepository _productRepo = SimpleIoc.Default.GetInstance<IProductRepository>();
        private readonly ICustomerSupplierRepository _customerSupplierRepo = SimpleIoc.Default.GetInstance<ICustomerSupplierRepository>();

        #endregion

        #region Properties

        private List<vwCustomerSupplier> _SupplierList;
        public List<vwCustomerSupplier> SupplierList
        {
            get { return _SupplierList; }
            set { Set(ref _SupplierList, value); }
        }


        private vwCustomerSupplier _SelectedSupplier;
        public vwCustomerSupplier SelectedSupplier
        {
            get { return _SelectedSupplier; }
            set { Set(ref _SelectedSupplier, value); }
        }

        private vwProduct _SelectedProduct;
        public vwProduct SelectedProduct
        {
            get { return _SelectedProduct; }
            set { Set(ref _SelectedProduct, value); }
        }

        #endregion

        #region Commands

        public RelayCommand SaveCommand => new RelayCommand(OnSave, CanSave);

        #endregion


        public async void Load(FrameworkElement element)
        {
            SupplierList = new List<vwCustomerSupplier>(await _customerSupplierRepo.GetCustomersSupplierViews((int)eEntityType.Supplier, false, false));
            SelectedSupplier = null;
        }

        private async void OnSave()
        {
            await _productRepo.AddProduct_Supplier(SelectedProduct.id, SelectedSupplier.id);
            DialogResult = true;
        }

        private bool CanSave()
        {
            if (SelectedSupplier == null || SelectedProduct == null) return false;
            return true;
        }

        public void Unload(FrameworkElement element)
        {
            SelectedProduct = null;
            SelectedSupplier = null;
            SupplierList = null;
            DialogResult = null;
        }
    }
}

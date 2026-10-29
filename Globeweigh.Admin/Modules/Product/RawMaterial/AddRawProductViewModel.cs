using Globeweigh.Shared;
using Globeweigh.Shared.Services;
using MvvmDialogs;
using System.Windows;
using GalaSoft.MvvmLight.CommandWpf;
using GalaSoft.MvvmLight.Ioc;
using Globeweigh.Model;

namespace Globeweigh.Admin
{
    public class AddRawProductViewModel : DialogViewModelBase, IModalDialogViewModel, IViewModel
    {
        #region private fields

        private readonly IProductRepository _ProductRepo = SimpleIoc.Default.GetInstance<IProductRepository>();

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

        #endregion

        #region Commands

        public RelayCommand SaveCommand => new RelayCommand(OnSave, CanSave);

        #endregion

        public async void Load(FrameworkElement element)
        {
            if (ProductId == 0)
            {
                CurrentProduct = new Product();
                CurrentProduct.ProductTypeId = (int)eProductType.RawMaterial;
            }
            else
            {
                CurrentProduct = await _ProductRepo.GetProductAsync(ProductId);
            }
        }

        private async void OnSave()
        {
            await _ProductRepo.AddUpdateProductAsync(CurrentProduct);
            DialogResult = true;
        }

        private bool CanSave()
        {
            if (CurrentProduct == null) return false;
            return true;
        }

        public void Unload(FrameworkElement element)
        {
            CurrentProduct = null;
            ProductId = 0;
            DialogResult = null;
        }
    }
}

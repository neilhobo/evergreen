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
    public class AddBogViewModel : DialogViewModelBase, IModalDialogViewModel, IViewModel
    {
        #region private fields

        private readonly IProductRepository _productRepo = SimpleIoc.Default.GetInstance<IProductRepository>();

        #endregion

        #region Properties

        public int SupplierBogId { get; set; }
        public int SupplierId { get; set; }

        private Supplier_Bog _CurrentSupplierBog;
        public Supplier_Bog CurrentSupplierBog
        {
            get { return _CurrentSupplierBog; }
            private set { Set(ref _CurrentSupplierBog, value); }
        }

        #endregion

        #region Commands

        public RelayCommand SaveCommand => new RelayCommand(OnSave, CanSave);

        #endregion


        public async void Load(FrameworkElement element)
        {
            if (SupplierBogId == 0)
            {
                CurrentSupplierBog = new Supplier_Bog();
                CurrentSupplierBog.SupplierId = SupplierId;
            }
            else
            {
                CurrentSupplierBog = await _productRepo.GetSupplierBog(SupplierBogId);
            }
        }

        private async void OnSave()
        {
            await _productRepo.AddUpdateSupplier_Bog(CurrentSupplierBog);
            DialogResult = true;
        }

        private bool CanSave()
        {
            if (CurrentSupplierBog == null) return false;
            if (string.IsNullOrEmpty(CurrentSupplierBog.Name)) return false;
            return true;
        }

        public void Unload(FrameworkElement element)
        {
            CurrentSupplierBog = null;
            SupplierBogId = 0;
            SupplierId = 0;
            DialogResult = null;
        }
    }
}

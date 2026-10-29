using System.Collections.Generic;
using System.Data.Entity;
using System.Runtime.Remoting.Contexts;
using Globeweigh.Shared;
using Globeweigh.Shared.Services;
using MvvmDialogs;
using System.Windows;
using GalaSoft.MvvmLight.CommandWpf;
using GalaSoft.MvvmLight.Ioc;
using Globeweigh.Model;

namespace Globeweigh.Admin
{
    public class AddEditAddressViewModel : DialogViewModelBase, IModalDialogViewModel, IViewModel
    {
        #region private fields

        private readonly ICustomerSupplierRepository _customerSupplierRepo =SimpleIoc.Default.GetInstance<ICustomerSupplierRepository>();
        private readonly IReferenceDataRepository _refDataRepo = SimpleIoc.Default.GetInstance<IReferenceDataRepository>();
        private CustomerSupplier_Address _originalObject;

        #endregion

        #region Properties

        public GlobeweighEntities Context { get; set; }

        private CustomerSupplier_Address _CurrentAddress;
        public CustomerSupplier_Address CurrentAddress
        {
            get { return _CurrentAddress; }
            set { Set(ref _CurrentAddress, value); }
        }

        #endregion

        #region Commands

        public RelayCommand SaveCommand => new RelayCommand(OnSave, CanSave);

        #endregion

        public async void Load(FrameworkElement element)
        {
            if (CurrentAddress == null)
            {
                CurrentAddress = new CustomerSupplier_Address();
                HeaderText = "Add New ";
            }
            else
            {
                _originalObject = new CustomerSupplier_Address();
                Context.Entry(_originalObject).State = EntityState.Added;
                Context.Entry(_originalObject).CurrentValues.SetValues(CurrentAddress);
                Context.CustomerSupplier_Address.Remove(_originalObject);
                HeaderText = "Edit ";
            }
        }

        public override RelayCommand CancelCommand
        {
            get
            {
                return _CancelCommand ?? (_CancelCommand = new RelayCommand(() =>
                {
                    if (_originalObject != null)
                    {
                        Context.Entry(CurrentAddress).CurrentValues.SetValues(_originalObject);
                    }
                    DialogResult = false;
                }));
            }
        }

        private async void OnSave()
        {
            // await _customerSupplierRepo.AddUpdateAddressAsync(CurrentAddress);
            DialogResult = true;
        }

        private bool CanSave()
        {
            if (CurrentAddress == null) return false;
            // if (string.IsNullOrEmpty(CurrentAddress.AddressFull)) return false;
            return true;
        }

        public void Unload(FrameworkElement element)
        {
            CurrentAddress = null;
            DialogResult = null;
        }
    }
}

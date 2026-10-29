using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Globeweigh.Shared;
using Globeweigh.Shared.Services;
using MvvmDialogs;
using System.Windows;
using DevExpress.Xpf.Core;
using GalaSoft.MvvmLight.CommandWpf;
using GalaSoft.MvvmLight.Ioc;
using Globeweigh.Model;
using Globeweigh.Model.Custom;
using Globeweigh.Shared.Helpers;

namespace Globeweigh.Admin
{
    public class AddEditCustomerSupplierViewModel : DialogViewModelBase, IModalDialogViewModel, IViewModel
    {
        #region private fields

        private readonly ICustomerSupplierRepository _customerSupplierRepo = SimpleIoc.Default.GetInstance<ICustomerSupplierRepository>();
        private readonly ICostingRepository _costingRepo = SimpleIoc.Default.GetInstance<ICostingRepository>();
        private readonly IDialogService _dialogService = SimpleIoc.Default.GetInstance<IDialogService>();
        private GlobeweighEntities _context = new GlobeweighEntities(GlobalVariables.ConnectionString);
        private bool _hasChanges = false;

        #endregion

        #region Properties

        public int CustomerSupplierId { get; set; }
        public int EntityTypeId { get; set; }
        public bool CostingZoneMandatory { get; set; }

        private List<CostingZone> _CostingZoneList;
        public List<CostingZone> CostingZoneList
        {
            get { return _CostingZoneList; }
            set { Set(ref _CostingZoneList, value); }
        }

        private CustomerSupplier _CurrentCustomerSupplier;
        public CustomerSupplier CurrentCustomerSupplier
        {
            get { return _CurrentCustomerSupplier; }
            private set { Set(ref _CurrentCustomerSupplier, value); }
        }


        private CustomerSupplier_Address _SelectedAddress;
        public CustomerSupplier_Address SelectedAddress
        {
            get { return _SelectedAddress; }
            set { Set(ref _SelectedAddress, value); }
        }

        public bool IsSupplier
        {
            get { return EntityTypeId == (int)eEntityType.Supplier; }
        }

        #endregion

        #region Commands

        public RelayCommand SaveCommand => new RelayCommand(OnSave, CanSave);
        public RelayCommand AddAddressCommand => new RelayCommand(OnAddAddress);
        public RelayCommand EditAddressCommand => new RelayCommand(OnEditAddress);
        public RelayCommand DeleteAddressCommand => new RelayCommand(OnDeleteAddress);

        #endregion

        private void OnDeleteAddress()
        {
            if (SelectedAddress == null) return;
            _context.CustomerSupplier_Address.Remove(SelectedAddress);
            SelectedAddress = null;
        }

        private void OnAddAddress()
        {
            var dialogViewModel = SimpleIoc.Default.GetInstanceWithoutCaching<AddEditAddressViewModel>();
            dialogViewModel.HeaderText = "Add Delivery Point";
            dialogViewModel.Context = _context;
            bool? success = _dialogService.ShowDialog<AddEditAddressView>(this, dialogViewModel);
            if (success == true)
            {
                var address = dialogViewModel.CurrentAddress;
                CurrentCustomerSupplier.CustomerSupplier_Address.Add(address);
                SelectedAddress = null;
            }
        }

        private void OnEditAddress()
        {
            if (SelectedAddress == null) return;
            var dialogViewModel = SimpleIoc.Default.GetInstanceWithoutCaching<AddEditAddressViewModel>();
            dialogViewModel.HeaderText = "Edit Delivery Point";
            dialogViewModel.Context = _context;
            dialogViewModel.CurrentAddress = SelectedAddress;
            bool? success = _dialogService.ShowDialog<AddEditAddressView>(this, dialogViewModel);
            if (success == true)
            {
                SelectedAddress.RaisePropertiesChangedManual();
                SelectedAddress = null;
            }
        }

        public async void Load(FrameworkElement element)
        {
            CostingZoneList = new List<CostingZone>(await _costingRepo.GetCostingZones());
            if (CustomerSupplierId == 0)
            {
                CurrentCustomerSupplier = new CustomerSupplier();
                CurrentCustomerSupplier.TypeId = EntityTypeId;
                CurrentCustomerSupplier.RaisePropertiesChangesManual();
                HeaderText = "Add New ";
            }
            else
            {
                CurrentCustomerSupplier = await _customerSupplierRepo.GetCustomerSupplierAsync(CustomerSupplierId, _context);
                HeaderText = "Edit";
            }
            if (EntityTypeId == (int)eEntityType.Customer) HeaderText += " Customer";
            else if (EntityTypeId == (int)eEntityType.Supplier) HeaderText += " Supplier";

            CurrentCustomerSupplier.PropertyChanged += CurrentCustomerSupplier_PropertyChanged;
        }

        private void CurrentCustomerSupplier_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            HasChanges = true;
        }

        private async void OnSave()
        {
            await _customerSupplierRepo.AddUpdateAsync(_context, CurrentCustomerSupplier);
            DialogResult = true;
        }

        private bool CanSave()
        {
            if (CurrentCustomerSupplier == null) return false;
            if (string.IsNullOrEmpty(CurrentCustomerSupplier.Name)) return false;
            if (CostingZoneMandatory && CurrentCustomerSupplier.CostingZoneId == 0)return false;
            return true;
        }

        public override RelayCommand CancelCommand
        {
            get
            {
                return _CancelCommand ?? (_CancelCommand = new RelayCommand(() =>
                {
                    if (_context.ChangeTracker.HasChanges())
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
            if (CurrentCustomerSupplier != null) CurrentCustomerSupplier.PropertyChanged -= CurrentCustomerSupplier_PropertyChanged;
            CurrentCustomerSupplier = null;
            CostingZoneMandatory = false;
            CustomerSupplierId = 0;
            EntityTypeId = 0;
            DialogResult = null;
            _context = null;
        }
    }
}

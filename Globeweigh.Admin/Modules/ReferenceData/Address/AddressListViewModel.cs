using System;
using System.Collections.Generic;
using System.Linq;
using Globeweigh.Shared.Services;
using Globeweigh.Shared;
using System.Windows;
using GalaSoft.MvvmLight.CommandWpf;
using Globeweigh.Shared.Helpers;
using Globeweigh.Model;
using GalaSoft.MvvmLight.Ioc;
using IDialogService = MvvmDialogs.IDialogService;
using System.Threading.Tasks;
using System.Windows.Controls;

namespace Globeweigh.Admin
{
    public class AddressListViewModel : AdminViewModelBase, IViewModel
    {
        #region private fields

        private readonly ICustomerSupplierRepository _customerSupplierRepo =SimpleIoc.Default.GetInstance<ICustomerSupplierRepository>();
        private readonly IDialogService _dialogService = SimpleIoc.Default.GetInstance<IDialogService>();

        #endregion

        #region Properties

        private List<CustomerSupplier_Address> _AddressList;
        public List<CustomerSupplier_Address> AddressList
        {
            get { return _AddressList; }
            set { Set(ref _AddressList, value); }
        }

        private CustomerSupplier_Address _SelectedAddress;
        public CustomerSupplier_Address SelectedAddress
        {
            get { return _SelectedAddress; }
            set { Set(ref _SelectedAddress, value); }
        }

        #endregion

        #region Commands

        public RelayCommand AddCommand => new RelayCommand(OnAdd);
        public RelayCommand EditCommand => new RelayCommand(OnEdit);
        public RelayCommand DeleteCommand => new RelayCommand(OnDelete);

        #endregion

        private void OnAdd()
        {
            AddEditNewRefDataDialog(0);
        }
        private void OnEdit()
        {
            AddEditNewRefDataDialog(SelectedAddress.id);
        }

        private async void OnDelete()
        {
            var result = _dialogService.ShowMessageBox(this, "Delete Address?", "", MessageBoxButton.OKCancel);
            if (result == MessageBoxResult.Cancel) return;
            // await _customerSupplierRepo.DeleteAddress(SelectedAddress.id);
            await RefreshList();
            SelectedAddress = null;

        }

        private async void AddEditNewRefDataDialog(int entityId)
        {
            var dialogViewModel = SimpleIoc.Default.GetInstanceWithoutCaching<AddEditAddressViewModel>();
            // dialogViewModel.AddressId = entityId;
            bool? success = _dialogService.ShowDialog<AddEditAddressView>(this, dialogViewModel);
            if (success == true)
            {
                SelectedAddress = null;
                await RefreshList();
            }
        }

        private async Task RefreshList()
        {
            // AddressList = new List<Address>(await _customerSupplierRepo.GetAllAddresses());
        }

        public async void Load(FrameworkElement element)
        {
            if (UtilitiesShared.InDesignMode) return;
            await RefreshList();
        }

        public void Unload(FrameworkElement element)
        {
            AddressList = null;
            SelectedAddress = null;
        }

    }
}

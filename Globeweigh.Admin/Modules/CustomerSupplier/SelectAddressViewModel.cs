using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Globeweigh.Shared;
using Globeweigh.Shared.Services;
using System.Windows;
using GalaSoft.MvvmLight.CommandWpf;
using System.Threading.Tasks;
using GalaSoft.MvvmLight.Ioc;
using Globeweigh.Model;
using Globeweigh.Model.Custom;
using MvvmDialogs;

namespace Globeweigh.Admin
{
    public class SelectAddressViewModel : DialogViewModelBase, IModalDialogViewModel, IViewModel
    {
        #region private fields

        private readonly ICustomerSupplierRepository _customerSupplierRepo = SimpleIoc.Default.GetInstance<ICustomerSupplierRepository>();

        #endregion

        #region Properties

        public int CustomerId { get; set; }

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

        public RelayCommand SaveCommand => new RelayCommand(OnSelect);

        #endregion

        private async void OnSelect()
        {
            this.DialogResult = true;
        }

        private async Task RefreshAddresses()
        {
            var custAddressList = new List<CustomerSupplier_Address>(await _customerSupplierRepo.GetAddresses(CustomerId));
            AddressList = new List<CustomerSupplier_Address>(custAddressList.OrderBy(a => a.AddressName));
        }

        public async void Load(FrameworkElement element)
        {
            await RefreshAddresses();
        }

        public void Unload(FrameworkElement element)
        {
            AddressList = null;
            SelectedAddress = null;
            CustomerId = 0;
            DialogResult = null;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
// using System.Collections.Generic;
// using System.Collections.ObjectModel;
using System.Data.Entity;
using System.Linq;
using System.Threading.Tasks;
using Globeweigh.Shared;
using Globeweigh.Shared.Services;
using MvvmDialogs;
using System.Windows;
using DevExpress.DataProcessing;
using DevExpress.Xpf.CodeView;
using GalaSoft.MvvmLight.CommandWpf;
using Globeweigh.Model;
using GalaSoft.MvvmLight.Ioc;
using System.Data.Entity.Core.Metadata.Edm;

namespace Globeweigh.Admin
{
    public class CreateDispatchOrderViewModel : DialogViewModelBase, IModalDialogViewModel, IViewModel
    {
        #region private fields

        private readonly IStockLocationRepository _stockLocationRepo = SimpleIoc.Default.GetInstance<IStockLocationRepository>();
        private readonly IDispatchOrderRepository _dispatchRepo = SimpleIoc.Default.GetInstance<IDispatchOrderRepository>();
        private readonly IDialogService _dialogService = SimpleIoc.Default.GetInstance<IDialogService>();
        private readonly IHaulierRepository _haulierRepo = SimpleIoc.Default.GetInstance<IHaulierRepository>();
        private readonly ICustomerSupplierRepository _customerRepo = SimpleIoc.Default.GetInstance<ICustomerSupplierRepository>();
        private readonly IProductRepository _productRepo = SimpleIoc.Default.GetInstance<IProductRepository>();
        private GlobeweighEntities _context = new GlobeweighEntities(GlobalVariables.ConnectionString);
        private List<Product> _productList ;
        #endregion

        #region Properties

        public int DispatchOrderId { get; set; }
        public bool ReadOnlyMode { get; set; }


        private bool _IsTransfer;
        public bool IsTransfer
        {
            get { return _IsTransfer; }
            set { Set(ref _IsTransfer, value); }
        }

        // Delivery type is inferred from DeliveryDate: ASAP => null, Week Commencing => a date.
        private bool _IsAsap;
        public bool IsAsap
        {
            get { return _IsAsap; }
            set
            {
                Set(ref _IsAsap, value);
                if (CurrentDispatchOrder == null) return;
                if (_IsAsap)
                {
                    CurrentDispatchOrder.DeliveryDate = null;
                }
                else if (CurrentDispatchOrder.DeliveryDate == null)
                {
                    // Default to today so "Week Commencing" doesn't silently save as ASAP.
                    CurrentDispatchOrder.DeliveryDate = DateTime.Today;
                }
            }
        }

        private DispatchOrderItem _SelectedDispatchOrderItem;
        public DispatchOrderItem SelectedDispatchOrderItem
        {
            get { return _SelectedDispatchOrderItem; }
            set { Set(ref _SelectedDispatchOrderItem, value); }
        }

        private DispatchOrder _CurrentDispatchOrder;
        public DispatchOrder CurrentDispatchOrder
        {
            get { return _CurrentDispatchOrder; }
            set { Set(ref _CurrentDispatchOrder, value); }
        }

        private List<CustomerSupplier> _CustomerList;
        public List<CustomerSupplier> CustomerList
        {
            get { return _CustomerList; }
            private set { Set(ref _CustomerList, value); }
        }

        private CustomerSupplier _SelectedCustomer;
        public CustomerSupplier SelectedCustomer
        {
            get { return _SelectedCustomer; }
            set
            {
                Set(ref _SelectedCustomer, value);
                if (SelectedCustomer != null)
                {
                    Task.Run(() => CustomerChanged());
                }
            }
        }

        private List<Haulier> _HaulierList;
        public List<Haulier> HaulierList
        {
            get { return _HaulierList; }
            private set { Set(ref _HaulierList, value); }
        }

        private List<StockLocation> _LocationList;
        public List<StockLocation> LocationList
        {
            get { return _LocationList; }
            set { Set(ref _LocationList, value); }
        }

        // private List<Product> _ProductList;
        // public List<Product> ProductList
        // {
        //     get { return _ProductList; }
        //     set { Set(ref _ProductList, value); }
        // }

        #endregion

        #region Commands

        public RelayCommand SaveCommand => new RelayCommand(OnSave, CanSave);
        public RelayCommand AddCommand => new RelayCommand(OnAdd, CanAdd);
        public RelayCommand DeleteCommand => new RelayCommand(OnDelete);
        public RelayCommand SelectAddressCommand => new RelayCommand(OnSelectAddress);

        #endregion


        private bool CanAdd()
        {
            if (SelectedCustomer == null) return false;
            return true;
        }

        private async void OnAdd()
        {
            if(SelectedCustomer == null)return;
            if (SelectedCustomer.CostingZoneId == 0)
            {
                var dlgRes = MessageBox.Show("Customer has no Costing Zone setup." + Environment.NewLine + "Press OK to Edit Customer and set one up.", "", MessageBoxButton.OKCancel);
                if(dlgRes == MessageBoxResult.Cancel)return;
                var dgCustomer = SimpleIoc.Default.GetInstanceWithoutCaching<AddEditCustomerSupplierViewModel>();
                dgCustomer.CustomerSupplierId = SelectedCustomer.id;
                dgCustomer.CostingZoneMandatory = true;
                bool? success1 = _dialogService.ShowDialog<AddEditCustomerSupplierView>(this, dgCustomer);
                if (success1 == true)
                {
                    SelectedCustomer.CostingZoneId = dgCustomer.CurrentCustomerSupplier.CostingZoneId;
                }
                else
                {
                    return;
                }
            }


            var dialogViewModel = SimpleIoc.Default.GetInstanceWithoutCaching<AddEditDispatchOrderItemViewModel>();
            dialogViewModel.HeaderText = "Add Order Item";
            dialogViewModel.AlreadySelectedItems = new List<DispatchOrderItem>(CurrentDispatchOrder.DispatchOrderItems);
            dialogViewModel.CostingZoneId = SelectedCustomer.CostingZoneId;
            bool? success = _dialogService.ShowDialog<AddEditDispatchOrderItemView>(this, dialogViewModel);
            if (success == true)
            {
                var newOrderItem = dialogViewModel.CurrentDispatchOrderItem;
                var match = _productList.FirstOrDefault(a => a.id == newOrderItem.ProductId);
                if (match == null) return;
                newOrderItem.ProductNameTemp = match.Description;
                newOrderItem.SizeTemp = match.Size;
                newOrderItem.UppTemp = match.Upp;
                CurrentDispatchOrder.DispatchOrderItems.Add(newOrderItem);

                if ((newOrderItem.PosQuantity != null || newOrderItem.PosQuantity>0) && (newOrderItem.PosId !=null || newOrderItem.PosId != 0))
                {
                    var matchPos = _productList.FirstOrDefault(a => a.id == newOrderItem.PosId);
                    if (matchPos == null) return;
                    var newOrderItemPos = new DispatchOrderItem();
                    newOrderItemPos.ProductId = (int)newOrderItem.PosId;
                    newOrderItemPos.ProductNameTemp = matchPos.Description;
                    newOrderItemPos.SizeTemp = matchPos.Size;
                    if (matchPos.Upp == 0) { newOrderItemPos.UppTemp = null; }
                    else { newOrderItemPos.UppTemp = matchPos.Upp; }

                    newOrderItemPos.Quantity = newOrderItem.PosQuantity;
                    CurrentDispatchOrder.DispatchOrderItems.Add(newOrderItemPos);
                }


            }
        }

        private async void OnEdit()
        {
            var dialogViewModel = SimpleIoc.Default.GetInstanceWithoutCaching<AddEditDispatchOrderItemViewModel>();
            dialogViewModel.HeaderText = "Edit Order Item";
            dialogViewModel.InEditMode = true;
            dialogViewModel.CurrentDispatchOrderItem = SelectedDispatchOrderItem;
            bool? success = _dialogService.ShowDialog<AddEditDispatchOrderItemView>(this, dialogViewModel);
            if (success == true)
            {
                // var newOrderItem = dialogViewModel.CurrentDispatchOrderItem;
                // CurrentDispatchOrder.DispatchOrderItems.Add(newOrderItem);
            }
        }

        private async void OnSelectAddress()
        {
            var dialogViewModel = SimpleIoc.Default.GetInstanceWithoutCaching<SelectAddressViewModel>();
            dialogViewModel.CustomerId = CurrentDispatchOrder.CustomerId;
            bool? success = _dialogService.ShowDialog<SelectAddressView>(this, dialogViewModel);
            if (success == true)
            {
                CurrentDispatchOrder.AddressId = dialogViewModel.SelectedAddress.id;
                CurrentDispatchOrder.AddressFull = dialogViewModel.SelectedAddress.AddressDisplay;
            }
        }

        private async void CustomerChanged()
        {
            if (CurrentDispatchOrder.id == 0)
            {
                CurrentDispatchOrder.OrderNo = await _dispatchRepo.GetNextCustomerOrderNo(SelectedCustomer.id);
            }

            // if (SelectedCustomer == null && CustomerList != null)
            // {
            //     SelectedCustomer = CustomerList.FirstOrDefault(a => a.id == CurrentDispatchOrder.CustomerId);
            // }

            if (SelectedCustomer != null) CurrentDispatchOrder.CustomerCode = SelectedCustomer.Code;
            CurrentDispatchOrder.RaisePropertiesChangedManual();
        }

        private bool CanSave()
        {
            if (CurrentDispatchOrder == null) return false;

            if (IsTransfer)
            {
                return true;
            }
            if (CurrentDispatchOrder.CustomerId == 0) return false;
            if (string.IsNullOrEmpty(CurrentDispatchOrder.PoNumber)) return false;
            // if (CurrentDispatchOrder.DeliveryDate == null) return false;

            return true;     
        }

        private async void OnDelete()
        {
            if (SelectedDispatchOrderItem == null) return;
            if (SelectedDispatchOrderItem.id == 0)
            {
                CurrentDispatchOrder.DispatchOrderItems.Remove(SelectedDispatchOrderItem);
            }
            else
            {
                _context.Entry(SelectedDispatchOrderItem).State = EntityState.Deleted;

            }

        }

        private async void OnSave()
        {

            await _dispatchRepo.SaveCustomerOrder(CurrentDispatchOrder, _context);
            if (CurrentDispatchOrder.id == 0)
                _context.DispatchOrders.Add(CurrentDispatchOrder);


            await _context.SaveChangesAsync();
            this.DialogResult = true;
        }

        public async void Load(FrameworkElement element)
        {
            IsBusy = true;
            CustomerList = new List<CustomerSupplier>(await _customerRepo.GetCustomersSuppliers((int)eEntityType.Customer, false,false));
            HaulierList = new List<Haulier>(await _haulierRepo.GetHaulierList());
            LocationList = new List<StockLocation>(await _stockLocationRepo.GetStockLocations());
            _productList = new List<Product>(await _productRepo.GetProductsAsync(eProductType.FinishedProduct, eProductType.Pos));

            IsBusy = false;
            if (DispatchOrderId == 0)
            {
                CurrentDispatchOrder = new DispatchOrder();

                if (IsTransfer)
                {
                    CurrentDispatchOrder.OrderTypeId = (int) eDispatchOrderType.Transfer;
                    CurrentDispatchOrder.TransferOrderNo = await _dispatchRepo.GetNextOrderNo(IsTransfer);
                }
                else
                {
                    CurrentDispatchOrder.OrderTypeId = (int)eDispatchOrderType.Customer;
                    CurrentDispatchOrder.OrderNo = await _dispatchRepo.GetNextOrderNo(IsTransfer);
                }
                CurrentDispatchOrder.DateCreated = DateTime.Now;
                CurrentDispatchOrder.DispatchOrderItems = new ObservableCollection<DispatchOrderItem>();
            }
            else
            {
                var customerOrder = await _dispatchRepo.GetDispatchOrder(DispatchOrderId, _context);
                if (customerOrder.OrderTypeId == (int) eDispatchOrderType.Transfer) IsTransfer = true;
                else { IsTransfer = false; }
                foreach (var item in customerOrder.DispatchOrderItems)
                {
                    var match = _productList.FirstOrDefault(a => a.id == item.ProductId);
                    if(match == null)continue;
                    item.ProductNameTemp = match.Description;
                    item.SizeTemp = match.Size;
                    if (match.Upp == 0) { item.UppTemp = null; }
                    else { item.UppTemp = match.Upp; }
                }
                CurrentDispatchOrder = customerOrder;
            }
            IsAsap = CurrentDispatchOrder.DeliveryDate == null;
            CurrentDispatchOrder.RaisePropertiesChangedManual();
        }

        public void Unload(FrameworkElement element)
        {
            DispatchOrderId = 0;
            IsTransfer = false;
            CurrentDispatchOrder = null;
            IsAsap = false;
            DialogResult = null;
            IsBusy = false;
            ReadOnlyMode = false;
            _context = null;
        }
    }
}

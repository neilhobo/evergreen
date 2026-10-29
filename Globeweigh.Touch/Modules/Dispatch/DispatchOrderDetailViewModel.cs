using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using GalaSoft.MvvmLight.Ioc;
using Globeweigh.Model;
using Globeweigh.Shared;
using System.Windows;
using GalaSoft.MvvmLight.CommandWpf;
using System.Threading.Tasks;
using DevExpress.XtraReports.UI;
using Globeweigh.Shared.Helpers;
using Globeweigh.Shared.Services;
using IDialogService = MvvmDialogs.IDialogService;

namespace Globeweigh.Touch
{
    public class DispatchOrderDetailViewModel : TouchViewModelBase, IViewModel
    {
        #region private fields

        private readonly IDialogService _dialogService = SimpleIoc.Default.GetInstance<IDialogService>();
        private readonly IDispatchOrderRepository _dispatchRepo = SimpleIoc.Default.GetInstance<IDispatchOrderRepository>();
        private readonly IPalletRepository _palletRepo = SimpleIoc.Default.GetInstance<IPalletRepository>();
        private readonly IGlobeweighUserRepository _userRepo = SimpleIoc.Default.GetInstance<IGlobeweighUserRepository>();

        #endregion

        #region Properties

        public int DispatchOrderId { get; set; }

        private List<vwDispatchOrderItem> _DispatchOrderItemList;
        public List<vwDispatchOrderItem> DispatchOrderItemList
        {
            get { return _DispatchOrderItemList; }
            set { Set(ref _DispatchOrderItemList, value); }
        }

        private vwDispatchOrder _CurrentDispatchOrder;
        public vwDispatchOrder CurrentDispatchOrder
        {
            get { return _CurrentDispatchOrder; }
            set { Set(ref _CurrentDispatchOrder, value); }
        }

        private vwDispatchOrderItem _SelectedOrderItem;
        public vwDispatchOrderItem SelectedOrderItem
        {
            get { return _SelectedOrderItem; }
            set
            {
                Set(ref _SelectedOrderItem, value);
                if (SelectedOrderItem == null) return;
                RefreshPickedList();
            }
        }

        private List<vwDispatchOrderPicked> _PickedList;
        public List<vwDispatchOrderPicked> PickedList
        {
            get { return _PickedList; }
            set { Set(ref _PickedList, value); }
        }

        private vwDispatchOrderPicked _SelectedPickedItem;
        public vwDispatchOrderPicked SelectedPickedItem
        {
            get { return _SelectedPickedItem; }
            set { Set(ref _SelectedPickedItem, value); }
        }

        #endregion

        #region Commands

        public RelayCommand ScanCommand => new RelayCommand(OnScan);
        public RelayCommand HomeCommand => new RelayCommand(() => SimpleIoc.Default.GetInstance<MainWindowViewModel>().CurrentViewModel = SimpleIoc.Default.GetInstance<DispatchOrderListViewModel>());
        public RelayCommand DeleteCommand => new RelayCommand(OnDelete);    
        public RelayCommand ManualAddCommand => new RelayCommand(OnManualAdd);

        #endregion

        private async void OnManualAdd()
        {
            if (SelectedOrderItem == null) return;
            if(SelectedOrderItem.TargetMet)return;
            var pallet = await _palletRepo.GetNextAvailablePallet(SelectedOrderItem.ProductId, 3);
            if (pallet == null)
            {
                var dialogVm = SimpleIoc.Default.GetInstance<OkCancelViewModel>();
                dialogVm.MessageText = "No pallets in stock";
                bool? success = _dialogService.ShowDialog<OkCancelView>(this, dialogVm);
            }
            else
            {
                var newPickedItem = new DispatchOrder_Picked();
                newPickedItem.DispatchOrderId = DispatchOrderId;
                newPickedItem.DispatchOrderItemId = SelectedOrderItem.id;
                newPickedItem.ProductId = SelectedOrderItem.ProductId;
                // newPickedItem.PalletId = pallet.id;
                newPickedItem.Quantity = 1;
                await _dispatchRepo.AddPalletToDispatchOrder(newPickedItem);
                await RefreshOrderItemList(true);
                await RefreshPickedList();
                await RefreshOrder();
            }


        }

        private async void OnScan()
        {
            if (SelectedOrderItem == null) return;

            var dialogVm = SimpleIoc.Default.GetInstance<ScanDispatchPickingViewModel>();
            dialogVm.SelectedOrderItem = SelectedOrderItem;
            bool? success = _dialogService.ShowDialog<ScanDispatchPickingView>(this, dialogVm);
            if (success == true)
            {
                await RefreshOrderItemList(true);
                await RefreshPickedList();
                await RefreshOrder();
            }
        }

        private bool CanCompleteOrder()
        {
            if (CurrentDispatchOrder == null) return false;
            // if (CurrentDispatchOrder.DateOrderCheckComplete == null) return false;
            return true;
        }

        private async void OnCompleteOrder()
        {
            var dialogVm = SimpleIoc.Default.GetInstanceWithoutCaching<OkCancelViewModel>();
            dialogVm.MessageText = "Complete Order?";
            bool? success = _dialogService.ShowDialog<OkCancelView>(this, dialogVm);
            if (success == true)
            {
                var list = new ObservableCollection<vwDispatchOrder>();
                list.Add(CurrentDispatchOrder);
                await _dispatchRepo.UpdateDispatchedDateComplete(DispatchOrderId, DateTime.Now);
                SimpleIoc.Default.GetInstance<MainWindowViewModel>().CurrentViewModel = SimpleIoc.Default.GetInstance<DispatchOrderListViewModel>();
            }
        }

        private async void OnDelete()
        {
            if (SelectedPickedItem == null) return;
            var dialogVm = SimpleIoc.Default.GetInstance<OkCancelViewModel>();
            dialogVm.MessageText = "Delete from picked items?";
            bool? success = _dialogService.ShowDialog<OkCancelView>(this, dialogVm);
            if (success == true)
            {
                await _dispatchRepo.DeletePickingItemFromDispatchOrder(SelectedPickedItem.id);
                await RefreshOrderItemList(true);
                await RefreshPickedList();
                await RefreshOrder();
            }
        }

        public async Task RefreshPickedList()
        {
            if (SelectedOrderItem == null) return;
            PickedList = new List<vwDispatchOrderPicked>(await _dispatchRepo.GetDispatchOrderPickedItemsByOrderItemId(SelectedOrderItem.id));
        }

        public async Task RefreshOrderItemList(bool keepSelection)
        {
            int selectedOrderId = 0;
            if (SelectedOrderItem != null && keepSelection)
            {
                selectedOrderId = SelectedOrderItem.id;
            }
            DispatchOrderItemList = new List<vwDispatchOrderItem>(await _dispatchRepo.GetDispatchOrderItems(DispatchOrderId));

            if (selectedOrderId != 0 && keepSelection)
            {
                SelectedOrderItem = DispatchOrderItemList.FirstOrDefault(a => a.id == selectedOrderId);
            }
        }

        public async Task RefreshOrder()
        {
            CurrentDispatchOrder = await _dispatchRepo.GetDispatchOrderView(DispatchOrderId, false,false);
        }

        public async void Load(FrameworkElement element)
        {
            if (DispatchOrderId != 0)
            {
                await RefreshOrder();
                await RefreshOrderItemList(false);
                await RefreshPickedList();
            }
        }

        public void Unload(FrameworkElement element)
        {
            SelectedOrderItem = null;
            DispatchOrderId = 0;
            DispatchOrderItemList = null;
            CurrentDispatchOrder = null;
            IsBusy = false;
        }
    }
}

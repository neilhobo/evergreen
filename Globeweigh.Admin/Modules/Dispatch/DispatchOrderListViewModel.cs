using System.Collections.Generic;
using Globeweigh.Shared;
using Globeweigh.Shared.Services;
using System.Windows;
using GalaSoft.MvvmLight.CommandWpf;
using Globeweigh.Model;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using GalaSoft.MvvmLight.Ioc;
using Globeweigh.Reports.Dispatch;
using MvvmDialogs;
using System.ComponentModel;
using DevExpress.XtraReports.UI;

namespace Globeweigh.Admin
{
    public class DispatchOrderListViewModel : AdminViewModelBase, IViewModel
    {
        #region private fields

        private readonly IDispatchOrderRepository _dispatchRepo = SimpleIoc.Default.GetInstance<IDispatchOrderRepository>();
        private readonly IDialogService _dialogService = SimpleIoc.Default.GetInstance<IDialogService>();
        private readonly IWeighbridgeRepository _weighbridgeRepo = SimpleIoc.Default.GetInstance<IWeighbridgeRepository>();

        #endregion

        #region Properties

        private List<vwDispatchOrder> _OpenOrderList;
        public List<vwDispatchOrder> OpenOrderList
        {
            get { return _OpenOrderList; }
            set { Set(ref _OpenOrderList, value); }
        }

        private vwDispatchOrder _SelectedDispatchOrder;
        public vwDispatchOrder SelectedDispatchOrder
        {
            get { return _SelectedDispatchOrder; }
            set
            {
                Set(ref _SelectedDispatchOrder, value);
                if (SelectedDispatchOrder == null) return;
                SelectedLoad = null;
                SelectedPending = null;
            }
        }

        private List<vwWeighbridgeComplete> _CompletedWeighbridgeRecordList;
        public List<vwWeighbridgeComplete> CompletedWeighbridgeRecordList
        {
            get { return _CompletedWeighbridgeRecordList; }
            set { Set(ref _CompletedWeighbridgeRecordList, value); }
        }

        private vwWeighbridgeComplete _SelectedCompletedWeighbridgeRecord;
        public vwWeighbridgeComplete SelectedCompletedWeighbridgeRecord
        {
            get { return _SelectedCompletedWeighbridgeRecord; }
            set
            {
                Set(ref _SelectedCompletedWeighbridgeRecord, value);
            }
        }

        private vwDispatchOrder _CompletedDispatchOrder;
        public vwDispatchOrder CompletedDispatchOrder
        {
            get { return _CompletedDispatchOrder; }
            set
            {
                Set(ref _CompletedDispatchOrder, value);
                if (CompletedDispatchOrder == null) return;
                SelectedLoad = null;
                SelectedPending = null;
                SelectedDispatchOrder = null;
            }
        }

        private List<vwWeighbridgePreLoad> _LoadList;
        public List<vwWeighbridgePreLoad> LoadList
        {
            get { return _LoadList; }
            set { Set(ref _LoadList, value); }
        }

        private List<vwWeighbridgePending> _PendingList;
        public List<vwWeighbridgePending> PendingList
        {
            get { return _PendingList; }
            set { Set(ref _PendingList, value); }
        }

        private vwWeighbridgePreLoad _SelectedLoad;
        public vwWeighbridgePreLoad SelectedLoad
        {
            get { return _SelectedLoad; }
            set
            {
                Set(ref _SelectedLoad, value);
                if (SelectedLoad == null) return;
                SelectedPending = null;
                SelectedDispatchOrder = null;
            }
        }

        private ObservableCollection<vwDispatchOrder> _SelectedDispatchOrders;
        public ObservableCollection<vwDispatchOrder> SelectedDispatchOrders
        {
            get { return _SelectedDispatchOrders; }
            set { Set(ref _SelectedDispatchOrders, value); }
        }

        private DateTime _DateFrom;
        public DateTime DateFrom
        {
            get { return _DateFrom; }
            set { Set(ref _DateFrom, value); }
        }

        private DateTime _DateTo;
        public DateTime DateTo
        {
            get { return _DateTo; }
            set { Set(ref _DateTo, value); }
        }

        private bool _IsCompletedSelected;
        public bool IsCompletedSelected
        {
            get { return _IsCompletedSelected; }
            set
            {
                Set(ref _IsCompletedSelected, value);
                RefreshDispatchOrders();
            }
        }

        private vwWeighbridgePending _SelectedPending;
        public vwWeighbridgePending SelectedPending
        {
            get { return _SelectedPending; }
            set
            {
                Set(ref _SelectedPending, value);
                if (SelectedPending == null) return;
                SelectedLoad = null;
                SelectedDispatchOrder = null;
                // RefreshAssignedOrders();
            }
        }

        private bool _IsCompletedOrdersBusy;
        public bool IsCompletedOrdersBusy
        {
            get { return _IsCompletedOrdersBusy; }
            set { Set(ref _IsCompletedOrdersBusy, value); }
        }



        #endregion

        #region Commands

        public RelayCommand RefreshCommand => new RelayCommand(async () => await RefreshDispatchOrders());
        public RelayCommand ViewCommand => new RelayCommand(OnViewCommand);
        public RelayCommand CreateDispatchOrderCommand => new RelayCommand(OnCreateDispatchOrder);
        public RelayCommand AddTransferOrderCommand => new RelayCommand(OnAddTransferOrder);
        public RelayCommand EditDispatchOrderCommand => new RelayCommand(OnEditDispatchOrder);
        public RelayCommand DeleteDispatchOrderCommand => new RelayCommand(OnDeleteDispatchOrder);
        public RelayCommand PrintDeliveryNoteCommand => new RelayCommand(OnPrintDeliveryDocket);
        public RelayCommand UnCompleteDispatchOrderCommand => new RelayCommand(UnCompleteDispatchOrder);
        public RelayCommand CompleteDispatchOrderCommand => new RelayCommand(OnCompleteDispatchOrder);
        public RelayCommand AddIncomingCommand => new RelayCommand(OnAddIncoming);
        public RelayCommand AddOutgoingCommand => new RelayCommand(OnAddOutgoing);
        public RelayCommand DeletePendingCommand => new RelayCommand(OnDeletePending);
        public RelayCommand EditPendingCommand => new RelayCommand(OnEditPending);
        public RelayCommand RefreshPendingCommand => new RelayCommand(async () => await RefreshPending());
        public RelayCommand EditLoadCommand => new RelayCommand(OnEditLoad);
        public RelayCommand AddLoadCommand => new RelayCommand(OnAddLoad);
        public RelayCommand DeleteLoadCommand => new RelayCommand(OnDeleteLoad);
        public RelayCommand RefreshCompletedCommand => new RelayCommand(OnRefreshCompleted);
        public RelayCommand WeighOutCommand => new RelayCommand(OnWeightOut);
        public RelayCommand ViewLoadCommand => new RelayCommand(OnViewLoad);
        public RelayCommand CompletePickingCommand => new RelayCommand(OnCompletePicking, CanCompletePicking);


        #endregion

        private bool CanCompletePicking()
        {
            if (SelectedPending == null || SelectedPending.DatePickingComplete !=null) return false;
            return true;
        }

        private async void OnCompletePicking()
        {
            if (SelectedPending == null) return;
            var result = _dialogService.ShowMessageBox(this, "Complete picking?", "", MessageBoxButton.OKCancel);
            if (result == MessageBoxResult.Cancel) return;
            await _dispatchRepo.UpdateLoadPicking(SelectedPending.id, DateTime.Now);
            await RefreshPending();
        }

        private async void OnViewLoad()
        {
            if (SelectedPending == null) return;
            var dialogVm1 = SimpleIoc.Default.GetInstanceWithoutCaching<ViewLoadDetailsViewModel>();
            dialogVm1.LoadId = SelectedPending.id;
            bool? success1 = _dialogService.ShowDialog<ViewLoadDetailsView>(this, dialogVm1);
            if (success1 == true)
            {
                // await RefreshLoads();
                // await RefreshDispatchOrders();
            }
        }

        private async void OnWeightOut()
        {
            var dialogVm = SimpleIoc.Default.GetInstanceWithoutCaching<WeighOutViewModel>();
            dialogVm.HeaderText = "Record Weigh";
            dialogVm.WeighbridgeRecordId = SelectedPending.id;
            dialogVm.IsOutgoing = !SelectedPending.IsOutgoing;
            bool? success = _dialogService.ShowDialog<WeighOutView>(this, dialogVm);
            if (success == true)
            {
                await RefreshPending();
                await RefreshCompletedDispatchOrders();
                await RefreshDispatchOrders();
            }
        }

        private async void OnRefreshCompleted()
        {
            await RefreshCompletedDispatchOrders();
        }

        private async void OnDeleteLoad()
        {
            if (SelectedLoad == null) return;
            var result = _dialogService.ShowMessageBox(this, "Delete Load?", "", MessageBoxButton.OKCancel);
            if (result == MessageBoxResult.Cancel) return;
            await _dispatchRepo.DeleteLoad(SelectedLoad.id);
            await RefreshLoads();
            await RefreshDispatchOrders();

        }

        private async void OnAddLoad()
        {
            var dialogVm1 = SimpleIoc.Default.GetInstanceWithoutCaching<CreateLoadViewModel>();
            dialogVm1.HeaderText = "Add Load";
            bool? success1 = _dialogService.ShowDialog<CreateLoadView>(this, dialogVm1);
            if (success1 == true)
            {
                await RefreshLoads();
                await RefreshDispatchOrders();
            }
        }

        private async void OnEditLoad()
        {
            if (SelectedLoad == null) return;
            var dialogVm1 = SimpleIoc.Default.GetInstanceWithoutCaching<CreateLoadViewModel>();
            dialogVm1.LoadId = SelectedLoad.id;
            dialogVm1.HeaderText = "Edit Load";
            bool? success1 = _dialogService.ShowDialog<CreateLoadView>(this, dialogVm1);
            if (success1 == true)
            {
                await RefreshLoads();
                await RefreshDispatchOrders();
            }
        }

        private async void OnAddOutgoing()
        {
            var dialogVm = SimpleIoc.Default.GetInstanceWithoutCaching<AddOutgoingViewModel>();
            dialogVm.HeaderText = "Record Incoming Weigh";
            dialogVm.IsOutgoing = true;
            // dialogVm.ScaleTcpConnection = ScaleTcpConnection;
            bool? success = _dialogService.ShowDialog<AddOutgoingView>(this, dialogVm);
            if (success == true)
            {
                await RefreshPending();
                await RefreshDispatchOrders();

            }
        }

        private async void OnAddIncoming()
        {
            // DisposeScale();
            var dialogVm = SimpleIoc.Default.GetInstanceWithoutCaching<AddIncomingViewModel>();
            dialogVm.HeaderText = "Record Incoming Weigh";
            // dialogVm.ScaleTcpConnection = ScaleTcpConnection;
            bool? success = _dialogService.ShowDialog<AddIncomingView>(this, dialogVm);
            if (success == true)
            {
                await RefreshPending();
            }
            // await SetupScale();
        }

        private async void OnDeletePending()
        {
            var result = _dialogService.ShowMessageBox(this, "Delete Pending Item?", "", MessageBoxButton.OKCancel);
            if (result == MessageBoxResult.Cancel) return;
            await _weighbridgeRepo.MarkAsDeletedAsync(SelectedPending.id);
            await RefreshPending();
            SelectedPending = null;
        }

        private async void OnEditPending()
        {
            // var dialogVm = SimpleIoc.Default.GetInstanceWithoutCaching<EditOutgoingViewModel>();
            // dialogVm.WeighbridgeRecordId = SelectedPending.id;
            // bool? success = _dialogService.ShowDialog<EditOutgoingView>(this, dialogVm);
            // if (success == true)
            // {
            //     await RefreshPending();
            //     await RefreshDispatchOrders();
            // }
            if (SelectedPending == null) return;
            var dialogVm1 = SimpleIoc.Default.GetInstanceWithoutCaching<CreateLoadViewModel>();
            dialogVm1.LoadId = SelectedPending.id;
            dialogVm1.HeaderText = "Edit Load";
            bool? success1 = _dialogService.ShowDialog<CreateLoadView>(this, dialogVm1);
            if (success1 == true)
            {
                await RefreshLoads();
                await RefreshDispatchOrders();
            }
        }

        private async Task RefreshPending()
        {
            // Only show records that have actually been weighed in / onsite (DateWeightIn set).
            // A freshly created load has no weigh-in yet, so it stays on the Load list only.
            PendingList = new List<vwWeighbridgePending>(await _weighbridgeRepo.GetWeighbridgeRecordsWeighedIn());
            SelectedPending = null;

        }

        private async void OnAddTransferOrder()
        {
            var dialogVm = SimpleIoc.Default.GetInstanceWithoutCaching<CreateDispatchOrderViewModel>();
            dialogVm.HeaderText = "Create Transfer Order";
            dialogVm.IsTransfer = true;
            bool? success = _dialogService.ShowDialog<CreateDispatchOrderView>(this, dialogVm);
            if (success == true)
            {
                await RefreshDispatchOrders();
            }
        }

        private async void OnCreateDispatchOrder()
        {
            var dialogVm = SimpleIoc.Default.GetInstanceWithoutCaching<CreateDispatchOrderViewModel>();
            dialogVm.HeaderText = "Create Sales Order";
            bool? success = _dialogService.ShowDialog<CreateDispatchOrderView>(this, dialogVm);
            if (success == true)
            {
                await RefreshDispatchOrders();
            }
        }

        private async void OnCompleteDispatchOrder()
        {
            if (!SelectedDispatchOrder.TargetMet)
            {
                var message1 = "Order is not 100% picked.";
                var result1 = _dialogService.ShowMessageBox(this, message1, "", MessageBoxButton.OKCancel);
                return;
            }

            var message = "Complete Dispatch Order?";
            var result = _dialogService.ShowMessageBox(this, message, "", MessageBoxButton.OKCancel);
            if (result == MessageBoxResult.Cancel) return;

            await _dispatchRepo.UpdateDispatchedDateComplete(SelectedDispatchOrder.id, DateTime.Now);
            await RefreshDispatchOrders();
        }

        private async void OnPrintDeliveryDocket()
        {
            var orderView = await _dispatchRepo.GetDispatchOrderView(SelectedDispatchOrder.id, true, false);
            var report1 = new rptDispatch(orderView);
            report1.ShowPrintMarginsWarning = false;
            report1.ShowPreviewDialog();
        }

        private async void UnCompleteDispatchOrder()
        {
            var message = "Re-open Dispatch Order?";


            var result = _dialogService.ShowMessageBox(this, message, "", MessageBoxButton.OKCancel);
            if (result == MessageBoxResult.Cancel) return;

            await _dispatchRepo.UpdateDispatchedDateComplete(SelectedDispatchOrder.id, null);
            await RefreshDispatchOrders();
        }

        private async void OnDeleteDispatchOrder()
        {
            //TODO
            // if (SelectedDispatchOrder.PickedCount > 0)
            // {
            //    _dialogService.ShowMessageBox(this, "You cannot DELETE an order with picked pallets." + Environment.NewLine + "Please remove pallets from the order before deletion", "", MessageBoxButton.OK);
            //     return;
            // }
            var result = _dialogService.ShowMessageBox(this, "Delete Dispatch Order?" + Environment.NewLine + "THIS ORDER WILL BE DELETED PERMANENTLY!", "", MessageBoxButton.OKCancel);
            if (result == MessageBoxResult.Cancel) return;
            await _dispatchRepo.DeleteDispatchOrdeAsync(SelectedDispatchOrder.id);
            await RefreshDispatchOrders();

        }

        private async void OnEditDispatchOrder()
        {
            // if (SelectedDispatchOrder.DateDispatched == null)
            {
                var dialogVm1 = SimpleIoc.Default.GetInstanceWithoutCaching<CreateDispatchOrderViewModel>();
                dialogVm1.DispatchOrderId = SelectedDispatchOrder.id;
                dialogVm1.HeaderText = "Edit Sales Order";
                bool? success1 = _dialogService.ShowDialog<CreateDispatchOrderView>(this, dialogVm1);
                if (success1 == true)
                {
                    await RefreshDispatchOrders();
                }
            }
            // else
            // {
            //     var dialogVm = SimpleIoc.Default.GetInstanceWithoutCaching<EditCompletedDispatchViewModel>();
            //     dialogVm.DispatchOrderId = SelectedDispatchOrder.id;
            //     dialogVm.HeaderText = "Edit Sales Order";
            //     bool? success = _dialogService.ShowDialog<EditCompletedDispatchView>(this, dialogVm);
            //     if (success == true)
            //     {
            //         await RefreshDispatchOrders();
            //     }
            // }
        }

        private async void OnViewCommand()
        {
            var dialogVm = SimpleIoc.Default.GetInstanceWithoutCaching<FullDispatchOrderViewModel>();
            dialogVm.DispatchOrderId = SelectedDispatchOrder.id;
            bool? success = _dialogService.ShowDialog<FullDispatchOrderView>(this, dialogVm);
            if (success == true)
            {
                //await RefreshDispatchOrders();
            }
        }

        private async Task RefreshLoads()
        {
            try
            {
                IsBusy = true;
                var openList = await _dispatchRepo.GetWeighbridgePreLoads();
                LoadList = new List<vwWeighbridgePreLoad>(openList.Where(a => a.HaulierId != 0));
                SelectedLoad = null;
            }
            catch
            {
                IsBusy = false;
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task RefreshDispatchOrders()
        {
            try
            {
                IsBusy = true;
                var openList = await _dispatchRepo.GetOpenDispatchOrderViews();
                // OpenOrderList = new List<vwDispatchOrder>(openList.Where(a => a.HaulierId == 0));
                OpenOrderList = new List<vwDispatchOrder>(openList);
                SelectedDispatchOrder = null;
            }
            catch
            {
                IsBusy = false;
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task RefreshCompletedDispatchOrders()
        {
            try
            {
                IsCompletedOrdersBusy = true;
                var completed = await _weighbridgeRepo.GetCompletedWeighbridgeRecordViews(DateTime.Now.AddDays(-7), DateTime.Now.AddDays(1));
                CompletedWeighbridgeRecordList = new List<vwWeighbridgeComplete>(completed.Where(a => a.IsOutgoing));
                SelectedDispatchOrder = null;
            }
            catch
            {
                IsCompletedOrdersBusy = false;
            }
            finally
            {
                IsCompletedOrdersBusy = false;
            }
        }

        public async void Load(FrameworkElement element)
        {
            await RefreshDispatchOrders();
            await RefreshPending();
            await RefreshCompletedDispatchOrders();
            await RefreshLoads();
            DateFrom = DateTime.Today.AddMonths(-1);
            DateTo = DateTime.Today.AddDays(1);
            SelectedDispatchOrders = new ObservableCollection<vwDispatchOrder>();
        }

        public void Unload(FrameworkElement element)
        {
            OpenOrderList = null;
            LoadList = null;

            SelectedDispatchOrder = null;
            IsCompletedSelected = false;
        }
    }
}

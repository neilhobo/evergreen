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
    public class CompletedOrderListViewModel : AdminViewModelBase, IViewModel
    {
        #region private fields

        private readonly IDispatchOrderRepository _dispatchRepo = SimpleIoc.Default.GetInstance<IDispatchOrderRepository>();
        private readonly IDialogService _dialogService = SimpleIoc.Default.GetInstance<IDialogService>();

        #endregion

        #region Properties

        private List<vwDispatchOrder> _OrderList;
        public List<vwDispatchOrder> OrderList
        {
            get { return _OrderList; }
            set { Set(ref _OrderList, value); }
        }

        private vwDispatchOrder _SelectedOrder;
        public vwDispatchOrder SelectedOrder
        {
            get { return _SelectedOrder; }
            set { Set(ref _SelectedOrder, value); }
        }

        private ObservableCollection<vwDispatchOrder> _SelectedOrders;
        public ObservableCollection<vwDispatchOrder> SelectedOrders
        {
            get { return _SelectedOrders; }
            set { Set(ref _SelectedOrders, value); }
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
                // RefreshDispatchOrders();
            }
        }

        #endregion

        #region Commands

        public RelayCommand RefreshCommand => new RelayCommand(async () => await RefreshDispatchOrders());
        public RelayCommand ViewCommand => new RelayCommand(OnViewCommand);
        public RelayCommand EditDispatchOrderCommand => new RelayCommand(OnEditDispatchOrder);
        public RelayCommand DeleteDispatchOrderCommand => new RelayCommand(OnDeleteDispatchOrder);
        public RelayCommand PrintDeliveryNoteCommand => new RelayCommand(OnPrintDeliveryDocket);

        #endregion

        private async void OnPrintDeliveryDocket()
        {
            var orderView = await _dispatchRepo.GetDispatchOrderView(SelectedOrder.id, true, false);
            var report1 = new rptDispatch(orderView);
            report1.ShowPrintMarginsWarning = false;
            report1.ShowPreviewDialog();
        }

        private async void OnDeleteDispatchOrder()
        {
            //TODO
            // if (SelectedOrder.PickedCount > 0)
            // {
            //    _dialogService.ShowMessageBox(this, "You cannot DELETE an order with picked pallets." + Environment.NewLine + "Please remove pallets from the order before deletion", "", MessageBoxButton.OK);
            //     return;
            // }
            var result = _dialogService.ShowMessageBox(this, "Delete Dispatch Order?" + Environment.NewLine + "THIS ORDER WILL BE DELETED PERMANENTLY!", "", MessageBoxButton.OKCancel);
            if (result == MessageBoxResult.Cancel) return;
            await _dispatchRepo.DeleteDispatchOrdeAsync(SelectedOrder.id);
            await RefreshDispatchOrders();

        }

        private async void OnEditDispatchOrder()
        {
            // if (SelectedOrder.DateDispatched == null)
            {
                var dialogVm1 = SimpleIoc.Default.GetInstanceWithoutCaching<CreateDispatchOrderViewModel>();
                dialogVm1.DispatchOrderId = SelectedOrder.id;
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
            //     dialogVm.DispatchOrderId = SelectedOrder.id;
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
            var dialogVm = SimpleIoc.Default.GetInstanceWithoutCaching<CreateDispatchOrderViewModel>();
            dialogVm.DispatchOrderId = SelectedOrder.id;
            dialogVm.ReadOnlyMode = true;
            bool? success = _dialogService.ShowDialog<CreateDispatchOrderView>(this, dialogVm);
            if (success == true)
            {

            }
        }

        private async Task RefreshDispatchOrders()
        {
            try
            {
                IsBusy = true;
                OrderList = new List<vwDispatchOrder>(await _dispatchRepo.GetCompletedDispatchOrderViews());
                SelectedOrder = null;
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

        public async void Load(FrameworkElement element)
        {
            await RefreshDispatchOrders();
            DateFrom = DateTime.Today.AddMonths(-1);
            DateTo = DateTime.Today.AddDays(1);
            SelectedOrders = new ObservableCollection<vwDispatchOrder>();
        }

        public void Unload(FrameworkElement element)
        {
            OrderList = null;
            SelectedOrder = null;
            IsCompletedSelected = false;
        }
    }
}

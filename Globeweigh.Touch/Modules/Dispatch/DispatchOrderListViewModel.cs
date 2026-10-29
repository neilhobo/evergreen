using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using GalaSoft.MvvmLight.Ioc;
using Globeweigh.Model;
using Globeweigh.Shared;
using System.Windows;
using GalaSoft.MvvmLight.CommandWpf;
using Globeweigh.Shared.Services;
using IDialogService = MvvmDialogs.IDialogService;

namespace Globeweigh.Touch
{
    public class DispatchOrderListViewModel : TouchViewModelBase, IViewModel
    {
        #region private fields

        private readonly IDialogService _dialogService = SimpleIoc.Default.GetInstance<IDialogService>();
        private readonly IDispatchOrderRepository _dispatchRepo = SimpleIoc.Default.GetInstance<IDispatchOrderRepository>();
        private readonly IWeighbridgeRepository _weighbridgeRepo = SimpleIoc.Default.GetInstance<IWeighbridgeRepository>();


        #endregion

        #region Properties

        private List<vwWeighbridgePending> _PendingList;
        public List<vwWeighbridgePending> PendingList
        {
            get { return _PendingList; }
            set { Set(ref _PendingList, value); }
        }

        private vwWeighbridgePending _SelectedPending;
        public vwWeighbridgePending SelectedPending
        {
            get { return _SelectedPending; }
            set
            {
                Set(ref _SelectedPending, value);
                SelectedPendingChanged();
            }
        }

        private List<vwDispatchOrder> _DispatchOrderList;
        public List<vwDispatchOrder> DispatchOrderList
        {
            get { return _DispatchOrderList; }
            set { Set(ref _DispatchOrderList, value); }
        }

        private vwDispatchOrder _SelectedDispatchOrder;
        public vwDispatchOrder SelectedDispatchOrder
        {
            get { return _SelectedDispatchOrder; }
            set { Set(ref _SelectedDispatchOrder, value); }
        }

        private bool _IsPendingBusy;
        public bool IsPendingBusy
        {
            get { return _IsPendingBusy; }
            set { Set(ref _IsPendingBusy, value); }
        }


        #endregion

        #region Events



        #endregion

        #region Commands

        public RelayCommand OrderDetailsCommand => new RelayCommand(OnOrderDetails);
        public RelayCommand HomeCommand => new RelayCommand(OnHome);

        #endregion

        private async void SelectedPendingChanged()
        {
            if(SelectedPending == null)return;
            await RefreshDispatchOrders();
        }

        private async Task RefreshPending()
        {
            try
            {
                IsPendingBusy = true;
                // PendingErrorFound = false;
                PendingList = new List<vwWeighbridgePending>(await _weighbridgeRepo.GetPendingWeighbridgeRecords());
                IsPendingBusy = false;
            }
            catch (Exception e)
            {
                IsPendingBusy = false;
                // PendingErrorFound = true;
                ExceptionHelper.LogError(e, "RefreshPending");
            }
        }

        private void OnHome()
        {
            IsBusy = true;
            SimpleIoc.Default.GetInstance<MainWindowViewModel>().CurrentViewModel = SimpleIoc.Default.GetInstance<HomeViewModel>();
        }

        private async void OnOrderDetails()
        {
            var vm = SimpleIoc.Default.GetInstance<DispatchOrderDetailViewModel>();
            vm.DispatchOrderId = SelectedDispatchOrder.id;
            SimpleIoc.Default.GetInstance<MainWindowViewModel>().CurrentViewModel = vm;
        }

        private async Task RefreshDispatchOrders()
        {
            if(SelectedPending == null)return;
            try
            {
                IsBusy = true;
                DispatchOrderList = new List<vwDispatchOrder>(await _dispatchRepo.GetDispatchOrderViewsByWeighbridgeRecordId(SelectedPending.id));
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

        public async void Load(FrameworkElement element)
        {
            await RefreshPending();
        }

        public void Unload(FrameworkElement element)
        {
            PendingList = null;
            SelectedPending = null;
            DispatchOrderList = null;
            SelectedDispatchOrder = null;
            IsBusy = false;
        }
    }
}

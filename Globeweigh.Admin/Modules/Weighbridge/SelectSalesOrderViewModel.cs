using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO.Ports;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using MvvmDialogs;
using System.Windows;
using System.Windows.Threading;
using GalaSoft.MvvmLight.CommandWpf;
using Globeweigh.Model;
using GalaSoft.MvvmLight.Ioc;
using GalaSoft.MvvmLight.Threading;
using Globeweigh.Shared;
using Globeweigh.Shared.Helpers;
using Globeweigh.Shared.Services;

namespace Globeweigh.Admin
{
    public class SelectSalesOrderViewModel : DialogViewModelBase, IModalDialogViewModel, IViewModel
    {
        #region private fields

        private readonly IDispatchOrderRepository _dispatchRepo = SimpleIoc.Default.GetInstance<IDispatchOrderRepository>();
        private readonly IWeighbridgeRepository _weighbridgeRepo = SimpleIoc.Default.GetInstance<IWeighbridgeRepository>();

        #endregion

        #region Properties

        // public int PendingRecordId { get; set; }

        private ObservableCollection<vwOpenDispatchOrderUnnasigned> _DispatchOrderList;
        public ObservableCollection<vwOpenDispatchOrderUnnasigned> DispatchOrderList
        {
            get { return _DispatchOrderList; }
            set { Set(ref _DispatchOrderList, value); }
        }

        private vwWeighbridgePending _SelectedPending;
        public vwWeighbridgePending SelectedPending
        {
            get { return _SelectedPending; }
            set { Set(ref _SelectedPending, value); }
        }


        #endregion

        #region Commands

        public RelayCommand SaveCommand => new RelayCommand(OnSave, CanSave);

        #endregion

        private async Task RefreshDispatchOrders()
        {
            try
            {
                IsBusy = true;
                DispatchOrderList = new ObservableCollection<vwOpenDispatchOrderUnnasigned>(await _dispatchRepo.GetOpenDispatchOrderForLinking(SelectedPending.HaulierId));
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

        private async void OnSave()
        {
            // await _weighbridgeRepo.UpdateRecordAssigned(SelectedPending.id, DispatchOrderList.Where(a=>a.IsSelected).ToList());
            DialogResult = true;
        }

        private bool CanSave()
        {
            if (DispatchOrderList == null || !DispatchOrderList.Any(a=>a.IsSelected)) return false;
            return true;
        }

        public async void Load(FrameworkElement element)
        {
            if (UtilitiesShared.InDesignMode) return;
            await RefreshDispatchOrders();
        }

        public void Unload(FrameworkElement element)
        {
            SelectedPending = null;
            DispatchOrderList = null;
            DialogResult = null;
        }
    }
}

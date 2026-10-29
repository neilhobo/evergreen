using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MvvmDialogs;
using System.Windows;
using System.Windows.Threading;
using GalaSoft.MvvmLight.CommandWpf;
using Globeweigh.Model;
using GalaSoft.MvvmLight.Ioc;
using Globeweigh.Shared;
using Globeweigh.Shared.Helpers;
using Globeweigh.Shared.Network;
using Globeweigh.Shared.Services;
using System.Net;
using System.Text;
using System.Net.Sockets;
using System.Collections.ObjectModel;
using System.Linq;
using DevExpress.Mvvm.Native;

namespace Globeweigh.Admin
{
    public class WeighOutViewModel : DialogViewModelBase, IModalDialogViewModel, IViewModel
    {
        #region private fields

        private readonly IWeighbridgeRepository _weighbridgeRepo = SimpleIoc.Default.GetInstance<IWeighbridgeRepository>();
        private readonly IReferenceDataRepository _refDataRepo = SimpleIoc.Default.GetInstance<IReferenceDataRepository>();
        private readonly IHaulierRepository _haulierRepo = SimpleIoc.Default.GetInstance<IHaulierRepository>();
        private readonly IDialogService _dialogService = SimpleIoc.Default.GetInstance<IDialogService>();
        private readonly IDispatchOrderRepository _dispatchRepo = SimpleIoc.Default.GetInstance<IDispatchOrderRepository>();
        private readonly IScaleService _scaleService = SimpleIoc.Default.GetInstance<IScaleService>();
        private EventHandler<string> _weightHandler;

        // private DispatcherTimer _timerRelay;
        #endregion

        #region Properties

        public int WeighbridgeRecordId { get; set; }

        private TcpClient _RelayTcpConnection;
        public TcpClient RelayTcpConnection
        {
            get { return _RelayTcpConnection; }
            set { Set(ref _RelayTcpConnection, value); }
        }

        private List<vwDispatchOrder> _DispatchOrderList;
        public List<vwDispatchOrder> DispatchOrderList
        {
            get { return _DispatchOrderList; }
            set { Set(ref _DispatchOrderList, value); }
        }


        private string _ManualWeight;
        public string ManualWeight
        {
            get { return _ManualWeight; }
            set { Set(ref _ManualWeight, value); }
        }

        private bool _IsManual;
        public bool IsManual
        {
            get { return _IsManual; }
            set { Set(ref _IsManual, value); }
        }


        private bool _IsOutgoing;
        public bool IsOutgoing
        {
            get { return _IsOutgoing; }
            set { Set(ref _IsOutgoing, value); }
        }

        private WeighbridgeRecord _CurrentWeighbridgeRecord;
        public WeighbridgeRecord CurrentWeighbridgeRecord
        {
            get { return _CurrentWeighbridgeRecord; }
            private set { Set(ref _CurrentWeighbridgeRecord, value); }
        }

        private vwWeighbridgePending _CurrentWeighbridgeRecordView;
        public vwWeighbridgePending CurrentWeighbridgeRecordView
        {
            get { return _CurrentWeighbridgeRecordView; }
            set { Set(ref _CurrentWeighbridgeRecordView, value); }
        }


        private string _LiveWeight;
        public string LiveWeight
        {
            get { return _LiveWeight; }
            set { Set(ref _LiveWeight, value); }
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
                DispatchOrderList = new List<vwDispatchOrder>(await _dispatchRepo.GetDispatchOrderViewsByWeighbridgeRecordId(WeighbridgeRecordId));
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
            int weight = 0;
            if (IsManual) { weight = Convert.ToInt32(ManualWeight); }
            else { weight = Convert.ToInt32(LiveWeight); }

            CurrentWeighbridgeRecord.WeightOut = weight;
            CurrentWeighbridgeRecord.DateWeightOut = DateTime.Now;

            await _weighbridgeRepo.CompleteWeighBridgeRecord(CurrentWeighbridgeRecord);

            DialogResult = true;
        }

        private bool CanSave()
        {
            if (CurrentWeighbridgeRecord == null) return false;
            if (CurrentWeighbridgeRecord.HaulierId == 0) return false;
            // if (InEditMode) return false;
            if (IsManual)
            {
                if (string.IsNullOrEmpty(ManualWeight)) return false;
            }
            else
            {
                if (string.IsNullOrEmpty(LiveWeight)) return false;
            }
            return true;
        }

        private void OnWeightChanged(object sender, string weight)
        {
            LiveWeight = weight;
        }

        public async void Load(FrameworkElement element)
        {
            if (UtilitiesShared.InDesignMode) return;
            HeaderText = "Weight Out";
            CurrentWeighbridgeRecord = await _weighbridgeRepo.GetWeighbridgeRecord(WeighbridgeRecordId,  false, false);
            CurrentWeighbridgeRecordView = await _weighbridgeRepo.GetWeighbridgeRecordPendingView(WeighbridgeRecordId);
            await RefreshDispatchOrders();

            _weightHandler = OnWeightChanged;
            _scaleService.WeightChanged += _weightHandler;
            await _scaleService.EnsureConnectedAsync();
            LiveWeight = _scaleService.CurrentWeight;
        }

        public void Unload(FrameworkElement element)
        {
            if (_weightHandler != null)
            {
                _scaleService.WeightChanged -= _weightHandler;
                _weightHandler = null;
            }

            CurrentWeighbridgeRecord = null;
            WeighbridgeRecordId = 0;
            DialogResult = null;
            IsOutgoing = false;
            IsManual = false;
        }
    }
}

using GalaSoft.MvvmLight.CommandWpf;
using GalaSoft.MvvmLight.Ioc;
using Globeweigh.Model;
using Globeweigh.Shared;
using Globeweigh.Shared.Helpers;
using Globeweigh.Shared.Services;
using MvvmDialogs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace Globeweigh.Touch
{
    /// <summary>
    /// Collection weigh screen. Mirrors <see cref="WeighbridgeWeighViewModel"/> for the live-weight
    /// behaviour, but has no docket entry, uses TrailerNo instead of Vehicle Reg, and shows the
    /// loaded weighbridge record together with the orders assigned to it.
    /// </summary>
    public class CollectionWeighViewModel : TouchViewModelBase, IViewModel
    {
        private readonly IDialogService _dialogService = SimpleIoc.Default.GetInstance<IDialogService>();
        private readonly IWeighbridgeRepository _weighRepo = SimpleIoc.Default.GetInstance<IWeighbridgeRepository>();
        private readonly IScaleService _scaleService;
        private readonly IRelayService _relay;

        private EventHandler<bool> _barrierHandler;
        private EventHandler<string> _weightHandler;
        private DispatcherTimer _scaleWatchdog;

        private DispatcherTimer _countdownTimer;
        private void EnsureCountdownTimer()
        {
            if (_countdownTimer != null) return;
            _countdownTimer = new DispatcherTimer();
            _countdownTimer.Interval = TimeSpan.FromSeconds(1);
            _countdownTimer.Tick += CountdownTimer_Tick;
        }

        public CollectionWeighViewModel(IScaleService scaleService, IRelayService relay)
        {
            _scaleService = scaleService;
            _relay = relay;

            _weightHandler = OnWeightChanged;
            _scaleService.WeightChanged += _weightHandler;

            _barrierHandler = delegate (object s, bool open) { BarrierOpen = open; };
            _relay.BarrierOpenChanged += _barrierHandler;

            _scaleWatchdog = new DispatcherTimer();
            _scaleWatchdog.Interval = TimeSpan.FromSeconds(2);
            _scaleWatchdog.Tick += ScaleWatchdog_Tick;
        }

        #region Properties

        // Set by CollectionInViewModel before navigation.
        public int WeighbridgeRecordId { get; set; }

        private WeighbridgeRecord _CurrentWeighbridgeRecord;
        public WeighbridgeRecord CurrentWeighbridgeRecord
        {
            get { return _CurrentWeighbridgeRecord; }
            set { Set(ref _CurrentWeighbridgeRecord, value); }
        }

        // The 3-digit reference given to the haulier (from the load view, zero-padded).
        private string _IdRef;
        public string IdRef
        {
            get { return _IdRef; }
            set { Set(ref _IdRef, value); }
        }

        // Loads don't store HaulierName on the record; it comes from the joined load view.
        private string _HaulierName;
        public string HaulierName
        {
            get { return _HaulierName; }
            set { Set(ref _HaulierName, value); }
        }

        private List<vwDispatchOrder> _AssignedOrders;
        public List<vwDispatchOrder> AssignedOrders
        {
            get { return _AssignedOrders; }
            set { Set(ref _AssignedOrders, value); }
        }

        private bool _BarrierOpen;
        public bool BarrierOpen
        {
            get { return _BarrierOpen; }
            set { Set(ref _BarrierOpen, value); }
        }

        private string _LiveWeight;
        public string LiveWeight
        {
            get { return _LiveWeight; }
            set { Set(ref _LiveWeight, value); }
        }

        private bool _SuccessOverlayVisible;
        public bool SuccessOverlayVisible
        {
            get { return _SuccessOverlayVisible; }
            set { Set(ref _SuccessOverlayVisible, value); }
        }

        private bool _InCountdownMode;
        public bool InCountdownMode
        {
            get { return _InCountdownMode; }
            set { Set(ref _InCountdownMode, value); }
        }

        private int _CountdownValue;
        public int CountdownValue
        {
            get { return _CountdownValue; }
            set { Set(ref _CountdownValue, value); }
        }

        private bool _ScaleHealthy;
        public bool ScaleHealthy
        {
            get => _ScaleHealthy;
            set
            {
                Set(ref _ScaleHealthy, value);
                SaveCommand.RaiseCanExecuteChanged();
            }
        }

        private DateTime _lastLiveWeightReceived = DateTime.MinValue;

        #endregion

        #region Commands

        private RelayCommand _saveCommand;
        public RelayCommand SaveCommand =>
            _saveCommand ?? (_saveCommand = new RelayCommand(OnSave, CanSave));

        private RelayCommand _backCommand;
        public RelayCommand BackCommand =>
            _backCommand ?? (_backCommand = new RelayCommand(OnBack));

        #endregion

        #region Lifecycle

        private async Task WaitForFreshScaleReadingAsync(TimeSpan timeout)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();

            while (sw.Elapsed < timeout)
            {
                bool connected = _scaleService.IsConnected;
                bool fresh = _scaleService.LastUpdateUtc > DateTime.UtcNow.AddSeconds(-2);
                bool receivedByThisView = _lastLiveWeightReceived > DateTime.UtcNow.AddSeconds(-2);

                if (connected && fresh && receivedByThisView)
                {
                    ScaleHealthy = true;
                    LiveWeight = _scaleService.CurrentWeight;
                    return;
                }

                await Task.Delay(200);
            }

            ScaleHealthy = false;
        }

        public async void Load(FrameworkElement element)
        {
            try
            {
                if (WeighbridgeRecordId != 0)
                {
                    // includeOrderViews=true populates OrderViewList - the same source the Admin
                    // "View Load" screen uses, which reliably lists the orders on the load.
                    CurrentWeighbridgeRecord =
                        await _weighRepo.GetWeighbridgeRecord(WeighbridgeRecordId, true, false);

                    AssignedOrders = new List<vwDispatchOrder>(
                        CurrentWeighbridgeRecord?.OrderViewList ?? new List<vwDispatchOrder>());

                    // Haulier name / reference live on the joined load view, not the raw record.
                    var loadView = await _weighRepo.GetWeighbridgeRecordPreloadView(WeighbridgeRecordId);
                    HaulierName = loadView?.HaulierName;
                    IdRef = loadView?.IdRef
                            ?? (WeighbridgeRecordId % 1000).ToString("D3");
                }

                if (CurrentWeighbridgeRecord != null)
                    CurrentWeighbridgeRecord.RaisePropertiesChangedManual();

                // -------------------------------------------------
                // SCALE SAFEGUARD SECTION
                // -------------------------------------------------
                LiveWeight = string.Empty;
                ScaleHealthy = false;

                if (_weightHandler != null)
                    _scaleService.WeightChanged -= _weightHandler;

                _weightHandler = OnWeightChanged;
                _scaleService.WeightChanged += _weightHandler;

                await _scaleService.EnsureConnectedAsync();
                await WaitForFreshScaleReadingAsync(TimeSpan.FromSeconds(3));

                if (!ScaleHealthy)
                    LiveWeight = _scaleService.CurrentWeight;

                if (_scaleWatchdog != null)
                    _scaleWatchdog.Start();

                // -------------------------------------------------
                // BARRIER SECTION
                // -------------------------------------------------
                await _relay.EnsureConnectedAsync();
                await _relay.RefreshBarrierStateAsync();
                BarrierOpen = _relay.BarrierOpen;
            }
            catch (Exception ex)
            {
                ExceptionHelper.LogError(ex, "CollectionWeighViewModel.Load");
            }
        }

        public void Unload(FrameworkElement element)
        {
            if (_weightHandler != null)
                _scaleService.WeightChanged -= _weightHandler;

            if (_scaleWatchdog != null)
                _scaleWatchdog.Stop();

            if (_barrierHandler != null)
                _relay.BarrierOpenChanged -= _barrierHandler;

            if (_countdownTimer != null)
                _countdownTimer.Stop();

            LiveWeight = string.Empty;
            ScaleHealthy = false;
            _lastLiveWeightReceived = DateTime.MinValue;
            CurrentWeighbridgeRecord = null;
            AssignedOrders = null;
            HaulierName = null;
            IdRef = null;
            WeighbridgeRecordId = 0;
        }

        #endregion

        #region Handlers

        private void OnWeightChanged(object sender, string weight)
        {
            LiveWeight = weight;
            _lastLiveWeightReceived = DateTime.UtcNow;
            ScaleHealthy = true;
        }

        private async void ScaleWatchdog_Tick(object sender, EventArgs e)
        {
            try
            {
                bool stale =
                    (DateTime.UtcNow - _scaleService.LastUpdateUtc) > TimeSpan.FromSeconds(3);

                if (!_scaleService.IsConnected || stale)
                    await _scaleService.EnsureConnectedAsync();

                ScaleHealthy =
                    _scaleService.IsConnected &&
                    (DateTime.UtcNow - _scaleService.LastUpdateUtc) < TimeSpan.FromSeconds(3);

                if (ScaleHealthy)
                    LiveWeight = _scaleService.CurrentWeight;

                SaveCommand.RaiseCanExecuteChanged();
            }
            catch (Exception ex)
            {
                ScaleHealthy = false;
                ExceptionHelper.LogError(ex, "CollectionWeighViewModel.ScaleWatchdog_Tick");
            }
        }

        private void OnBack()
        {
            SimpleIoc.Default.GetInstance<MainWindowViewModel>().CurrentViewModel =
                SimpleIoc.Default.GetInstance<CollectionInViewModel>();
        }

        private bool CanSave()
        {
            if (CurrentWeighbridgeRecord == null) return false;

            if (!ScaleHealthy) return false;
            if (!_scaleService.IsConnected) return false;

            bool stale =
                (DateTime.UtcNow - _scaleService.LastUpdateUtc) > TimeSpan.FromSeconds(3);
            if (stale) return false;

            if (string.IsNullOrEmpty(CurrentWeighbridgeRecord.TrailerNo)) return false;

            return true;
        }

        private async void OnSave()
        {
            if (!_scaleService.IsConnected)
            {
                MessageBox.Show("Scale is not connected.");
                return;
            }

            if ((DateTime.UtcNow - _scaleService.LastUpdateUtc) > TimeSpan.FromSeconds(3))
            {
                MessageBox.Show("Scale weight is not live. Please wait.");
                return;
            }

            if (IsBusy) return;

            try
            {
                IsBusy = true;

                int weight = ParseSafeInt(_scaleService.CurrentWeight);
                LiveWeight = _scaleService.CurrentWeight;

                if (CurrentWeighbridgeRecord.DateWeightIn == null)
                {
                    CurrentWeighbridgeRecord.WeightIn = weight;
                    CurrentWeighbridgeRecord.DateWeightIn = DateTime.Now;
                }
                else
                {
                    CurrentWeighbridgeRecord.WeightOut = weight;
                    CurrentWeighbridgeRecord.DateWeightOut = DateTime.Now;
                }

                await _weighRepo.AddUpdateAsync(CurrentWeighbridgeRecord);
                IsBusy = false;

                if (UtilitiesShared.IsMyMachine)
                {
                    StartCountdownAndNavigate();
                    return;
                }

                await RunPostWeighFlowAsync();
            }
            catch (Exception e)
            {
                IsBusy = false;
                ExceptionHelper.ShowErrorWindow(e, "Collection weigh", false);
            }
        }

        private void StartCountdownAndNavigate()
        {
            EnsureCountdownTimer();
            CountdownValue = 5;
            InCountdownMode = true;

            _countdownTimer.Stop();
            _countdownTimer.Start();
        }

        private void CountdownTimer_Tick(object sender, EventArgs e)
        {
            if (CountdownValue > 1)
            {
                CountdownValue--;
                return;
            }

            _countdownTimer.Stop();
            InCountdownMode = false;

            SimpleIoc.Default.GetInstance<MainWindowViewModel>().CurrentViewModel =
                SimpleIoc.Default.GetInstance<WeighbridgeInViewModel>();
        }

        public void MouseUpCommand(string name)
        {
            if (name == "TrailerNo")
            {
                var dialogViewModel = SimpleIoc.Default.GetInstance<KeyboardViewModel>();
                dialogViewModel.HeaderText = "Enter Trailer No";
                if (!UtilitiesShared.IsMyMachine) dialogViewModel.IsMaximized = true;
                bool? success = _dialogService.ShowDialog<KeyboardView>(this, dialogViewModel);
                if (success == true)
                {
                    CurrentWeighbridgeRecord.TrailerNo = dialogViewModel.KeyboardValue;
                }
            }

            SaveCommand.RaiseCanExecuteChanged();
        }

        #endregion

        #region Post-weigh flow

        private async Task RunPostWeighFlowAsync()
        {
            SuccessOverlayVisible = true;

            try
            {
                if (GlobalVariables.GlobalDbSettings.GateEnabled)
                {
                    await _relay.EnsureConnectedAsync();
                    if (!_relay.IsConnected)
                    {
                        ExceptionHelper.LogError(null, "Relay not connected in CollectionWeighViewModel.RunPostWeighFlowAsync");
                    }
                    else
                    {
                        if (!_relay.BarrierOpen)
                        {
                            await _relay.SetBarrierAsync(true);
                            BarrierOpen = _relay.BarrierOpen;
                        }

                        await WaitUntilWeightBelowAsync(150, TimeSpan.FromSeconds(2), TimeSpan.FromMinutes(60));

                        await _relay.SetBarrierAsync(false);
                        BarrierOpen = _relay.BarrierOpen;
                    }
                }
            }
            catch (Exception ex)
            {
                ExceptionHelper.LogError(ex, "CollectionWeighViewModel.RunPostWeighFlowAsync");
            }
            finally
            {
                SuccessOverlayVisible = false;

                SimpleIoc.Default.GetInstance<MainWindowViewModel>().CurrentViewModel =
                    SimpleIoc.Default.GetInstance<WeighbridgeInViewModel>();
            }
        }

        private async Task WaitUntilWeightBelowAsync(int thresholdKg, TimeSpan stableFor, TimeSpan timeout)
        {
            var total = new System.Diagnostics.Stopwatch();
            var stable = new System.Diagnostics.Stopwatch();
            total.Start();

            while (total.Elapsed < timeout)
            {
                int w;
                int.TryParse((_scaleService.CurrentWeight ?? string.Empty).Trim(), out w);

                if (w < thresholdKg)
                {
                    if (!stable.IsRunning) stable.Start();
                    if (stable.Elapsed >= stableFor) return;
                }
                else
                {
                    if (stable.IsRunning) stable.Reset();
                }

                await Task.Delay(100);
            }
        }

        private static int ParseSafeInt(string s)
        {
            int v;
            if (int.TryParse((s ?? string.Empty).Trim(), out v)) return v;
            return 0;
        }

        #endregion
    }
}

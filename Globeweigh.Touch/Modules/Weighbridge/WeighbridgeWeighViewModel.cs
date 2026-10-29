using GalaSoft.MvvmLight.CommandWpf;
using GalaSoft.MvvmLight.Ioc;
using Globeweigh.Model;
using Globeweigh.Shared;
using Globeweigh.Shared.Helpers;
using Globeweigh.Shared.Services;
using MvvmDialogs;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace Globeweigh.Touch
{
    public class WeighbridgeWeighViewModel : TouchViewModelBase, IViewModel
    {
        private readonly IDialogService _dialogService = SimpleIoc.Default.GetInstance<IDialogService>();
        private readonly IWeighbridgeRepository _weighRepo = SimpleIoc.Default.GetInstance<IWeighbridgeRepository>();
        private readonly IScaleService _scaleService;
        private readonly IRelayService _relay;
        // public bool IsOut { get; set; }

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

        public WeighbridgeWeighViewModel(IScaleService scaleService, IRelayService relay)
        {
            _scaleService = scaleService;
            _relay = relay;

            _weightHandler = OnWeightChanged;
            _scaleService.WeightChanged += _weightHandler;

            _barrierHandler = delegate (object s, bool open) { BarrierOpen = open; };
            _relay.BarrierOpenChanged += _barrierHandler;

            // Lightweight watchdog to keep the scale connection healthy
            _scaleWatchdog = new DispatcherTimer();
            _scaleWatchdog.Interval = TimeSpan.FromSeconds(2);
            _scaleWatchdog.Tick += ScaleWatchdog_Tick;
        }

        #region Properties
        
        private vwWeighbridgePending _SelectedPending;
        public vwWeighbridgePending SelectedPending
        {
            get { return _SelectedPending; }
            set { Set(ref _SelectedPending, value); }
        }

        private WeighbridgeRecord _CurrentWeighbridgeRecord;
        public WeighbridgeRecord CurrentWeighbridgeRecord
        {
            get { return _CurrentWeighbridgeRecord; }
            set { Set(ref _CurrentWeighbridgeRecord, value); }
        }

        private bool _IsOut;
        public bool IsOut
        {
            get { return _IsOut; }
            set { Set(ref _IsOut, value); }
        }

        private bool? _isDocketSelected;
        public bool? IsDocketSelected
        {
            get => _isDocketSelected;
            set
            {
                var old = _isDocketSelected;
                Set(ref _isDocketSelected, value);   // returns void in your project

                // run logic only when the value actually changed
                if (old != value && value == false && CurrentWeighbridgeRecord != null)
                {
                    if (!string.IsNullOrEmpty(CurrentWeighbridgeRecord.DocketNumber))
                    {
                        CurrentWeighbridgeRecord.DocketNumber = null;
                    }
                }
            }
        }

        private bool _NotApplicableChecked;
        public bool NotApplicableChecked
        {
            get { return _NotApplicableChecked; }
            set { Set(ref _NotApplicableChecked, value); }
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

        // Fullscreen green overlay shown after weigh; hides once barrier is closed
        private bool _SuccessOverlayVisible;
        public bool SuccessOverlayVisible
        {
            get { return _SuccessOverlayVisible; }
            set { Set(ref _SuccessOverlayVisible, value); }
        }

        // NEW: countdown overlay for Out flow
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

                bool fresh =
                    _scaleService.LastUpdateUtc > DateTime.UtcNow.AddSeconds(-2);

                bool receivedByThisView =
                    _lastLiveWeightReceived > DateTime.UtcNow.AddSeconds(-2);

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
            NotApplicableChecked = false;

            try
            {
                // Load record first
                if (SelectedPending != null)
                {
                    CurrentWeighbridgeRecord =
                        await _weighRepo.GetWeighbridgeRecord(SelectedPending.id, false, false);
                }

                if (CurrentWeighbridgeRecord != null)
                    CurrentWeighbridgeRecord.RaisePropertiesChangedManual();

                // -------------------------------------------------
                // SCALE SAFEGUARD SECTION
                // -------------------------------------------------

                // Reset UI until we confirm live data
                LiveWeight = string.Empty;
                ScaleHealthy = false;

                // Re-subscribe in case old handler was lost/frozen
                if (_weightHandler != null)
                    _scaleService.WeightChanged -= _weightHandler;

                _weightHandler = OnWeightChanged;
                _scaleService.WeightChanged += _weightHandler;

                // Ensure connection
                await _scaleService.EnsureConnectedAsync();

                await WaitForFreshScaleReadingAsync(TimeSpan.FromSeconds(3));

                // If still no fresh data, show cached weight but mark unhealthy
                if (!ScaleHealthy)
                {
                    LiveWeight = _scaleService.CurrentWeight;
                }

                // Start watchdog
                if (_scaleWatchdog != null)
                    _scaleWatchdog.Start();

                // -------------------------------------------------
                // BARRIER SECTION
                // -------------------------------------------------
                if (!IsOut)
                {
                    await _relay.EnsureConnectedAsync();
                    await _relay.RefreshBarrierStateAsync();
                    BarrierOpen = _relay.BarrierOpen;
                }
            }
            catch (Exception ex)
            {
                ExceptionHelper.LogError(ex, "WeighbridgeWeighViewModel.Load");
            }
        }

        public void Unload(FrameworkElement element)
        {
            // Remove scale event handler
            if (_weightHandler != null)
            {
                _scaleService.WeightChanged -= _weightHandler;
            }

            if (_scaleWatchdog != null)
            {
                _scaleWatchdog.Stop();
            }

            // Remove relay handler
            if (_barrierHandler != null)
            {
                _relay.BarrierOpenChanged -= _barrierHandler;
            }

            // Stop countdown timer if active
            if (_countdownTimer != null)
            {
                _countdownTimer.Stop();
            }

            // Reset scale UI state
            LiveWeight = string.Empty;
            ScaleHealthy = false;

            // Reset page state
            _lastLiveWeightReceived = DateTime.MinValue;
            CurrentWeighbridgeRecord = null;
            SelectedPending = null;
            NotApplicableChecked = false;
            IsDocketSelected = null;
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
                    (DateTime.UtcNow - _scaleService.LastUpdateUtc) >
                    TimeSpan.FromSeconds(3);

                // Reconnect if needed
                if (!_scaleService.IsConnected || stale)
                {
                    await _scaleService.EnsureConnectedAsync();
                }

                // Recalculate health
                ScaleHealthy =
                    _scaleService.IsConnected &&
                    (DateTime.UtcNow - _scaleService.LastUpdateUtc) <
                    TimeSpan.FromSeconds(3);

                // If healthy, refresh displayed weight from service
                if (ScaleHealthy)
                {
                    LiveWeight = _scaleService.CurrentWeight;
                }

                // Refresh Save button state
                SaveCommand.RaiseCanExecuteChanged();
            }
            catch (Exception ex)
            {
                ScaleHealthy = false;
                ExceptionHelper.LogError(ex, "WeighbridgeWeighViewModel.ScaleWatchdog_Tick");
            }
        }

        private void OnBack()
        {
            if (SelectedPending != null)
            {
                SimpleIoc.Default.GetInstance<MainWindowViewModel>().CurrentViewModel =
                    SimpleIoc.Default.GetInstance<WeighbridgeOutViewModel>();
            }
            else
            {
                SimpleIoc.Default.GetInstance<MainWindowViewModel>().CurrentViewModel =
                    SimpleIoc.Default.GetInstance<DeliveryInViewModel>();
            }
        }

        private bool CanSave()
        {
            if (CurrentWeighbridgeRecord == null) return false;

            if (!ScaleHealthy) return false;
            if (!_scaleService.IsConnected) return false;

            bool stale =
                (DateTime.UtcNow - _scaleService.LastUpdateUtc) > TimeSpan.FromSeconds(3);

            if (stale) return false;

            if (string.IsNullOrEmpty(CurrentWeighbridgeRecord.VehicleReg)) return false;

            if (!IsOut)
            {
                if (IsDocketSelected == null) return false;
                if (IsDocketSelected == true &&
                    string.IsNullOrEmpty(CurrentWeighbridgeRecord.DocketNumber))
                    return false;
            }

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
                if (CurrentWeighbridgeRecord == null)
                    CurrentWeighbridgeRecord = new WeighbridgeRecord();

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

                if (IsOut)
                {
                    StartCountdownAndNavigate();
                }
                else
                {
                    if (UtilitiesShared.IsMyMachine)
                    {
                        StartCountdownAndNavigate();
                        return;
                    }
                    await RunPostWeighFlowAsync(); // overlay + barrier control + navigate
                }


            }
            catch (Exception e)
            {
                IsBusy = false;
                ExceptionHelper.ShowErrorWindow(e, "Add Weighbridge record", false);
            }
        }

        private void StartCountdownAndNavigate()
        {
            EnsureCountdownTimer();
            CountdownValue = 5;
            InCountdownMode = true;

            // make sure it's not already running
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

            // Done
            _countdownTimer.Stop();
            InCountdownMode = false;

            // After Out save + countdown, go to Out list
            SimpleIoc.Default.GetInstance<MainWindowViewModel>().CurrentViewModel =
                SimpleIoc.Default.GetInstance<WeighbridgeOutViewModel>();
        }

        public async void MouseUpCommand(string name)
        {
            if (name == "VehicleReg")
            {
                var dialogViewModel = SimpleIoc.Default.GetInstance<KeyboardViewModel>();
                dialogViewModel.HeaderText = "Enter Vehicle Reg";
                if (!UtilitiesShared.IsMyMachine) dialogViewModel.IsMaximized = true;
                bool? success = _dialogService.ShowDialog<KeyboardView>(this, dialogViewModel);
                if (success == true)
                {
                    CurrentWeighbridgeRecord.VehicleReg = dialogViewModel.KeyboardValue;
                }
            }
            else if (name == "DocketNumber")
            {
                var dialogViewModel = SimpleIoc.Default.GetInstance<KeypadViewModel>();
                dialogViewModel.HeaderText = "Enter Docket Number";
                bool? success = _dialogService.ShowDialog<KeypadView>(this, dialogViewModel);
                if (success == true)
                {
                    CurrentWeighbridgeRecord.DocketNumber = dialogViewModel.KeypadValue;
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
                        ExceptionHelper.LogError(null, "Relay not connected in RunPostWeighFlowAsync");
                    }
                    else
                    {
                        // Open if not already open
                        if (!_relay.BarrierOpen)
                        {
                            await _relay.SetBarrierAsync(true);
                            BarrierOpen = _relay.BarrierOpen;
                        }

                        // Wait until the bridge is clear: < 150 kg, stable 2s, timeout 60 min
                        await WaitUntilWeightBelowAsync(150, TimeSpan.FromSeconds(2), TimeSpan.FromMinutes(60));

                        // Close barrier
                        await _relay.SetBarrierAsync(false);
                        BarrierOpen = _relay.BarrierOpen;
                    }
                }
            }
            catch (Exception ex)
            {
                ExceptionHelper.LogError(ex, "RunPostWeighFlowAsync");
            }
            finally
            {
                SuccessOverlayVisible = false;

                // Navigate depending on whether this is a completed in+out
                if (CurrentWeighbridgeRecord != null &&
                    CurrentWeighbridgeRecord.DateWeightIn != null &&
                    CurrentWeighbridgeRecord.DateWeightOut != null)
                {
                    SimpleIoc.Default.GetInstance<MainWindowViewModel>().CurrentViewModel =
                        SimpleIoc.Default.GetInstance<WeighbridgeOutViewModel>();
                }
                else
                {
                    SimpleIoc.Default.GetInstance<MainWindowViewModel>().CurrentViewModel =
                        SimpleIoc.Default.GetInstance<WeighbridgeInViewModel>();
                }
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
            // Timeout: proceed; caller already closes barrier afterwards.
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

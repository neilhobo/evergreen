using GalaSoft.MvvmLight.CommandWpf;
using GalaSoft.MvvmLight.Ioc;
using Globeweigh.Model;
using Globeweigh.Shared;
using Globeweigh.Shared.Helpers;
using Globeweigh.Shared.Services;
using MvvmDialogs;
using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace Globeweigh.Touch
{
    public class WeighbridgeInViewModel : TouchViewModelBase, IViewModel
    {
        private readonly IDialogService _dialogService = SimpleIoc.Default.GetInstance<IDialogService>();
        private readonly IScaleService _scaleService;
        private readonly IRelayService _relay;

        private DispatcherTimer _scaleWatchdog;
        private EventHandler<string> _weightHandler;
        private EventHandler<bool> _barrierHandler;

        public WeighbridgeInViewModel(IScaleService scaleService, IRelayService relay)
        {
            _scaleService = scaleService;
            _relay = relay;

            EnsureSubscriptions();
            EnsureScaleWatchdog(); // create + wire, but don't Start here
        }

        // ---------- Ensure helpers (idempotent) ----------
        private void EnsureSubscriptions()
        {
            if (_weightHandler == null)
            {
                _weightHandler = OnWeightChanged;
                _scaleService.WeightChanged += _weightHandler;
            }

            if (_barrierHandler == null)
            {
                _barrierHandler = delegate (object s, bool open) { BarrierOpen = open; };
                _relay.BarrierOpenChanged += _barrierHandler;
            }
        }

        private void EnsureScaleWatchdog()
        {
            if (_scaleWatchdog != null) return;

            _scaleWatchdog = new DispatcherTimer();
            _scaleWatchdog.Interval = TimeSpan.FromSeconds(2);
            _scaleWatchdog.Tick += ScaleWatchdog_Tick;
        }

        // ---------- Properties ----------
        private DateTime _CurrentDate;
        public DateTime CurrentDate { get { return _CurrentDate; } set { Set(ref _CurrentDate, value); } }

        private string _CurrentVersion;
        public string CurrentVersion { get { return _CurrentVersion; } set { Set(ref _CurrentVersion, value); } }

        private bool _BarrierOpen;
        public bool BarrierOpen { get { return _BarrierOpen; } set { Set(ref _BarrierOpen, value); } }

        private bool _GateOpen;
        public bool GateOpen { get { return _GateOpen; } set { Set(ref _GateOpen, value); } }

        private string _LiveWeight;
        public string LiveWeight { get { return _LiveWeight; } set { Set(ref _LiveWeight, value); } }

        private void OnWeightChanged(object sender, string weight)
        {
            LiveWeight = weight; // binding updates immediately
        }

        // ---------- Commands ----------
        public RelayCommand OpenBarrierCommand { get { return new RelayCommand(OnOpenBarrier); } }
        public RelayCommand DeliveryCommand { get { return new RelayCommand(OnDelivery); } }
        public RelayCommand CollectionCommand { get { return new RelayCommand(OnCollection); } }
        public RelayCommand BackCommand { get { return new RelayCommand(OnBack); } }

        private void OnCollection()
        {
            var vm = SimpleIoc.Default.GetInstanceWithoutCaching<CollectionInViewModel>();
            // vm.CurrentWeighbridgeRecord = null;
            SimpleIoc.Default.GetInstance<MainWindowViewModel>().CurrentViewModel = vm;
        }

        private void OnDelivery()
        {
            var vm = SimpleIoc.Default.GetInstanceWithoutCaching<DeliveryInViewModel>();
            vm.CurrentWeighbridgeRecord = null;
            SimpleIoc.Default.GetInstance<MainWindowViewModel>().CurrentViewModel = vm;
        }

        private async void OnOpenBarrier()
        {
            var dialogKeypad = SimpleIoc.Default.GetInstance<KeypadViewModel>();
            dialogKeypad.IsPassword = true;
            dialogKeypad.EnforcePassword = true;
            dialogKeypad.ExpectedPassword = "2404";
            dialogKeypad.HeaderText = "Enter Pass Code";

            bool? success = _dialogService.ShowDialog<KeypadView>(this, dialogKeypad);
            if (success != true) return;

            try
            {
                await _relay.EnsureConnectedAsync();
                if (!_relay.IsConnected) return;

                if (!_relay.BarrierOpen)
                {
                    await _relay.SetBarrierAsync(true);
                    await WaitUntilWeightBelowAsync(150, TimeSpan.FromSeconds(2), TimeSpan.FromMinutes(60));
                    await _relay.SetBarrierAsync(false);
                }
                else
                {
                    await _relay.SetBarrierAsync(false);
                }
            }
            catch (Exception ex)
            {
                ExceptionHelper.LogError(ex, "WeighbridgeInViewModel.OnOpenBarrier");
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

        private async void ScaleWatchdog_Tick(object sender, EventArgs e)
        {
            var stale = (DateTime.UtcNow - _scaleService.LastUpdateUtc) > TimeSpan.FromSeconds(3);

            if (!_scaleService.IsConnected || stale)
            {
                try
                {
                    await _scaleService.EnsureConnectedAsync();
                }
                catch (Exception ex)
                {
                    ExceptionHelper.LogError(ex, "ScaleWatchdog_EnsureConnected");
                }
            }
        }

        private void OnBack()
        {
            SimpleIoc.Default.GetInstance<MainWindowViewModel>().CurrentViewModel =
                SimpleIoc.Default.GetInstance<WeighbridgeInViewModel>();
        }

        // ---------- Lifecycle ----------
        public async void Load(FrameworkElement _)
        {
            // Make sure everything exists even if ctor didn’t re-run
            EnsureSubscriptions();
            EnsureScaleWatchdog();

            CurrentDate = DateTime.Now;
            CurrentVersion = UtilitiesShared.GetCurrentMsiVersion();
            LiveWeight = _scaleService.CurrentWeight;

            _scaleWatchdog.Start();   // safe now

            await _relay.EnsureConnectedAsync();
            await _relay.RefreshBarrierStateAsync();
            BarrierOpen = _relay.BarrierOpen;
        }

        public void Unload(FrameworkElement _)
        {
            // Keep the objects; just stop the timer so it doesn’t tick in background
            if (_scaleWatchdog != null)
            {
                _scaleWatchdog.Stop();
                // do NOT null/unnwire here; we'll reuse on next Load
            }

            // If you prefer to unsubscribe on Unload, you can — but then Load must re-subscribe (which we do).
            if (_weightHandler != null)
            {
                _scaleService.WeightChanged -= _weightHandler;
                _weightHandler = null;
            }
            if (_barrierHandler != null)
            {
                _relay.BarrierOpenChanged -= _barrierHandler;
                _barrierHandler = null;
            }
        }
    }
}

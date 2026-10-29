using System;
using System.Collections.Generic;
using System.Linq;
using GalaSoft.MvvmLight.Ioc;
using Globeweigh.Model;
using Globeweigh.Shared;
using System.Windows;
using GalaSoft.MvvmLight.CommandWpf;
using System.Threading.Tasks;
using System.Windows.Threading;
using Globeweigh.Shared.Helpers;
using Globeweigh.Shared.Services;
using IDialogService = MvvmDialogs.IDialogService;

namespace Globeweigh.Touch
{
    public class WeighbridgeOutViewModel : TouchViewModelBase, IViewModel
    {
        #region private fields

        private readonly IWeighbridgeRepository _weighbridgeRepo = SimpleIoc.Default.GetInstance<IWeighbridgeRepository>();
        private readonly IDialogService _dialogService = SimpleIoc.Default.GetInstance<IDialogService>();

        private DispatcherTimer _refreshTimer;

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
            set { Set(ref _SelectedPending, value); }
        }


        private bool _PendingErrorFound;
        public bool PendingErrorFound
        {
            get { return _PendingErrorFound; }
            set { Set(ref _PendingErrorFound, value); }
        }

        private bool _IsPendingBusy;
        public bool IsPendingBusy
        {
            get { return _IsPendingBusy; }
            set { Set(ref _IsPendingBusy, value); }
        }

        #endregion

        #region Commands

        public RelayCommand HomeCommand => new RelayCommand(() => SimpleIoc.Default.GetInstance<MainWindowViewModel>().CurrentViewModel = SimpleIoc.Default.GetInstance<HomeViewModel>());
        public RelayCommand WeighOutCommand => new RelayCommand(OnWeighOut);
        public RelayCommand RetryPendingRefreshCommand => new RelayCommand(async () => await RefreshPending());

        #endregion
        bool RegEquals(string a, string b) => string.Equals(RemoveWhitespace(a), RemoveWhitespace(b), StringComparison.OrdinalIgnoreCase);

        string RemoveWhitespace(string s) => new string((s ?? string.Empty).Where(c => !char.IsWhiteSpace(c)).ToArray());

        private void OnWeighOut()
        {
            if(SelectedPending ==null)return;
            var dialogViewModel = SimpleIoc.Default.GetInstance<KeyboardViewModel>();
            if(!UtilitiesShared.IsMyMachine) dialogViewModel.IsMaximized = true;
            dialogViewModel.HeaderText = "Enter Vehicle Reg to confirm correct Load";
            bool? success = _dialogService.ShowDialog<KeyboardView>(this, dialogViewModel);
            if (success == true)
            {
                var vehicleReg = dialogViewModel.KeyboardValue;
                if (RegEquals(dialogViewModel.KeyboardValue, SelectedPending.VehicleReg))
                {
                    var vm = SimpleIoc.Default.GetInstance<WeighbridgeWeighViewModel>();
                    vm.CurrentWeighbridgeRecord = null;
                    vm.SelectedPending = SelectedPending;
                    vm.IsOut = true;
                    SimpleIoc.Default.GetInstance<MainWindowViewModel>().CurrentViewModel = vm;
                }
                else
                {
                    var dgMessage = SimpleIoc.Default.GetInstance<OkCancelViewModel>();
                    dgMessage.MessageText = "Vehicle Reg entered does not match";
                    dgMessage.HideCancel = true;
                    bool? success1 = _dialogService.ShowDialog<OkCancelView>(this, dgMessage);
                }
            }
  


        }

        private async Task RefreshPending()
        {
            try
            {
                IsPendingBusy = true;
                PendingErrorFound = false;

                // Remember the currently selected ID (if any)
                var selectedId = SelectedPending?.id;

                // Refresh list
                var newList = await _weighbridgeRepo.GetWeighbridgeRecordsWeighedIn();
                PendingList = new List<vwWeighbridgePending>(newList);

                // Try to restore the previous selection
                if (selectedId != null)
                {
                    SelectedPending = PendingList
                        .Find(p => p.id == selectedId);
                }

                IsPendingBusy = false;
            }
            catch (Exception e)
            {
                IsPendingBusy = false;
                PendingErrorFound = true;
                ExceptionHelper.LogError(e, "RefreshPending");
            }
        }


        public async void Load(FrameworkElement element)
        {
            PendingErrorFound = false;
            await RefreshPending();

            _refreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMinutes(1)
            };
            _refreshTimer.Tick += async (s, e) => await RefreshPending();
            _refreshTimer.Start();
        }

        public void Unload(FrameworkElement element)
        {
            IsPendingBusy = false;
            IsBusy = false;
            PendingErrorFound = false;
            PendingList = null;
            SelectedPending = null;

            if (_refreshTimer != null)
            {
                _refreshTimer.Stop();
                _refreshTimer = null;
            }
        }
    }
}

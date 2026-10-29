using GalaSoft.MvvmLight.Ioc;
using Globeweigh.Model;
using Globeweigh.Shared;
using System.Windows;
using GalaSoft.MvvmLight.CommandWpf;
using Globeweigh.Shared.Helpers;
using System;
using System.Threading.Tasks;
using System.Windows.Threading;
using MvvmDialogs;
using Globeweigh.Shared.Services;

namespace Globeweigh.Touch
{
    public class HomeViewModel : TouchViewModelBase, IViewModel
    {
        private readonly IDialogService _dialogService = SimpleIoc.Default.GetInstance<IDialogService>();
        private bool _ignoreUpdate;
        private DispatcherTimer _dispatcherTimer;

        #region Properties

        private DateTime _CurrentDate;
        public DateTime CurrentDate
        {
            get { return _CurrentDate; }
            set { Set(ref _CurrentDate, value); }
        }

        private string _CurrentVersion;
        public string CurrentVersion
        {
            get { return _CurrentVersion; }
            set { Set(ref _CurrentVersion, value); }
        }

        #endregion

        #region Commands

        public RelayCommand ExitCommand => new RelayCommand(OnExit);
        public RelayCommand ProductionSummaryCommand => new RelayCommand(() => SimpleIoc.Default.GetInstance<MainWindowViewModel>().CurrentViewModel = SimpleIoc.Default.GetInstanceWithoutCaching<ProductionSummaryViewModel>());
        public RelayCommand ProductionCommand => new RelayCommand(() => SimpleIoc.Default.GetInstance<MainWindowViewModel>().CurrentViewModel = SimpleIoc.Default.GetInstanceWithoutCaching<ProductionPalletListViewModel>());
        public RelayCommand SampleWeightCommand => new RelayCommand(() => SimpleIoc.Default.GetInstance<MainWindowViewModel>().CurrentViewModel = SimpleIoc.Default.GetInstanceWithoutCaching<SampleWeightViewModel>());
        //public RelayCommand DispatchCommand => new RelayCommand(() => OnDispatchCommand());
        //public RelayCommand AdminSettingsCommand => new RelayCommand(OnAdminSettings);


        #endregion

        private async void OnDispatchCommand()
        {
            // SimpleIoc.Default.GetInstance<MainWindowViewModel>().CurrentViewModel = SimpleIoc.Default.GetInstanceWithoutCaching<DispatchOrderListViewModel>();
        }

        private async void OnAdminSettings()
        {
            var dialogKeypad = SimpleIoc.Default.GetInstance<KeypadViewModel>();
            dialogKeypad.IsPassword = true;
            dialogKeypad.HeaderText = "Enter Pass Code";
            bool? success = _dialogService.ShowDialog<KeypadView>(this, dialogKeypad);
            if (success == true)
            {
                if (Convert.ToInt32(dialogKeypad.KeypadValue) == 1225)
                {
                    
                }
            }
        }

        private void OnExit()
        {
            Application.Current.Shutdown();
        }

        public async void Load(FrameworkElement element)
        {
            CurrentDate = DateTime.Now;
            if (_ignoreUpdate) return;
            CurrentVersion = UtilitiesShared.GetCurrentMsiVersion();

        }

        private async void dispatcherTimer_Tick(object sender, object e)
        {
            await OnTick();
        }

        private async Task OnTick()
        {
            try
            {
                CurrentDate = DateTime.Now;
            }
            catch (Exception ex)
            {
            }
        }

        public void Unload(FrameworkElement element)
        {
            if (_dispatcherTimer != null)
            {
                _dispatcherTimer.Stop();
                _dispatcherTimer.Tick -= dispatcherTimer_Tick;
                _dispatcherTimer = null;
            }
        }
    }

}

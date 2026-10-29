using System;
using System.Collections.Generic;
using System.Diagnostics;
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
    public class LoginViewModel : TouchViewModelBase, IViewModel
    {
        #region private fields

        private readonly IDialogService _dialogService = SimpleIoc.Default.GetInstance<IDialogService>();
        private readonly IGlobeweighUserRepository _userRepo = SimpleIoc.Default.GetInstance<IGlobeweighUserRepository>();
        private DispatcherTimer _dispatcherTimer;

        #endregion

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

        private List<vwGlobeweighUserFactory> _UserList;
        public List<vwGlobeweighUserFactory> UserList
        {
            get { return _UserList; }
            set { Set(ref _UserList, value); }
        }

        private vwGlobeweighUserFactory _SelectedUser;
        public vwGlobeweighUserFactory SelectedUser
        {
            get { return _SelectedUser; }
            set
            {
                Set(ref _SelectedUser, value);
                KeypadValue = string.Empty;
            }
        }

        private string _KeypadValue;
        public string KeypadValue
        {
            get { return _KeypadValue; }
            private set
            {
                Set(ref _KeypadValue, value);
                if(KeypadValue == null)return;
                int charCount = KeypadValue.Length;
                KeypadDisplayValue = "";
                for (var i = 0; i < charCount; i++)
                {
                    KeypadDisplayValue += "*";
                }
            }
        }

        private string _KeypadDisplayValue;
        public string KeypadDisplayValue
        {
            get { return _KeypadDisplayValue; }
            set { Set(ref _KeypadDisplayValue, value); }
        }

        private string _ErrorText;
        public string ErrorText
        {
            get { return _ErrorText; }
            set { Set(ref _ErrorText, value); }
        }


        #endregion

        #region Commands

        public RelayCommand LoginCommand => new RelayCommand(OnLogin, CanLogin);
        public RelayCommand HomeCommand => new RelayCommand(() => SimpleIoc.Default.GetInstance<MainWindowViewModel>().CurrentViewModel = SimpleIoc.Default.GetInstance<HomeViewModel>());
        public RelayCommand ExitCommand => new RelayCommand(OnExit);
        public RelayCommand<string> KeypadCommand => new RelayCommand<string>(OnKeypadCommand);

        #endregion

        private void OnExit()
        {
            Application.Current.Shutdown();
        }

        private void OnKeypadCommand(string param)
        {
            if (param == "Clear")
            {
                KeypadValue = string.Empty;
                return;
            }
            if (param == "Del")
            {
                if (KeypadValue == null) return;
                if (KeypadValue.Length == 0) return;
                KeypadValue = KeypadValue.Substring(0, KeypadValue.Length - 1);
                return;
            }
            if (param == "-")
            {
                if (!string.IsNullOrEmpty(KeypadValue)) return;
            }
            else if (param == ".")
            {
                if (string.IsNullOrEmpty(KeypadValue)) return;
                if (KeypadValue.Contains(".")) return;
            }
            KeypadValue += param;


        }

        private bool CanLogin()
        {
            if (SelectedUser == null) return false;
            return true;
        }

        private async void OnLogin()
        {
            ErrorText = null;
            var user = await _userRepo.LoginTouch(SelectedUser.id, KeypadValue);
            if (user == null)
            {
                ErrorText = "Invalid passcode";
            }
            else if (user.Disabled)
            {
                MessageBox.Show("Account is disabled");
                return;
            }
            else
            {
                GlobalVariables.CurrentGlobeweighUser = user;
                SimpleIoc.Default.GetInstance<MainWindowViewModel>().CurrentViewModel = SimpleIoc.Default.GetInstance<HomeViewModel>();
            }

        }

        private async Task RefreshUserList()
        {
            UserList = new List<vwGlobeweighUserFactory>(await _userRepo.GetGlobeweighFacoryUsers());
        }

        public async void Load(FrameworkElement element)
        {
            CurrentDate = DateTime.Now;
            await RefreshUserList();

            CurrentVersion = UtilitiesShared.GetCurrentMsiVersion();

            if (CurrentVersion != null)
            {
                if (!Debugger.IsAttached)
                {
                    var newReleaseFullFileName = UtilitiesShared.CheckforUpdate(CurrentVersion, "TouchSetup", out string newReleaseVersion);
                    if (newReleaseFullFileName != null)
                    {
                        var dialogViewModel = SimpleIoc.Default.GetInstance<DownloadNewUpdateViewModel>();
                        dialogViewModel.NewReleaseFullFileName = newReleaseFullFileName;
                        bool? success = _dialogService.ShowDialog<DownloadNewUpdateView>(this, dialogViewModel);
                        if (success == true)
                        {
                        }
                    }
                }
            }


            if (_dispatcherTimer == null) _dispatcherTimer = new DispatcherTimer();
            _dispatcherTimer.Tick += dispatcherTimer_Tick;
            _dispatcherTimer.Interval = new TimeSpan(0, 0, 0, 60);
            _dispatcherTimer.Start();

        }


        private async void dispatcherTimer_Tick(object sender, object e)
        {
            await Task.Run(() => OnTick());
        }

        private async void OnTick()
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

            KeypadValue = null;
            UserList = null;
            SelectedUser = null;
            IsBusy = false;
        }
    }
}

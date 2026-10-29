using System.Diagnostics;
using Globeweigh.Shared;
using System.Windows;
using Globeweigh.Model;
using GalaSoft.MvvmLight.Ioc;
using Globeweigh.Shared.Helpers;
using Globeweigh.Shared.Services;
using MvvmDialogs;

namespace Globeweigh.Touch
{
    public class MainWindowViewModel : BindableBase, IViewModel
    {
        #region private fields

        private readonly IDialogService _dialogService = SimpleIoc.Default.GetInstance<IDialogService>();

        #endregion

        #region Properties

        private object _CurrentViewModel;
        public object CurrentViewModel
        {
            get { return _CurrentViewModel; }
            set { Set(ref _CurrentViewModel, value); }
        }

        private bool _InTestMode;
        public bool InTestMode
        {
            get { return _InTestMode; }
            set { Set(ref _InTestMode, value); }
        }

        private string _CurrentVersion;
        public string CurrentVersion
        {
            get { return _CurrentVersion; }
            set { Set(ref _CurrentVersion, value); }
        }

        #endregion

        public async void Load(FrameworkElement element)
        {

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
                            return;
                        }
                    }
                }
            }


            if (UtilitiesShared.IsMyMachine)
            {
                // CurrentViewModel = SimpleIoc.Default.GetInstance<HomeViewModel>();
                // CurrentViewModel = SimpleIoc.Default.GetInstance<WeighbridgeOutViewModel>();
                CurrentViewModel = SimpleIoc.Default.GetInstance<WeighbridgeInViewModel>();
            }
            else
            {
                if (GlobalVariables.CurrentDevice.IsWeighIn)
                {
                    CurrentViewModel = SimpleIoc.Default.GetInstance<WeighbridgeInViewModel>();
                }
                else if (GlobalVariables.CurrentDevice.IsWeighOut)
                {
                    CurrentViewModel = SimpleIoc.Default.GetInstance<WeighbridgeOutViewModel>();
                }
                else
                {
                    CurrentViewModel = SimpleIoc.Default.GetInstance<HomeViewModel>();
                }
            }

        }

        public void Unload(FrameworkElement element)
        {

        }
    }
}

using Globeweigh.Shared;
using Globeweigh.Model;
using Globeweigh.Shared.Services;
using MvvmDialogs;
using System;
using System.Windows;
using GalaSoft.MvvmLight.CommandWpf;
using GalaSoft.MvvmLight.Ioc;
using Microsoft.Win32;

namespace Globeweigh.Touch
{
    public class EditSettingsViewModel : DialogViewModelBase, IModalDialogViewModel, IViewModel
    {
        #region private fields

        private readonly ISettingsRepository _settingsRepo = SimpleIoc.Default.GetInstance<ISettingsRepository>();
        private readonly IDialogService _dialogService = SimpleIoc.Default.GetInstance<IDialogService>();


        #endregion

        #region Properties

        private bool _DispatchOnHomeScreen;
        public bool DispatchOnHomeScreen
        {
            get { return _DispatchOnHomeScreen; }
            set { Set(ref _DispatchOnHomeScreen, value); }
        }

        private bool _IntakeOnHomeScreen;
        public bool IntakeOnHomeScreen
        {
            get { return _IntakeOnHomeScreen; }
            set { Set(ref _IntakeOnHomeScreen, value); }
        }

        private int _PalletLabelPrintCount;
        public int PalletLabelPrintCount
        {
            get { return _PalletLabelPrintCount; }
            set { Set(ref _PalletLabelPrintCount, value); }
        }

  

        #endregion

        #region Commands

        public RelayCommand SaveCommand => new RelayCommand(OnSave);

        #endregion


        private async void OnSave()
        {
            Microsoft.Win32.RegistryKey regKey = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Globeweigh", true);
            if (regKey == null)
            {
                regKey =Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"SOFTWARE\Globeweigh", RegistryKeyPermissionCheck.ReadWriteSubTree);
            }

            if (regKey != null)
            {
                regKey.SetValue("DispatchOnHomeScreen", DispatchOnHomeScreen);
                regKey.SetValue("IntakeOnHomeScreen", IntakeOnHomeScreen);
                regKey.Close();
            }


            if (GlobalVariables.GlobalDbSettings.PalletLabelPrintCount != PalletLabelPrintCount)
            {
                GlobalVariables.GlobalDbSettings.PalletLabelPrintCount = PalletLabelPrintCount;
                await _settingsRepo.UpdateSettings();
            }

            DialogResult = true;
        }

        public async void MouseUpCommand(string name)
        {
            if (name == "PrintCount")
            {
                var dialogVm = SimpleIoc.Default.GetInstance<KeypadViewModel>();
                dialogVm.HeaderText = "Enter Label Print Count:";
                bool? success = _dialogService.ShowDialog<KeypadView>(this, dialogVm);
                if (success == true)
                {
                    PalletLabelPrintCount= Convert.ToInt32(dialogVm.KeypadValue);
                }
            }
        }


        public async void Load(FrameworkElement element)
        {
            Microsoft.Win32.RegistryKey regKey = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Globeweigh");
            if (regKey == null) return;
            var dispatchOnHomeScreen = regKey.GetValue("DispatchOnHomeScreen");
            if (dispatchOnHomeScreen != null)
            {
                DispatchOnHomeScreen = Convert.ToBoolean(regKey.GetValue("DispatchOnHomeScreen").ToString());
            }
            var intakeOnHomeScreen = regKey.GetValue("IntakeOnHomeScreen");
            if (intakeOnHomeScreen != null)
            {
                IntakeOnHomeScreen = Convert.ToBoolean(regKey.GetValue("IntakeOnHomeScreen").ToString());
            }

            PalletLabelPrintCount = GlobalVariables.GlobalDbSettings.PalletLabelPrintCount;
        }

        public void Unload(FrameworkElement element)
        {
            DialogResult = null;
        }
    }
}

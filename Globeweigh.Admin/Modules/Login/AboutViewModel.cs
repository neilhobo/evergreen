using Globeweigh.Shared;
using Globeweigh.Shared.Services;
using MvvmDialogs;
using System.Windows;
using GalaSoft.MvvmLight.CommandWpf;
using GalaSoft.MvvmLight.Ioc;
using Globeweigh.Admin.Properties;
using Globeweigh.Model;
using Globeweigh.Shared.Helpers;


namespace Globeweigh.Admin
{
    public class AboutViewModel : DialogViewModelBase, IModalDialogViewModel, IViewModel
    {
        #region private fields

        #endregion

        #region Properties

        private string _CurrentVersion;
        public string CurrentVersion
        {
            get { return _CurrentVersion; }
            set { Set(ref _CurrentVersion, value); }
        }

        #endregion

        #region Commands

        // public RelayCommand LoginCommand => new RelayCommand(OnLogin, CanLogin);

        #endregion

        public async void Load(FrameworkElement element)
        {
            CurrentVersion = UtilitiesShared.GetCurrentMsiVersion();

            if (UtilitiesShared.IsMyMachine)
            {
                CurrentVersion = "1.0.62";
            }
        }



        public void Unload(FrameworkElement element)
        {
            DialogResult = null;
        }
    }
}

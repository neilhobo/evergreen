using Globeweigh.Shared;
using Globeweigh.Shared.Services;
using MvvmDialogs;
using System.Windows;
using GalaSoft.MvvmLight.CommandWpf;
using GalaSoft.MvvmLight.Ioc;
using Globeweigh.Model;
using Globeweigh.Shared.Helpers;


namespace Globeweigh.Admin
{
    public class ServerConfigViewModel : DialogViewModelBase, IModalDialogViewModel, IViewModel
    {
        #region private fields

        private readonly IGlobeweighUserRepository _globeweighUserRepository = SimpleIoc.Default.GetInstance<IGlobeweighUserRepository>();

        #endregion

        #region Properties


        private string _ServerInstance;
        public string ServerInstance
        {
            get { return _ServerInstance; }
            set { Set(ref _ServerInstance, value); }
        }

        private string _DatabaseName;
        public string DatabaseName
        {
            get { return _DatabaseName; }
            set { Set(ref _DatabaseName, value); }
        }

        private bool? _ConnectionSuccessfull;
        public bool? ConnectionSuccessfull
        {
            get { return _ConnectionSuccessfull; }
            set { Set(ref _ConnectionSuccessfull, value); }
        }



        #endregion

        #region Commands

        public RelayCommand SaveCommand => new RelayCommand(OnSave, CanSave);
        public RelayCommand TestCommand => new RelayCommand(OnTest);

        #endregion

        public async void Load(FrameworkElement element)
        {
        }

        private async void OnSave()
        {
            ConnectionSuccessfull = null;
            DialogResult = true;
        }

        private async void OnTest()
        {
            ConnectionSuccessfull = null;
            DialogResult = true;
        }

        private bool CanSave()
        {
            return true;
        }

        public void Unload(FrameworkElement element)
        {
            DialogResult = null;
        }
    }
}

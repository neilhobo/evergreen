using System.Windows;
using System.Windows.Controls;
using GalaSoft.MvvmLight.Ioc;
using Globeweigh.Shared;

namespace Globeweigh.Admin
{
    public class CostingMasterViewModel : AdminViewModelBase, IViewModel
    {
        #region Properties

        private object _CurrentViewModel;
        public object CurrentViewModel
        {
            get { return _CurrentViewModel; }
            set { Set(ref _CurrentViewModel, value); }
        }

        private ListBoxItem _SelectedNavItem;
        public ListBoxItem SelectedNavItem
        {
            get { return _SelectedNavItem; }
            set
            {
                Set(ref _SelectedNavItem, value);
                if (SelectedNavItem != null) SetCurrentView();
            }
        }

        #endregion

        private void SetCurrentView()
        {
            switch (SelectedNavItem.Name)
            {
                case "weighbridgeRecords":
                    CurrentViewModel = SimpleIoc.Default.GetInstance<WeighbridgeViewModel>();
                    break;
                case "dataSetup":
                    CurrentViewModel = SimpleIoc.Default.GetInstance<WeighbridgeSetupViewModel>();
                    break;
                default:
                    CurrentViewModel = null;
                    break;
            }
        }

        public async void Load(FrameworkElement element)
        {
        }

        public void Unload(FrameworkElement element)
        {
        }
    }
}

using GalaSoft.MvvmLight.Ioc;
using Globeweigh.Shared;
using System.Windows;
using GalaSoft.MvvmLight.CommandWpf;
using System.Windows.Controls;

namespace Globeweigh.Touch
{
    public class StockMasterViewModel : TouchViewModelBase, IViewModel
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

        #region

        public RelayCommand HomeCommand => new RelayCommand(OnHome);

        #endregion



        private void OnHome()
        {
            SimpleIoc.Default.GetInstance<MainWindowViewModel>().CurrentViewModel = SimpleIoc.Default.GetInstance<HomeViewModel>();
        }

        private void SetCurrentView()
        {
            switch (SelectedNavItem.Name)
            {
                case "Summary":
                    CurrentViewModel = SimpleIoc.Default.GetInstance<StockSummaryViewModel>();
                    break;
                case "LocationView":
                    CurrentViewModel = SimpleIoc.Default.GetInstance<StockLocationViewModel>();
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

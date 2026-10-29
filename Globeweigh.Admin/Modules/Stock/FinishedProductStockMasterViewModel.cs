using Globeweigh.Shared;
using System.Windows;
using System.Windows.Controls;
using GalaSoft.MvvmLight.Ioc;

namespace Globeweigh.Admin
{
    public class FinishedProductStockMasterViewModel : AdminViewModelBase, IViewModel
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
                case "CurrentStockList":
                    CurrentViewModel = SimpleIoc.Default.GetInstance<CurrentStockListViewModel>();
                    break;
                case "LocationView":
                    CurrentViewModel = SimpleIoc.Default.GetInstance<CurrentStockLocationViewModel>();
                    break;
                case "SummaryView":
                    CurrentViewModel = SimpleIoc.Default.GetInstance<CurrentStockSummaryViewModel>();
                    break;
                case "StockTakes":
                    CurrentViewModel = SimpleIoc.Default.GetInstance<StockTakeViewModel>();
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

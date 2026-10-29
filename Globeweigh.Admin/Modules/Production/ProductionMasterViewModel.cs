using System.Collections.Generic;
using System.Linq;
using Globeweigh.Shared;
using System.Windows;
using System.Windows.Controls;
using GalaSoft.MvvmLight.Ioc;
using Globeweigh.Model;
using Globeweigh.Shared.Helpers;
using Globeweigh.Shared.Services;

namespace Globeweigh.Admin
{
    public class ProductionMasterViewModel : AdminViewModelBase, IViewModel
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
                case "production":
                    CurrentViewModel = SimpleIoc.Default.GetInstance<ProductionPalletListViewModel>();
                    break;
                case "productionSummary":
                    CurrentViewModel = SimpleIoc.Default.GetInstance<ProductionSummaryViewModel>();
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

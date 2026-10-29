using System;
using System.Collections.Generic;
using Globeweigh.Shared.Services;
using Globeweigh.Shared;
using System.Windows;
using System.Windows.Controls;
using Globeweigh.Shared.Helpers;
using Globeweigh.Model;
using GalaSoft.MvvmLight.Ioc;

namespace Globeweigh.Admin
{
    public class ReportsMasterViewModel : AdminViewModelBase, IViewModel
    {
        #region private fields

        private readonly IReferenceDataRepository _refRepo = SimpleIoc.Default.GetInstance<IReferenceDataRepository>();

        #endregion

        #region Properties

        private ListBoxItem _SelectedEntity;
        public ListBoxItem SelectedEntity
        {
            get { return _SelectedEntity; }
            set
            {
                Set(ref _SelectedEntity, value);
                SetCurrentView();
            }
        }

        private object _CurrentViewModel;
        public object CurrentViewModel
        {
            get { return _CurrentViewModel; }
            set { Set(ref _CurrentViewModel, value); }
        }



        #endregion

        #region Commands

        #endregion

        private async void SetCurrentView()
        {
            if (SelectedEntity == null)
            {
                CurrentViewModel = null;
                return;
            }
            switch (SelectedEntity.Name)
            {
                case "StockDispatch":
                    CurrentViewModel = SimpleIoc.Default.GetInstanceWithoutCaching<ReportDispatchInStockViewModel>();
                    break;
                case "StockSummary":
                    CurrentViewModel = SimpleIoc.Default.GetInstanceWithoutCaching<ReportStockSummaryViewModel>();
                    break;
                case "Dispatch":
                    CurrentViewModel = SimpleIoc.Default.GetInstanceWithoutCaching<ReportDispatchRecordsViewModel>();
                    break;
//                case "PurchasesByCustomer":
//                    CurrentViewModel = SimpleIoc.Default.GetInstanceWithoutCaching<PurchaseReportsCustomerViewModel>();
//                    break;
                default:
                    CurrentViewModel = null;
                    break;
            }

        }

        public async void Load(FrameworkElement element)
        {
            if (UtilitiesShared.InDesignMode) return;
            
        }

        public void Unload(FrameworkElement element)
        {
            SelectedEntity = null;
        }

    }
}

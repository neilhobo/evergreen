using System;
using System.Collections.Generic;
using System.Linq;
using GalaSoft.MvvmLight.Ioc;
using Globeweigh.Model;
using Globeweigh.Shared;
using System.Windows;
using GalaSoft.MvvmLight.CommandWpf;
using Globeweigh.Shared.Services;
using System.Threading.Tasks;
using System.Windows.Threading;
using Globeweigh.Touch.Helpers;
using IDialogService = MvvmDialogs.IDialogService;

namespace Globeweigh.Touch
{
    public class StockLocationViewModel : TouchViewModelBase, IViewModel
    {
        #region private fields

        private readonly IDialogService _dialogService = SimpleIoc.Default.GetInstance<IDialogService>();
        private readonly IStockRepository _stockRepo = SimpleIoc.Default.GetInstance<IStockRepository>();

        private DispatcherTimer _dispatcherTimer;

        #endregion

        #region Properties

        private List<vwCurrentStockSummaryByLocation> _SummaryList;
        public List<vwCurrentStockSummaryByLocation> SummaryList
        {
            get { return _SummaryList; }
            set { Set(ref _SummaryList, value); }
        }

        private vwCurrentStockSummaryByLocation _SelectedSummary;
        public vwCurrentStockSummaryByLocation SelectedSummary
        {
            get { return _SelectedSummary; }
            set
            {
                Set(ref _SelectedSummary, value);

            }
        }

        private List<vwCurrentStockLocation> _LocationList;
        public List<vwCurrentStockLocation> LocationList
        {
            get { return _LocationList; }
            set
            {
                Set(ref _LocationList, value);
            }
        }

        private vwCurrentStockLocation _SelectedLocation;
        public vwCurrentStockLocation SelectedLocation
        {
            get { return _SelectedLocation; }
            set
            {
                Set(ref _SelectedLocation, value);
                if (SelectedLocation == null) return;
                RefreshSummaryList();
            }
        }

        #endregion

        #region Events

        #endregion

        #region Commands

        #endregion

        public async Task RefreshSummaryList()
        {
            try
            {
                IsBusy = true;
                SelectedSummary = null;
                SummaryList = new List<vwCurrentStockSummaryByLocation>(await _stockRepo.GetCurrentStockByLocation(SelectedLocation.LocationId));
                IsBusy = false;
            }
            catch (Exception ex)
            {
                IsBusy = false;
            }
            finally
            {
                IsBusy = false;
            }
        }

        public async Task RefreshLocationList()
        {
            try
            {
                IsBusy = true;
                SelectedSummary = null;
                LocationList = new List<vwCurrentStockLocation>(await _stockRepo.GetCurrentStockLocationList());
                SummaryList = null;
                IsBusy = false;
            }
            catch (Exception ex)
            {
                IsBusy = false;
            }
            finally
            {
                IsBusy = false;
            }
        }

        //public async Task RefreshLocationList()
        //{
        //    try
        //    {
        //        IsBusy = true;
        //        LocationList = new List<vwIntakeLocation>( await _LocationRepo.GetIntakeLocationsByProductId(FilterDate.Date, SelectedSummary.ProductId));
        //        LocationCount = LocationList.Count;
        //        SelectedLocation = null;
        //        IsBusy = false;
        //    }
        //    catch (Exception ex)
        //    {
        //        IsBusy = false;
        //    }
        //    finally
        //    {
        //        IsBusy = false;
        //    }

        //}

        public async void Load(FrameworkElement element)
        {
            IsBusy = false;
            await RefreshLocationList();
        }

        public void Unload(FrameworkElement element)
        {
            IsBusy = true;
            LocationList = null;
            SelectedLocation = null;
            IsBusy = false;
        }
    }
}

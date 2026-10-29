using GalaSoft.MvvmLight.Ioc;
using Globeweigh.Model;
using Globeweigh.Shared;
using Globeweigh.Shared.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using GalaSoft.MvvmLight.CommandWpf;

namespace Globeweigh.Touch
{
    public class ProductionPalletListViewModel : TouchViewModelBase, IViewModel
    {
        #region private fields

        private readonly IPalletRepository _palletRepo = SimpleIoc.Default.GetInstance<IPalletRepository>();
        private DispatcherTimer _refreshTimer;
        private bool _isRefreshing;

        private DateTime _lastRefreshTime;
        private DispatcherTimer _clockTimer;



        #endregion

        #region Properties

        private string _lastRefreshText;
        public string LastRefreshText
        {
            get { return _lastRefreshText; }
            set { Set(ref _lastRefreshText, value); }
        }

        private List<vwCodicoPallet> _PalletList;
        public List<vwCodicoPallet> PalletList
        {
            get { return _PalletList; }
            set { Set(ref _PalletList, value); }
        }

        private vwCodicoPallet _SelectedPallet;
        public vwCodicoPallet SelectedPallet
        {
            get { return _SelectedPallet; }
            set { Set(ref _SelectedPallet, value); }
        }

        private DateTime _DateFrom;
        public DateTime DateFrom
        {
            get { return _DateFrom; }
            set { Set(ref _DateFrom, value); }
        }

        private DateTime _DateTo;
        public DateTime DateTo
        {
            get { return _DateTo; }
            set { Set(ref _DateTo, value); }
        }

        private ObservableCollection<vwCodicoPallet> _SelectedPallets;
        public ObservableCollection<vwCodicoPallet> SelectedPallets
        {
            get { return _SelectedPallets; }
            set { Set(ref _SelectedPallets, value); }
        }

        #endregion
        public RelayCommand HomeCommand => new RelayCommand(OnHome);

        private void OnHome()
        {
            IsBusy = true;
            SimpleIoc.Default.GetInstance<MainWindowViewModel>().CurrentViewModel = SimpleIoc.Default.GetInstance<HomeViewModel>();
        }

        private async Task RefreshPallets()
        {
            if (_isRefreshing)
                return;

            try
            {
                _isRefreshing = true;
                IsBusy = true;
                PalletList = new List<vwCodicoPallet>(await _palletRepo.GetProductionPalletsAsync(DateFrom, DateTo.AddDays(1)));
                SelectedPallet = null;
                SelectedPallets = new ObservableCollection<vwCodicoPallet>();
                _lastRefreshTime = DateTime.Now;
            }
            catch
            {
                // optionally log the exception
            }
            finally
            {
                IsBusy = false;
                _isRefreshing = false;
            }
        }

        public async void Load(FrameworkElement element)
        {
            DateFrom = DateTime.Today.AddDays(-1);
            DateTo = DateTime.Today.AddDays(1);

            await RefreshPallets();

            _refreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(10)
            };

            _refreshTimer.Tick += async (s, e) => await RefreshPallets();
            _refreshTimer.Start();

            _clockTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            // _clockTimer.Tick += (s, e) =>
            // {
            //     if (_lastRefreshTime == default) return;
            //     var seconds = (int)(DateTime.Now - _lastRefreshTime).TotalSeconds;
            //     LastRefreshText = seconds == 1 ? "Last refresh: 1 second ago" : $"Last refresh: {seconds} seconds ago";
            // };
            // _clockTimer.Start();
        }

        public void Unload(FrameworkElement element)
        {
            if (_refreshTimer != null)
            {
                _refreshTimer.Stop();
                _refreshTimer = null;
            }

            // if (_clockTimer != null)
            // {
            //     _clockTimer.Stop();
            //     _clockTimer = null;
            // }

            PalletList = null;
            SelectedPallet = null;
            SelectedPallets = null;
        }
    }
}
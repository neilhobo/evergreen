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
using DevExpress.XtraReports.UI;
using DevExpress.XtraRichEdit.Commands;
using Globeweigh.Shared.Helpers;
using Globeweigh.Touch.Helpers;
using IDialogService = MvvmDialogs.IDialogService;
using vwPalletView = Globeweigh.Model.vwPalletView;
using vwProductionSummaryPalletView = Globeweigh.Model.vwProductionSummaryPalletView;

namespace Globeweigh.Touch
{
    public class ProductionSummaryViewModel : TouchViewModelBase, IViewModel
    {
        #region private fields

        private readonly IDialogService _dialogService = SimpleIoc.Default.GetInstance<IDialogService>();
        private readonly IPalletRepository _palletRepo = SimpleIoc.Default.GetInstance<IPalletRepository>();

        private DispatcherTimer _dispatcherTimer;

        #endregion

        #region Properties

        private DateTime _FilterDate;
        public DateTime FilterDate
        {
            get { return _FilterDate; }
            set { Set(ref _FilterDate, value); }
        }

        private List<GetProductionSummary_Result> _ProductList;
        public List<GetProductionSummary_Result> ProductList
        {
            get { return _ProductList; }
            set { Set(ref _ProductList, value); }
        }

        private GetProductionSummary_Result _SelectedProduct;
        public GetProductionSummary_Result SelectedProduct
        {
            get { return _SelectedProduct; }
            set
            {
                Set(ref _SelectedProduct, value);
                SelectedProductChanged();
            }
        }

        private List<vwPalletView> _PalletList;
        public List<vwPalletView> PalletList
        {
            get { return _PalletList; }
            set
            {
                Set(ref _PalletList, value);
            }
        }

        private vwPalletView _SelectedPallet;
        public vwPalletView SelectedPallet
        {
            get { return _SelectedPallet; }
            set { Set(ref _SelectedPallet, value); }
        }

        private int _PalletCount;
        public int PalletCount
        {
            get { return _PalletCount; }
            private set { Set(ref _PalletCount, value); }
        }


        #endregion

        #region Events



        #endregion

        #region Commands

        public RelayCommand HomeCommand => new RelayCommand(OnHome);
        public RelayCommand FilterDateBackCommand => new RelayCommand(OnFilterDateBack);
        public RelayCommand FilterDateForwardCommand => new RelayCommand(OnFilterDateForward);

        #endregion

        private async void SelectedProductChanged()
        {
            if (SelectedProduct == null) return;
            // await RefreshHistory();

        }

        private void OnHome()
        {
            IsBusy = true;
            SimpleIoc.Default.GetInstance<MainWindowViewModel>().CurrentViewModel = SimpleIoc.Default.GetInstance<HomeViewModel>();
        }


        private async void OnFilterDateBack()
        {
            FilterDate = FilterDate.AddDays(-1);
            await RefreshProducts();
        }

        private async void OnFilterDateForward()
        {
            FilterDate = FilterDate.AddDays(1);
            await RefreshProducts();
        }


        private async Task RefreshProducts()
        {
            try
            {
                IsBusy = true;
                ProductList = new List<GetProductionSummary_Result>(await _palletRepo.GetProductionSummary(FilterDate, FilterDate));
            }
            catch
            {
                IsBusy = false;
            }
            finally
            {
                IsBusy = false;
            }
        }

        public async Task RefreshPalletList()
        {
            try
            {
                IsBusy = true;
                PalletList = new List<vwPalletView>( await _palletRepo.GetProductionPalletsByProductId(FilterDate.Date, (int)SelectedProduct.id));
                PalletCount = PalletList.Count;
                SelectedPallet = null;
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

        private async void Timer_Tick(object sender, object e)
        {
            await Task.Run(() => OnTick());
        }

        private async void OnTick()
        {
            try
            {
                if (IsBusy) return;
                await RefreshProducts();
            }
            catch (Exception ex)
            {

            }
        }

        public async void Load(FrameworkElement element)
        {
            IsBusy = false;
            FilterDate = DateTime.Today;
            //if (AsnImportSelected != false) AsnImportSelected = false;
            await RefreshProducts();

            //if (_dispatcherTimer == null) _dispatcherTimer = new DispatcherTimer();
            //_dispatcherTimer.Tick += Timer_Tick;
            //_dispatcherTimer.Interval = new TimeSpan(0, 0, 2, 0);
            //_dispatcherTimer.Start();
        }

        public void Unload(FrameworkElement element)
        {
            IsBusy = true;
            if (_dispatcherTimer != null)
            {
                _dispatcherTimer.Tick -= Timer_Tick;
                _dispatcherTimer.Stop();
                _dispatcherTimer = null;
            }

            PalletList = null;
            //AsnImportSelected = null;
            SelectedPallet = null;
            IsBusy = false;
        }
    }
}

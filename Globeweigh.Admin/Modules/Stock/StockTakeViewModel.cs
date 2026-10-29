using System.Collections.Generic;
using Globeweigh.Shared;
using Globeweigh.Shared.Services;
using System.Windows;
using GalaSoft.MvvmLight.CommandWpf;
using Globeweigh.Model;
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Controls;
using DevExpress.Xpf.Grid;
using GalaSoft.MvvmLight.Ioc;
using Globeweigh.Admin.Helpers;
using Globeweigh.Model.Custom;
using Globeweigh.Shared.Helpers;
using MvvmDialogs;

namespace Globeweigh.Admin
{
    public class StockTakeViewModel : AdminViewModelBase, IViewModel
    {
        #region private fields

        private readonly IStockRepository _stockRepo = SimpleIoc.Default.GetInstance<IStockRepository>();
        private readonly IDialogService _dialogService = SimpleIoc.Default.GetInstance<IDialogService>();
        // private List<vwStockTakeSummary> _allSummaryList;

        #endregion

        #region Properties

        private List<StockTake> _StockTakeList;
        public List<StockTake> StockTakeList
        {
            get { return _StockTakeList; }
            set { Set(ref _StockTakeList, value); }
        }

        private StockTake _SelectedStockTake;
        public StockTake SelectedStockTake
        {
            get { return _SelectedStockTake; }
            set
            {
                Set(ref _SelectedStockTake, value);
                GetStockTakeSummary();
            }
        }

        private bool _ShowDiscrepancies;
        public bool ShowDiscrepancies
        {
            get { return _ShowDiscrepancies; }
            set
            {
                Set(ref _ShowDiscrepancies, value);
                RefreshStockTakeSummary();
            }
        }

        #endregion

        #region Commands

        public RelayCommand RefreshStockTakeListCommand => new RelayCommand(async () => await RefreshStockTakeList());
        public RelayCommand CompleteStockTakeCommand => new RelayCommand(async () => await OnCompleteStockTake());
        public RelayCommand AddNewStockTakeCommand => new RelayCommand(async () => await OnAddNewStockTake());
        public RelayCommand MakeCurrentStockCommand => new RelayCommand(async () => await OnMakeCurrentStock());



        #endregion

        private async Task OnMakeCurrentStock()
        {
            if (SelectedStockTake == null) return;
            var dlgRes = MessageBox.Show("Convert to current stock?", "", MessageBoxButton.OKCancel);
            if (dlgRes == MessageBoxResult.OK)
            {
                IsBusy = true;
                // await _stockRepo.MakeCurrentStock(SelectedStockTake.id);
                IsBusy = false;

                var dlgRes1 = MessageBox.Show("Stock Converted", "", MessageBoxButton.OKCancel);
                await RefreshStockTakeList();
            }
        }

        private async Task OnAddNewStockTake()
        {
            var dlgRes = MessageBox.Show("Add New Stock Take?", "", MessageBoxButton.OKCancel);
            if (dlgRes == MessageBoxResult.OK)
            {
                await _stockRepo.AddUpdateStockTake(new StockTake());
                await RefreshStockTakeList();
            }
        }

        private async Task OnCompleteStockTake()
        {
            if (SelectedStockTake == null) return;
            if (SelectedStockTake.DateCompleted == null)
            {
                var dlgRes = MessageBox.Show("Complete Stock Take?", "", MessageBoxButton.OKCancel);
                if (dlgRes == MessageBoxResult.OK)
                {
                    SelectedStockTake.DateCompleted = DateTime.Now;
                    await _stockRepo.CompleteStockTake(SelectedStockTake.id);
                    int selectedStockTakeId = SelectedStockTake.id;
                    await RefreshStockTakeList();
                    SelectedStockTake = StockTakeList.FirstOrDefault(a => a.id == selectedStockTakeId);
                }
            }
        }

        private async Task GetStockTakeSummary()
        {
            if(SelectedStockTake == null)return;
            // _allSummaryList = new List<vwStockTakeSummary>(await _stockRepo.GetStockTakeSummaryView(SelectedStockTake.id));
            // StockTakeSummaryList = new List<vwStockTakeSummary>(_allSummaryList);
            ShowDiscrepancies = false;
        }


        private async Task RefreshStockTakeSummary()
        {
            if (ShowDiscrepancies)
            {
                // StockTakeSummaryList = new List<vwStockTakeSummary>(_allSummaryList.Where(a => a.HasDifference));
            }
            else
            {
                // StockTakeSummaryList = new List<vwStockTakeSummary>(_allSummaryList);
            }

        }

        private async Task RefreshStockTakeList()
        {
            StockTakeList = await _stockRepo.GetStockTakeList();
        }

        public async void Load(FrameworkElement element)
        {
            await RefreshStockTakeList();
        }

        public void Unload(FrameworkElement element)
        {
            StockTakeList = null;
            SelectedStockTake = null;
            // StockTakeSummaryList = null;
        }
    }
}

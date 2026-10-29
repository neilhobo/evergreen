using System;
using System.Collections.Generic;
using Globeweigh.Shared;
using Globeweigh.Shared.Services;
using System.Windows;
using GalaSoft.MvvmLight.CommandWpf;
using System.Threading.Tasks;
using GalaSoft.MvvmLight.Ioc;
using Globeweigh.Model;
using Globeweigh.Model.Custom;
using MvvmDialogs;

namespace Globeweigh.Admin
{
    public class CurrentStockSummaryViewModel : AdminViewModelBase, IViewModel
    {
        #region private fields

        private readonly IDialogService _dialogService = SimpleIoc.Default.GetInstance<IDialogService>();
        private readonly IPalletRepository _palletRepo = SimpleIoc.Default.GetInstance<IPalletRepository>();
        private readonly IStockRepository _stockRepo = SimpleIoc.Default.GetInstance<IStockRepository>();

        #endregion

        #region Properties

        private List<vwCurrentStockSummary> _SummaryList;
        public List<vwCurrentStockSummary> SummaryList
        {
            get { return _SummaryList; }
            set { Set(ref _SummaryList, value); }
        }

        private vwCurrentStockSummary _SelectedSummary;
        public vwCurrentStockSummary SelectedSummary
        {
            get { return _SelectedSummary; }
            set
            {
                Set(ref _SelectedSummary, value);
                if (SelectedSummary == null) return;
                RefreshPalletList();
            }
        }

        private List<vwPalletCurrentStock> _PalletList;
        public List<vwPalletCurrentStock> PalletList
        {
            get { return _PalletList; }
            set
            {
                Set(ref _PalletList, value);
            }
        }

        private vwPalletCurrentStock _SelectedPallet;
        public vwPalletCurrentStock SelectedPallet
        {
            get { return _SelectedPallet; }
            set { Set(ref _SelectedPallet, value); }
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
                SummaryList = new List<vwCurrentStockSummary>(await _stockRepo.GetCurrentStockSummaryList());
                PalletList = null;
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

        public async Task RefreshPalletList()
        {
            try
            {
                IsBusy = true;
                PalletList = new List<vwPalletCurrentStock>( await _palletRepo.GetCurrentStockPalletsByProductId(SelectedSummary.ProductId));
                //PalletCount = PalletList.Count;
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

        public async void Load(FrameworkElement element)
        {
            IsBusy = false;
            await RefreshSummaryList();
        }

        public void Unload(FrameworkElement element)
        {
            IsBusy = true;
            PalletList = null;
            SelectedPallet = null;
            IsBusy = false;
        }
    }
}

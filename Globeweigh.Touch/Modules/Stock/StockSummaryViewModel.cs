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

namespace Globeweigh.Touch
{
    public class StockSummaryViewModel : TouchViewModelBase, IViewModel
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
                //RefreshPalletList();
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
                SummaryList = new List<vwCurrentStockSummary>(await _stockRepo.GetCurrentStockSummaryList());
                // PalletList = null;
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

        //public async Task RefreshPalletList()
        //{
        //    try
        //    {
        //        IsBusy = true;
        //        PalletList = new List<vwIntakePallet>( await _palletRepo.GetIntakePalletsByProductId(FilterDate.Date, SelectedSummary.ProductId));
        //        PalletCount = PalletList.Count;
        //        SelectedPallet = null;
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
            await RefreshSummaryList();
        }

        public void Unload(FrameworkElement element)
        {
            IsBusy = true;
            // PalletList = null;
            // SelectedPallet = null;
            IsBusy = false;
        }
    }
}

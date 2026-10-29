using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using Globeweigh.Shared;
using Globeweigh.Shared.Services;
using System.Windows;
using GalaSoft.MvvmLight.CommandWpf;
using System.Threading.Tasks;
using DevExpress.CodeParser;
using DevExpress.Xpf.CodeView;
using DevExpress.Xpf.Core;
using GalaSoft.MvvmLight.Ioc;
using Globeweigh.Model;
using Globeweigh.Model.Custom;
using Globeweigh.Model.Helpers;
using Globeweigh.Shared.Helpers;
using MvvmDialogs;

namespace Globeweigh.Admin
{
    public class SelectBulkPalletViewModel : DialogViewModelBase, IModalDialogViewModel, IViewModel
    {
        #region private fields

        private readonly IStockRepository _stockRepo = SimpleIoc.Default.GetInstance<IStockRepository>();
        private readonly IPalletRepository _palletRepo = SimpleIoc.Default.GetInstance<IPalletRepository>();
        private readonly IDialogService _dialogService = SimpleIoc.Default.GetInstance<IDialogService>();

        #endregion

        #region Properties

        public int CustomerId { get; set; }
        public int DispatchOrderId { get; set; }

        private ItemsChangeObservableCollection<vwPalletCurrentStock> _PalletList;
        public ItemsChangeObservableCollection<vwPalletCurrentStock> PalletList
        {
            get { return _PalletList; }
            set { Set(ref _PalletList, value); }
        }
        //
        private ObservableCollection<vwPalletCurrentStock> _SelectedPallets;
        public ObservableCollection<vwPalletCurrentStock> SelectedPallets
        {
            get { return _SelectedPallets; }
            set { Set(ref _SelectedPallets, value); }
        }

        private List<DispatchOrderItem> _AlreadySelectedPalletList;
        public List<DispatchOrderItem> AlreadySelectedPalletList
        {
            get { return _AlreadySelectedPalletList; }
            set { Set(ref _AlreadySelectedPalletList, value); }
        }

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

        private int? _QuantityToAdd;
        public int? QuantityToAdd
        {
            get { return _QuantityToAdd; }
            set { Set(ref _QuantityToAdd, value); }
        }

        #endregion

        #region Commands

        //public RelayCommand RefreshCommand => new RelayCommand(async () => await RefreshStock());
        public RelayCommand SelectCommand => new RelayCommand(OnSelect);
        public RelayCommand SelectPalletsCommand => new RelayCommand(OnSelectPallets, CanSelectPallets);


        #endregion

        private bool CanSelectPallets()
        {
            if (QuantityToAdd == null || QuantityToAdd <= 0) return false;
            if(!UtilitiesShared.IsNumeric(QuantityToAdd)) return false;
            if (PalletList == null || !PalletList.Any()) return false;
            if (PalletList.Count(a => !a.IsSelected) < QuantityToAdd) return false;
            return true;
        }


        private async void OnSelectPallets()
        {
            var palletsToSelect = PalletList.Where(a => a.IsSelected == false).Take((int) QuantityToAdd).ToList();
            foreach (var pallet in palletsToSelect)
            {
                pallet.IsSelected = true;
            }

            SelectedPallets = new ObservableCollection<vwPalletCurrentStock>(palletsToSelect);
            //this.DialogResult = true;
        }

        private async void OnSelect()
        {
            SelectedPallets = new ObservableCollection<vwPalletCurrentStock>(PalletList.Where(a => a.IsSelected).ToList());
            this.DialogResult = true;
        }

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

        private async Task RefreshPalletList()
        {
            var stockList = await _stockRepo.GetCurrentStockPalletsByProductId(SelectedSummary.ProductId);

            // List<vwPalletCurrentStock> list = new List<vwPalletCurrentStock>();
            // if (DispatchOrderId == 0)
            // {
            //     list = stockList.Where(a => a.PickedOrderId == null).ToList();
            // }
            // else
            // {
            //     list = stockList.Where(a => a.PickedOrderId == DispatchOrderId || a.PickedOrderId == null).ToList();
            // }


            PalletList = new ItemsChangeObservableCollection<vwPalletCurrentStock>();
            // PalletList.AddRange(list);

            // foreach (var vwPalletCurrentStock in list)
            // {
            //     PalletList.Add(vwPalletCurrentStock);
            // }

            // foreach (var selectedPallet in AlreadySelectedPalletList)
            // {
            //     var match = PalletList.FirstOrDefault(a => a.id == selectedPallet.PalletId);
            //     if (match != null)
            //     {
            //         match.IsSelected = true;
            //     }
            // }
        }
        //
        // public async Task RefreshPalletList()
        // {
        //     try
        //     {
        //         IsBusy = true;
        //         PalletList = new List<vwPalletCurrentStock>(await _palletRepo.GetCurrentStockPalletsByProductId(SelectedSummary.ProductId));
        //         //PalletCount = PalletList.Count;
        //         SelectedPallet = null;
        //         IsBusy = false;
        //     }
        //     catch (Exception ex)
        //     {
        //         IsBusy = false;
        //     }
        //     finally
        //     {
        //         IsBusy = false;
        //     }
        //
        // }


        public async void Load(FrameworkElement element)
        {
            await RefreshSummaryList();

            if (SelectedPallets == null) SelectedPallets = new ObservableCollectionCore<vwPalletCurrentStock>();
            //PalletList.CollectionChanged += PalletList_CollectionChanged;
        }

        public void Unload(FrameworkElement element)
        {
            //PalletList.CollectionChanged -= PalletList_CollectionChanged;
            PalletList = null;
            SummaryList = null;
            SelectedPallets = null;
            CustomerId = 0;
            QuantityToAdd = null;
            SelectedSummary = null;
        }
    }




}

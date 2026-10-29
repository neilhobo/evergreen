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
using DevExpress.Xpf.Core;
using GalaSoft.MvvmLight.Ioc;
using Globeweigh.Model;
using Globeweigh.Model.Custom;
using Globeweigh.Model.Helpers;
using MvvmDialogs;

namespace Globeweigh.Admin
{
    public class SelectPalletViewModel : DialogViewModelBase, IModalDialogViewModel, IViewModel
    {
        #region private fields

        private readonly IStockRepository _stockRepo = SimpleIoc.Default.GetInstance<IStockRepository>();
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

        private List<PalletSummaryObject> _SummaryList;
        public List<PalletSummaryObject> SummaryList
        {
            get { return _SummaryList; }
            set { Set(ref _SummaryList, value); }
        }

        private string _SelectedSummary;
        public string SelectedSummary
        {
            get { return _SelectedSummary; }
            set { Set(ref _SelectedSummary, value); }
        }

        #endregion

        #region Commands

        public RelayCommand RefreshCommand => new RelayCommand(async () => await RefreshStock());
        public RelayCommand SelectCommand => new RelayCommand(OnSelect);

        #endregion

        private async void OnSelect()
        {
            SelectedPallets = new ObservableCollection<vwPalletCurrentStock>(PalletList.Where(a => a.IsSelected).ToList());
            this.DialogResult = true;
        }

        private async Task RefreshStock()
        {
            var stockList = await _stockRepo.GetCurrentStock();

            // List<vwPalletCurrentStock> list = new List<vwPalletCurrentStock>();
            // if (DispatchOrderId == 0)
            // {
            //     list = stockList.Where(a =>  a.PickedOrderId == null).ToList();
            // }
            // else
            // {
            //     list = stockList.Where(a => (a.PickedOrderId == DispatchOrderId || a.PickedOrderId == null)).ToList();
            // }


            PalletList = new ItemsChangeObservableCollection<vwPalletCurrentStock>();

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

        public async void Load(FrameworkElement element)
        {
            await RefreshStock();

            if (SelectedPallets == null) SelectedPallets = new ObservableCollectionCore<vwPalletCurrentStock>();

            BuildSummaryString();
            PalletList.CollectionChanged += PalletList_CollectionChanged;
        }

        private void PalletList_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            BuildSummaryString();
        }

        // private void BuildSummaryString()
        // {
        //     var groupedList = PalletList.Where(a => a.IsSelected).GroupBy(a => a.CustomerPalletNo).ToList();
        //
        //     var list = groupedList.Select(@group => new PalletSummaryObject {CustomerPalletNo = @group.Key.ToString(), PalletCount = @group.Count()}).ToList();
        //     SummaryList = new List<PalletSummaryObject>(list);
        // }


        private void BuildSummaryString()
        {
            List<PalletSummaryObject> groupedList = PalletList.Where(a => a.IsSelected)
                .GroupBy(row => new { row.ProductId, row.ProductName, row.Size, row.Upp })
                .Select(g => new PalletSummaryObject()
                {
                    ProductName = g.Key.ProductName,
                    Size = g.Key.Size,
                    Upp = g.Key.Upp,
                    PalletCount = g.Count()
                    //,
                    //UnitsCount = g.Sum(x => x.NoOfUnits)
                })
                .ToList();

            SummaryList = new List<PalletSummaryObject>(groupedList.OrderBy(a => a.ProductName));
        }


        public void Unload(FrameworkElement element)
        {
            PalletList.CollectionChanged -= PalletList_CollectionChanged;
            PalletList = null;
            SelectedPallets = null;
            CustomerId = 0;
            SelectedSummary = null;
        }
    }

    public partial class PalletSummaryObject
    {
        public string ProductName { get; set; }
        public int PalletCount { get; set; }
        public int? Size { get; set; }
        public int? Upp { get; set; }
    }


}

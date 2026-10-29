using GalaSoft.MvvmLight.CommandWpf;
using GalaSoft.MvvmLight.Ioc;
using Globeweigh.Model;
using Globeweigh.Model.Custom;
using Globeweigh.Shared;
using Globeweigh.Shared.Services;
using MvvmDialogs;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace Globeweigh.Admin
{
    public class ProductionSummaryViewModel : AdminViewModelBase, IViewModel
    {
        #region private fields

        private readonly IStockRepository _stockRepo = SimpleIoc.Default.GetInstance<IStockRepository>();
        private readonly IDialogService _dialogService = SimpleIoc.Default.GetInstance<IDialogService>();
        private readonly IPalletRepository _palletRepo = SimpleIoc.Default.GetInstance<IPalletRepository>();

        #endregion

        #region Properties

        private List<ProductionSummaryItem> _ProductList;
        public List<ProductionSummaryItem> ProductList
        {
            get { return _ProductList; }
            set { Set(ref _ProductList, value); }
        }

        private ProductionSummaryItem _SelectedProduct;
        public ProductionSummaryItem SelectedProduct
        {
            get { return _SelectedProduct; }
            set
            {
                Set(ref _SelectedProduct, value);
                SelectedProductChanged();
            }
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

        private List<StockHistory> _HistoryList;
        public List<StockHistory> HistoryList
        {
            get { return _HistoryList; }
            set { Set(ref _HistoryList, value); }
        }

        private bool _GroupByLine;
        public bool GroupByLine
        {
            get { return _GroupByLine; }
            set
            {
                Set(ref _GroupByLine, value);
                _ = RefreshProducts(); // auto refresh when toggled
            }
        }

        #endregion

        #region Commands

        public RelayCommand RefreshCommand => new RelayCommand(async () => await RefreshProducts());

        #endregion

        private async void SelectedProductChanged()
        {
            if(SelectedProduct == null)return;
            await RefreshHistory();

        }

        private async Task RefreshProducts()
        {
            try
            {
                IsBusy = true;

                if (GroupByLine)
                {
                    var data = await _palletRepo.GetProductionSummaryByLine(DateFrom, DateTo);

                    ProductList = data.Select(x => new ProductionSummaryItem
                    {
                        LineNumber = x.LineNumber,
                        id = x.id,
                        Gtin = x.Gtin,
                        Description = x.Description,
                        Size = x.Size,
                        Upp = x.Upp,
                        RecordCount = x.RecordCount ?? 0,
                        BagCount = x.BagCount ?? 0
                    }).ToList();
                }
                else
                {
                    var data = await _palletRepo.GetProductionSummary(DateFrom, DateTo);

                    ProductList = data.Select(x => new ProductionSummaryItem
                    {
                        LineNumber = null, // 👈 no line in this mode
                        id = x.id,
                        Gtin = x.Gtin,
                        Description = x.Description,
                        Size = x.Size,
                        Upp = x.Upp,
                        RecordCount = x.RecordCount ?? 0,
                        BagCount = x.BagCount ?? 0
                    }).ToList();
                }
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task RefreshHistory()
        {
            try
            {
                if (SelectedProduct == null || SelectedProduct.id == null)
                {
                    HistoryList = null;
                    return;
                }
                IsBusy = true;
                HistoryList = new List<StockHistory>(await _stockRepo.GetStockHistory((int)SelectedProduct.id));
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

        public async void Load(FrameworkElement element)
        {
            DateFrom = DateTime.Today.AddDays(-5);
            DateTo = DateTime.Today.AddDays(1);
            await RefreshProducts();
        }

        public void Unload(FrameworkElement element)
        {
            ProductList = null;
            SelectedProduct = null;
        }
    }
}

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
using DevExpress.Office.Utils;
using DevExpress.Xpf.Core;
using GalaSoft.MvvmLight.Ioc;
using Globeweigh.Model;
using Globeweigh.Model.Custom;
using MvvmDialogs;

namespace Globeweigh.Admin
{
    public class PalletHistoryViewModel : DialogViewModelBase, IModalDialogViewModel, IViewModel
    {
        #region private fields

        private readonly IPalletRepository _palletRepo = SimpleIoc.Default.GetInstance<IPalletRepository>();

        #endregion

        #region Properties

        public int PalletId { get; set; }

        private List<vwPalletHistory> _HistoryList;
        public List<vwPalletHistory> HistoryList
        {
            get { return _HistoryList; }
            set { Set(ref _HistoryList, value); }
        }


        #endregion

        #region Commands

        #endregion


        private async Task RefreshHistory()
        {
            HistoryList = new List<vwPalletHistory>(await _palletRepo.GetPalletHistory(PalletId));
        }

        public async void Load(FrameworkElement element)
        {
            await RefreshHistory();
        }

        public void Unload(FrameworkElement element)
        {

            HistoryList = null;

        }
    }


}

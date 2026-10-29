using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MvvmDialogs;
using System.Windows;
using System.Windows.Threading;
using GalaSoft.MvvmLight.CommandWpf;
using Globeweigh.Model;
using GalaSoft.MvvmLight.Ioc;
using Globeweigh.Shared;
using Globeweigh.Shared.Helpers;
using Globeweigh.Shared.Network;
using Globeweigh.Shared.Services;
using System.Net;
using System.Text;
using System.Net.Sockets;
using System.Collections.ObjectModel;
using System.Linq;
using DevExpress.Mvvm.Native;

namespace Globeweigh.Admin
{
    public class ViewLoadDetailsViewModel : DialogViewModelBase, IModalDialogViewModel, IViewModel
    {
        #region private fields

        private readonly IWeighbridgeRepository _weighbridgeRepo = SimpleIoc.Default.GetInstance<IWeighbridgeRepository>();

        #endregion

        #region Properties

        public int LoadId { get; set; }

        private ObservableCollection<vwDispatchOrder> _OrderList;
        public ObservableCollection<vwDispatchOrder> OrderList
        {
            get { return _OrderList; }
            set { Set(ref _OrderList, value); }
        }

        private WeighbridgeRecord _CurrentWeighbridgeRecord;
        public WeighbridgeRecord CurrentWeighbridgeRecord
        {
            get { return _CurrentWeighbridgeRecord; }
            private set { Set(ref _CurrentWeighbridgeRecord, value); }
        }

        private vwWeighbridgePending _CurrentWeighbridgeRecordView;
        public vwWeighbridgePending CurrentWeighbridgeRecordView
        {
            get { return _CurrentWeighbridgeRecordView; }
            private set { Set(ref _CurrentWeighbridgeRecordView, value); }
        }


        #endregion

        #region Commands

        public RelayCommand SaveCommand => new RelayCommand(OnSave);

        #endregion

        private async void OnSave()
        {
            DialogResult = true;
        }

        public async void Load(FrameworkElement element)
        {
            CurrentWeighbridgeRecord = await _weighbridgeRepo.GetWeighbridgeRecord(LoadId, true, true);
            CurrentWeighbridgeRecordView = await _weighbridgeRepo.GetWeighbridgeRecordPendingView(LoadId);
        }

        public void Unload(FrameworkElement element)
        {
            CurrentWeighbridgeRecord = null;
            LoadId = 0;
            DialogResult = null;
        }

    }
}

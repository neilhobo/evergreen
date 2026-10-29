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
    public class EditOutgoingViewModel : DialogViewModelBase, IModalDialogViewModel, IViewModel
    {
        #region private fields

        private readonly IWeighbridgeRepository _weighbridgeRepo = SimpleIoc.Default.GetInstance<IWeighbridgeRepository>();
        private readonly IReferenceDataRepository _refDataRepo = SimpleIoc.Default.GetInstance<IReferenceDataRepository>();
        private readonly IHaulierRepository _haulierRepo = SimpleIoc.Default.GetInstance<IHaulierRepository>();
        private readonly IDialogService _dialogService = SimpleIoc.Default.GetInstance<IDialogService>();
        private readonly IDispatchOrderRepository _dispatchRepo = SimpleIoc.Default.GetInstance<IDispatchOrderRepository>();

        private DispatcherTimer _timerRelay;
        // private List<vwDispatchOrder> _openList;
        #endregion

        #region Properties

        public int WeighbridgeRecordId { get; set; }

        private ObservableCollection<vwDispatchOrder> _DispatchOrderList;
        public ObservableCollection<vwDispatchOrder> DispatchOrderList
        {
            get { return _DispatchOrderList; }
            set { Set(ref _DispatchOrderList, value); }
        }

        private WeighbridgeRecord _CurrentWeighbridgeRecord;
        public WeighbridgeRecord CurrentWeighbridgeRecord
        {
            get { return _CurrentWeighbridgeRecord; }
            private set { Set(ref _CurrentWeighbridgeRecord, value); }
        }

        private List<Haulier> _HaulierList;
        public List<Haulier> HaulierList
        {
            get { return _HaulierList; }
            set { Set(ref _HaulierList, value); }
        }

        private Haulier _SelectedHaulier;
        public Haulier SelectedHaulier
        {
            get { return _SelectedHaulier; }
            set
            {
                Set(ref _SelectedHaulier, value);
                if (SelectedHaulier != null) RefreshDispatchOrders();
            }
        }

        #endregion

        #region Commands

        public RelayCommand EditSaveCommand => new RelayCommand(OnEditSave);
        public RelayCommand SaveCommand => new RelayCommand(OnSave, CanSave);

        #endregion

        private async Task RefreshDispatchOrders()
        {
            try
            {
                IsBusy = true;
                var alreadySelectedList = await _dispatchRepo.GetDispatchOrderViewsByWeighbridgeRecordId(WeighbridgeRecordId);
                // var list = await _dispatchRepo.GetOpenDispatchOrderViews(CurrentWeighbridgeRecord.HaulierId);
  
                foreach (var alreadySelected in alreadySelectedList)
                {
                    alreadySelected.IsSelected = true;
                }
                // list.AddRange(alreadySelectedList);
                // DispatchOrderList = new ObservableCollection<vwDispatchOrder>(list);
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

        private async void OnEditSave()
        {
            // CurrentWeighbridgeRecord.HaulierName = SelectedHaulier.DisplayName;
            // CurrentWeighbridgeRecord.VehicleReg = SelectedVehicle.DisplayName;
            await _weighbridgeRepo.AddUpdateAsync(CurrentWeighbridgeRecord);
            DialogResult = true;
        }

        private async void OnSave()
        {
            await _weighbridgeRepo.AddUpdateOutgoing(CurrentWeighbridgeRecord, DispatchOrderList.Where(a => a.IsSelected).ToList());
            DialogResult = true;
        }

        private bool CanSave()
        {
            if (CurrentWeighbridgeRecord == null) return false;
            if (CurrentWeighbridgeRecord.HaulierId == 0) return false;
            if (SelectedHaulier == null) return false;
            return true;
        }

        public async void Load(FrameworkElement element)
        {
            if (UtilitiesShared.InDesignMode) return;
            HaulierList = new List<Haulier>(await _haulierRepo.GetHaulierList());
            CurrentWeighbridgeRecord = await _weighbridgeRepo.GetWeighbridgeRecord(WeighbridgeRecordId,  true, false);


            await RefreshDispatchOrders();


        }

        public void Unload(FrameworkElement element)
        {
            SelectedHaulier = null;
            CurrentWeighbridgeRecord = null;
            WeighbridgeRecordId = 0;
            DialogResult = null;

        }


    }
}

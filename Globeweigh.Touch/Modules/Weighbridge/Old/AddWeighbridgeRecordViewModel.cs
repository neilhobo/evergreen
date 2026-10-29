using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using Globeweigh.Shared;
using Globeweigh.Model;
using Globeweigh.Shared.Services;
using MvvmDialogs;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using DevExpress.CodeParser;
using GalaSoft.MvvmLight.CommandWpf;
using GalaSoft.MvvmLight.Ioc;
using GalaSoft.MvvmLight.Threading;
using Globeweigh.Shared.Helpers;

namespace Globeweigh.Touch
{
    public class AddWeighbridgeRecordViewModel : DialogViewModelBase, IModalDialogViewModel, IViewModel
    {
        #region private fields

        private readonly IWeighbridgeRepository _weighbridgeRepo = SimpleIoc.Default.GetInstance<IWeighbridgeRepository>();
        private readonly IHaulierRepository _haulierRepo = SimpleIoc.Default.GetInstance<IHaulierRepository>();
        private readonly IDialogService _dialogService = SimpleIoc.Default.GetInstance<IDialogService>();
        private SerialPort _serialCom1;
        private DispatcherTimer _timer;
        private int _serialDebugCount;
        private TextBlock _txbWeight1;

        #endregion

        #region Properties

        public int WeighbridgeRecordId { get; set; }

        private bool _IsOutgoing;

        public bool IsOutgoing
        {
            get => _IsOutgoing;
            set => Set(ref _IsOutgoing, value);
        }

        private string _ManualWeight;

        public string ManualWeight
        {
            get => _ManualWeight;
            set => Set(ref _ManualWeight, value);
        }

        private bool _IsManual;

        public bool IsManual
        {
            get => _IsManual;
            set => Set(ref _IsManual, value);
        }


        private bool _InEditMode;
        public bool InEditMode
        {
            get => _InEditMode;
            set => Set(ref _InEditMode, value);
        }

        private WeighbridgeRecord _CurrentWeighbridgeRecord;
        public WeighbridgeRecord CurrentWeighbridgeRecord
        {
            get => _CurrentWeighbridgeRecord;
            private set => Set(ref _CurrentWeighbridgeRecord, value);
        }

        private vwWeighbridgePreLoad _CurrentWeighbridgeRecordView;
        public vwWeighbridgePreLoad CurrentWeighbridgeRecordView
        {
            get => _CurrentWeighbridgeRecordView;
            set => Set(ref _CurrentWeighbridgeRecordView, value);
        }

        private string _LiveWeight;
        public string LiveWeight
        {
            get => _LiveWeight;
            set => Set(ref _LiveWeight, value);
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
            set { Set(ref _SelectedHaulier, value); }
        }

        #endregion

        #region Commands

        public RelayCommand EditSaveCommand => new RelayCommand(OnEditSave);
        public RelayCommand SaveCommand => new RelayCommand(OnSave, CanSave);

        #endregion



        private async void OnEditSave()
        {
            // await _weighbridgeRepo.AddUpdateAsync(CurrentWeighbridgeRecord, false);
            DialogResult = true;
        }

        public async void MouseUpCommand(string name)
        {
            if (name == "ManualWeight")
            {
                var dialogVm = SimpleIoc.Default.GetInstance<KeypadViewModel>();
                dialogVm.HeaderText = "Enter Weight";
                var success = _dialogService.ShowDialog<KeypadView>(this, dialogVm);
                if (success == true) ManualWeight = dialogVm.KeypadValue;
            }
            else if (name == "Comments")
            {
                var dialogVm = SimpleIoc.Default.GetInstance<KeyboardViewModel>();
                dialogVm.HeaderText = "Enter Comments";
                var success = _dialogService.ShowDialog<KeyboardView>(this, dialogVm);
                if (success == true) CurrentWeighbridgeRecord.Comments = dialogVm.KeyboardValue;
            }
            else if (name == "VehicleReg")
            {
                var dialogVm = SimpleIoc.Default.GetInstance<KeyboardViewModel>();
                dialogVm.HeaderText = "Enter Vehicle Reg";
                // dialogVm.UpperCase = true;

                var success = _dialogService.ShowDialog<KeyboardView>(this, dialogVm);
                if (success == true) CurrentWeighbridgeRecord.VehicleReg = dialogVm.KeyboardValue;
            }
            else if (name == "TrailerNo")
            {
                var dialogVm = SimpleIoc.Default.GetInstance<KeypadViewModel>();
                dialogVm.HeaderText = "Enter Trailer No";
                dialogVm.IsDecimalPointVisible = true;
                var success = _dialogService.ShowDialog<KeypadView>(this, dialogVm);
                if (success == true) CurrentWeighbridgeRecord.TrailerNo = dialogVm.KeypadValue;
            }
            else if (name == "Haulier")
            {
                var dialogVm = SimpleIoc.Default.GetInstance<SelectHaulierViewModel>();
                dialogVm.HaulierList = new List<Haulier>(HaulierList);
                var success = _dialogService.ShowDialog<SelectHaulierView>(this, dialogVm);
                if (success == true)
                {
                    CurrentWeighbridgeRecord.HaulierId = dialogVm.SelectedHaulier.id;
                    // CurrentWeighbridgeRecord.HaulierName = dialogVm.SelectedHaulier.Name;
                }
            }
        }

        private async void OnSave()
        {
            if (IsBusy) return;

            try
            {
                IsBusy = true;
                int weight;
                weight = Convert.ToInt32(ManualWeight);

                if (IsOutgoing)
                {
                    CurrentWeighbridgeRecord.WeightIn = weight;
                    CurrentWeighbridgeRecord.DateWeightIn = DateTime.Now;
                }
                else
                {
                    CurrentWeighbridgeRecord.WeightOut = weight;
                    CurrentWeighbridgeRecord.DateWeightOut = DateTime.Now;
                }

                await _weighbridgeRepo.AddUpdateAsync(CurrentWeighbridgeRecord);

                IsBusy = false;
                DialogResult = true;
            }
            catch (Exception e)
            {
                IsBusy = false;
                ExceptionHelper.ShowErrorWindow(e, "Add Weighbridge record", false);
            }
        }

        private bool CanSave()
        {
            if (CurrentWeighbridgeRecord == null) return false;
            if (CurrentWeighbridgeRecord.HaulierId == 0) return false;
            if (string.IsNullOrEmpty(ManualWeight)) return false;

            return true;
        }

        public async void Load(FrameworkElement element)
        {
            if (UtilitiesShared.InDesignMode) return;
            HaulierList = new List<Haulier>(await _haulierRepo.GetHaulierList());
            

            if (WeighbridgeRecordId == 0)
            {
                CurrentWeighbridgeRecord = new WeighbridgeRecord();
                CurrentWeighbridgeRecord.IsOutgoing = IsOutgoing;
            }
            else
            {
                CurrentWeighbridgeRecord = await _weighbridgeRepo.GetWeighbridgeRecord(WeighbridgeRecordId,  false, false);
                CurrentWeighbridgeRecordView = await _weighbridgeRepo.GetWeighbridgeRecordPreloadView(WeighbridgeRecordId);
                CurrentWeighbridgeRecord.IsOutgoing = IsOutgoing;
            }

            CurrentWeighbridgeRecord.PropertyChanged += CurrentWeighbridgeRecord_PropertyChanged;


        }

        private void CurrentWeighbridgeRecord_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            // if (e.PropertyName == "OrderType") CurrentWeighbridgeRecord.RaisePropertiesChangedManual();
        }

        public void Unload(FrameworkElement element)
        {

            CurrentWeighbridgeRecord.PropertyChanged -= CurrentWeighbridgeRecord_PropertyChanged;
            CurrentWeighbridgeRecord = null;
            WeighbridgeRecordId = 0;
            DialogResult = null;
            IsOutgoing = false;
            InEditMode = false;
            IsManual = false;
        }
    }
}
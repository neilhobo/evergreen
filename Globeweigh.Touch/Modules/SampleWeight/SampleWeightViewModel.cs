using GalaSoft.MvvmLight.CommandWpf;
using GalaSoft.MvvmLight.Ioc;
using Globeweigh.Model;
using Globeweigh.Shared;
using Globeweigh.Shared.Services;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using IDialogService = MvvmDialogs.IDialogService;

namespace Globeweigh.Touch
{
    public class SampleWeightViewModel : TouchViewModelBase, IViewModel
    {
        #region private fields

        private readonly IDialogService _dialogService = SimpleIoc.Default.GetInstance<IDialogService>();
        private readonly IDispatchOrderRepository _dispatchRepo = SimpleIoc.Default.GetInstance<IDispatchOrderRepository>();
        private readonly IWeighbridgeRepository _weighbridgeRepo = SimpleIoc.Default.GetInstance<IWeighbridgeRepository>();
        private DispatcherTimer _refreshTimer;


        #endregion

        #region Properties

        private List<vwWeighbridgeRecordsToBeSampled> _RecordList;
        public List<vwWeighbridgeRecordsToBeSampled> RecordList
        {
            get { return _RecordList; }
            set { Set(ref _RecordList, value); }
        }

        private vwWeighbridgeRecordsToBeSampled _SelectedRecord;
        public vwWeighbridgeRecordsToBeSampled SelectedRecord
        {
            get { return _SelectedRecord; }
            set { Set(ref _SelectedRecord, value);
                SelectedRecordChanged();
            }
        }

        private decimal? _SampleWeight;
        public decimal? SampleWeight
        {
            get { return _SampleWeight; }
            set { Set(ref _SampleWeight, value); }
        }

        private DateTime _FilterDate;
        public DateTime FilterDate
        {
            get { return _FilterDate; }
            set { Set(ref _FilterDate, value); }
        }

        #endregion

        #region Commands

        public RelayCommand HomeCommand => new RelayCommand(OnHome);
        public RelayCommand FilterDateBackCommand => new RelayCommand(OnFilterDateBack);
        public RelayCommand FilterDateForwardCommand => new RelayCommand(OnFilterDateForward);

        #endregion

        private async void OnFilterDateBack()
        {
            FilterDate = FilterDate.AddDays(-1);
            await RefreshRecords();
        }

        private async void OnFilterDateForward()
        {
            FilterDate = FilterDate.AddDays(1);
            await RefreshRecords();
        }

        public async void MouseUpCommand(string name)
        {
            if (name == "TestWeight")
            {
                var dialogViewModel = SimpleIoc.Default.GetInstance<KeypadViewModel>();
                dialogViewModel.HeaderText = "Enter Sample Weight (kg)";
                dialogViewModel.IsDecimalPointVisible = true;
                bool? success = _dialogService.ShowDialog<KeypadView>(this, dialogViewModel);
                if (success == true)
                {
                    SampleWeight = Convert.ToDecimal(dialogViewModel.KeypadValue);

                    var prevSelectedId = SelectedRecord?.id; // remember selection

                    await _weighbridgeRepo.AddSampleWeight(SelectedRecord.id, (decimal)SampleWeight);
                    await RefreshRecords();

                    // restore selection if still in the refreshed list
                    if (prevSelectedId != null)
                        SelectedRecord = RecordList?.Find(r => r.id == prevSelectedId);
                }
            }
        }

        private async void SelectedRecordChanged()
        {
            if(SelectedRecord == null)return;
            SampleWeight = SelectedRecord.SampleBoxWeight;
        }

        private void OnHome()
        {
            IsBusy = true;
            SimpleIoc.Default.GetInstance<MainWindowViewModel>().CurrentViewModel = SimpleIoc.Default.GetInstance<HomeViewModel>();
        }


        private async Task RefreshRecords(bool preserveSelection = false)
        {
            try
            {
                IsBusy = true;

                var prevSelectedId = preserveSelection ? SelectedRecord?.id : null;

                RecordList = new List<vwWeighbridgeRecordsToBeSampled>(
                    await _dispatchRepo.GetWeighbridgeRecordsToBeSampled(FilterDate)
                );

                if (prevSelectedId != null)
                    SelectedRecord = RecordList?.Find(r => r.id == prevSelectedId);
                else
                    SelectedRecord = null;
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
            FilterDate = DateTime.Today;
            _refreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(60) // adjust as needed
            };
            _refreshTimer.Tick += async (s, e) => await RefreshRecords(preserveSelection: true);
            _refreshTimer.Start();
            await RefreshRecords();
        }

        public void Unload(FrameworkElement element)
        {
            _refreshTimer.Stop();
            RecordList = null;
            SelectedRecord = null;
            IsBusy = false;
        }
    }
}

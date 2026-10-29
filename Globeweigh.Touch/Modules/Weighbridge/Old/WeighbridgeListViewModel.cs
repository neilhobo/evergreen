using System;
using System.Collections.Generic;
using GalaSoft.MvvmLight.Ioc;
using Globeweigh.Model;
using Globeweigh.Shared;
using System.Windows;
using GalaSoft.MvvmLight.CommandWpf;
using System.Threading.Tasks;
using Globeweigh.Shared.Services;
using IDialogService = MvvmDialogs.IDialogService;

namespace Globeweigh.Touch
{
    public class WeighbridgeListViewModel : TouchViewModelBase, IViewModel
    {
        #region private fields

        private readonly IWeighbridgeRepository _weighbridgeRepo = SimpleIoc.Default.GetInstance<IWeighbridgeRepository>();
        private readonly IDialogService _dialogService = SimpleIoc.Default.GetInstance<IDialogService>();

        #endregion

        #region Properties

        private List<vwWeighbridgePending> _PendingList;
        public List<vwWeighbridgePending> PendingList
        {
            get { return _PendingList; }
            set { Set(ref _PendingList, value); }
        }

        private List<WeighbridgeRecord> _WeighbridgeRecordList;
        public List<WeighbridgeRecord> WeighbridgeRecordList
        {
            get { return _WeighbridgeRecordList; }
            set { Set(ref _WeighbridgeRecordList, value); }
        }

        private vwWeighbridgePending _SelectedPending;
        public vwWeighbridgePending SelectedPending
        {
            get { return _SelectedPending; }
            set { Set(ref _SelectedPending, value); }
        }

        private WeighbridgeRecord _SelectedWeighbridgeRecord;
        public WeighbridgeRecord SelectedWeighbridgeRecord
        {
            get { return _SelectedWeighbridgeRecord; }
            set { Set(ref _SelectedWeighbridgeRecord, value); }
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

        private bool _ErrorFound;
        public bool ErrorFound
        {
            get { return _ErrorFound; }
            set { Set(ref _ErrorFound, value); }
        }

        private bool _PendingErrorFound;
        public bool PendingErrorFound
        {
            get { return _PendingErrorFound; }
            set { Set(ref _PendingErrorFound, value); }
        }

        private bool _IsPendingBusy;
        public bool IsPendingBusy
        {
            get { return _IsPendingBusy; }
            set { Set(ref _IsPendingBusy, value); }
        }

        #endregion

        #region Commands

        public RelayCommand HomeCommand => new RelayCommand(() => SimpleIoc.Default.GetInstance<MainWindowViewModel>().CurrentViewModel = SimpleIoc.Default.GetInstance<HomeViewModel>());
        public RelayCommand AddOutgoingCommand => new RelayCommand(OnAddOutgoing);
        public RelayCommand AddIncomingCommand => new RelayCommand(OnAddIncoming);
        public RelayCommand WeighInOutCommand => new RelayCommand(OnWeighInOut);
        public RelayCommand DeletePendingCommand => new RelayCommand(OnDeletePending);
        public RelayCommand EditPendingCommand => new RelayCommand(OnEditPending);
        public RelayCommand ViewDetailsCommand => new RelayCommand(OnViewDetails);
        public RelayCommand ViewDetailsPendingCommand => new RelayCommand(OnViewDetailsPending);
        public RelayCommand RetryPendingRefreshCommand => new RelayCommand(async () => await RefreshPending());
        public RelayCommand RetryWeighbridgeRefreshCommand => new RelayCommand(async () => await RefreshWeighbridgeRecords());
        public RelayCommand PreviewDocketCommand => new RelayCommand(OnPreviewDocket);
        public RelayCommand PrintDocketCommand => new RelayCommand(OnPrintDocket);
        public RelayCommand LookupBatchCommand => new RelayCommand(OnLookupBatch);



        #endregion

        private async void OnLookupBatch()
        {
            // var dialogVm = SimpleIoc.Default.GetInstance<KeypadViewModel>();
            // dialogVm.HeaderText = "Enter Order No:";
            // bool? success = _dialogService.ShowDialog<KeypadView>(this, dialogVm);
            // if (success == true)
            // {
            //     var returnedOrderNo = Convert.ToInt32(dialogVm.KeypadValue);
            //     var id =await _weighbridgeRepo.GetWeighbridgeRecordIdByOrderNo(returnedOrderNo);
            //     if (id == 0)
            //     {
            //         var dialogVm1 = SimpleIoc.Default.GetInstance<OkCancelViewModel>();
            //         dialogVm1.MessageText = "Order Not found";
            //         dialogVm1.HideCancel = true;
            //         bool? success1 = _dialogService.ShowDialog<OkCancelView>(this, dialogVm1);
            //         if (success1 == true)
            //         {
            //             return;
            //         }
            //     }
            //     var vm = SimpleIoc.Default.GetInstance<WeighbridgeRecordDetailViewModel>();
            //     vm.WeighbridgeRecordId = id;
            //     SimpleIoc.Default.GetInstance<MainWindowViewModel>().CurrentViewModel = vm;
            // }
        }

        private async void OnPreviewDocket()
        {
            // var record = await _weighbridgeRepo.GetWeighbridgeRecord(SelectedWeighbridgeRecord.id);
            //
            //
            // if (record.DriverSignature == null)
            // {
            //     var dialogVm = SimpleIoc.Default.GetInstance<CaptureSignatureViewModel>();
            //     dialogVm.RecordId = SelectedWeighbridgeRecord.id;
            //     bool? success = _dialogService.ShowDialog<CaptureSignatureView>(this, dialogVm);
            //     if (success == true)
            //     {
            //
            //     }
            //     else
            //     {
            //         return;
            //     }
            //
            // }
            //
            // var recordView = await _weighbridgeRepo.GetWeighbridgeRecordView(SelectedWeighbridgeRecord.id);
            // record = await _weighbridgeRepo.GetWeighbridgeRecord(SelectedWeighbridgeRecord.id);
            // if (record.DriverSignature != null)
            // {
            //     recordView.tmp_Signature = record.DriverSignature;
            // }
            // var report = new rptWeighbridgeDocket(recordView, record);
            // await report.CreateDocumentAsync();
            // var dialogVm1 = SimpleIoc.Default.GetInstance<ViewDocumentViewModel>();
            // dialogVm1.CurrentReport = report;
            // // if (!CurrentDispatchOrder.TargetMet) dialogVm.HideDocPreviewCommandBar = true;
            // bool? success1 = _dialogService.ShowDialog<ViewDocumentView>(this, dialogVm1);
            // if (success1 == true)
            // {
            // }


        }

        private async void OnPrintDocket()
        {
            // var record = await _weighbridgeRepo.GetWeighbridgeRecord(SelectedWeighbridgeRecord.id);
            // var recordView = await _weighbridgeRepo.GetWeighbridgeRecordView(SelectedWeighbridgeRecord.id);
            //
            // var report = new rptWeighbridgeDocket(recordView, record);
            // await report.CreateDocumentAsync();

            // var dialogVm = SimpleIoc.Default.GetInstance<ViewDocumentViewModel>();
            // dialogVm.CurrentReport = report;
            // // if (!CurrentDispatchOrder.TargetMet) dialogVm.HideDocPreviewCommandBar = true;
            // bool? success = _dialogService.ShowDialog<ViewDocumentView>(this, dialogVm);
            // if (success == true)
            // {
            // }
        }

        private void OnViewDetails()
        {
            // if (SelectedWeighbridgeRecord == null) return;
            // var vm = SimpleIoc.Default.GetInstance<WeighbridgeRecordDetailViewModel>();
            // vm.WeighbridgeRecordId = SelectedWeighbridgeRecord.id;
            // SimpleIoc.Default.GetInstance<MainWindowViewModel>().CurrentViewModel = vm;
        }

        private void OnViewDetailsPending()
        {
            // if (SelectedPending == null) return;
            // var vm = SimpleIoc.Default.GetInstance<WeighbridgeRecordDetailViewModel>();
            // vm.WeighbridgeRecordId = SelectedPending.id;
            // SimpleIoc.Default.GetInstance<MainWindowViewModel>().CurrentViewModel = vm;
        }

        private async void OnWeighInOut()
        {
            var dialogVm = SimpleIoc.Default.GetInstanceWithoutCaching<AddWeighbridgeRecordViewModel>();
            dialogVm.WeighbridgeRecordId = SelectedPending.id;
            dialogVm.IsOutgoing = !SelectedPending.IsOutgoing;
            bool? success = _dialogService.ShowDialog<AddWeighbridgeRecordView>(this, dialogVm);
            if (success == true)
            {
                await RefreshPending();
                await RefreshWeighbridgeRecords();
            }
        }

        private async void OnAddOutgoing()
        {
            var dialogVm = SimpleIoc.Default.GetInstanceWithoutCaching<AddWeighbridgeRecordViewModel>();
            dialogVm.HeaderText = "Add Outgoing Weigh";
            dialogVm.IsOutgoing = true;
            bool? success = _dialogService.ShowDialog<AddWeighbridgeRecordView>(this, dialogVm);
            if (success == true)
            {
                await RefreshPending();
            }
        }

        private async void OnAddIncoming()
        {
            var dialogVm = SimpleIoc.Default.GetInstanceWithoutCaching<AddWeighbridgeRecordViewModel>();
            dialogVm.HeaderText = "Add Incoming Weigh";
            dialogVm.IsOutgoing = false;
            bool? success = _dialogService.ShowDialog<AddWeighbridgeRecordView>(this, dialogVm);
            if (success == true)
            {
                await RefreshPending();
            }
        }

        private async void OnDeletePending()
        {
            var dialogVm = SimpleIoc.Default.GetInstanceWithoutCaching<OkCancelViewModel>();
            dialogVm.MessageText = "Delete Pending Item?";
            bool? success = _dialogService.ShowDialog<OkCancelView>(this, dialogVm);
            if (success == true)
            {
                await _weighbridgeRepo.MarkAsDeletedAsync(SelectedPending.id);
                await RefreshPending();
                SelectedPending = null;
            }
        }

        private async void OnEditPending()
        {
            var dialogVm = SimpleIoc.Default.GetInstanceWithoutCaching<AddWeighbridgeRecordViewModel>();
            dialogVm.HeaderText = "Edit Incoming Weigh";
            dialogVm.InEditMode = true;
            dialogVm.WeighbridgeRecordId = SelectedPending.id;
            bool? success = _dialogService.ShowDialog<AddWeighbridgeRecordView>(this, dialogVm);
            if (success == true)
            {
                await RefreshPending();
            }
        }

        private async Task RefreshPending()
        {
            try
            {
                IsPendingBusy = true;
                PendingErrorFound = false;
                PendingList = new List<vwWeighbridgePending>(await _weighbridgeRepo.GetPendingWeighbridgeRecords());
                IsPendingBusy = false;
            }
            catch (Exception e)
            {
                IsPendingBusy = false;
                PendingErrorFound = true;
                ExceptionHelper.LogError(e, "RefreshPending");
            }
        }

        private async Task RefreshWeighbridgeRecords()
        {
            try
            {
                IsBusy = true;
                ErrorFound = false;
                WeighbridgeRecordList = new List<WeighbridgeRecord>(await _weighbridgeRepo.GetWeighbridgeRecords(DateFrom.Date, DateTo.Date));
                IsBusy = false;
            }
            catch (Exception e)
            {
                IsBusy = false;
                ErrorFound = true;
                ExceptionHelper.LogError(e, "RefreshWeighbridgeRecords");
            }
        }

        public async void Load(FrameworkElement element)
        {
            ErrorFound = false;
            PendingErrorFound = false;
            DateFrom = DateTime.Today.AddDays(-3);
            DateTo = DateTime.Today.AddDays(1);
            await RefreshPending();
            await RefreshWeighbridgeRecords();
        }

        public void Unload(FrameworkElement element)
        {
            IsPendingBusy = false;
            IsBusy = false;
            PendingErrorFound = false;
            ErrorFound = false;
            WeighbridgeRecordList = null;
            PendingList = null;
            SelectedPending = null;
            SelectedWeighbridgeRecord = null;
        }
    }
}

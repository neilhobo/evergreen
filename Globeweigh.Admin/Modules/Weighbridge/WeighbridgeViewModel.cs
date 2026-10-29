using System.Collections.Generic;
using MvvmDialogs;
using System.Windows;
using GalaSoft.MvvmLight.CommandWpf;
using Globeweigh.Model;
using Globeweigh.Model.Custom;
using System;
using System.Linq;
using System.Threading.Tasks;
using DevExpress.XtraReports.UI;
using GalaSoft.MvvmLight.Ioc;
using Globeweigh.Shared;
using Globeweigh.Shared.Services;
using System.Net.Sockets;
using Globeweigh.Shared.Helpers;
using Globeweigh.Shared.Network;
using System.Net;
using System.Windows.Threading;
using System.Text;
using System.Threading;
using Globeweigh.Admin.Helpers;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using MessageBox = System.Windows.MessageBox;
using System.Windows.Media.Media3D;

namespace Globeweigh.Admin
{
    public class WeighbridgeViewModel : AdminViewModelBase, IViewModel
    {
        #region private fields

        private readonly IWeighbridgeRepository _weighbridgeRepo = SimpleIoc.Default.GetInstance<IWeighbridgeRepository>();
        private readonly IDialogService _dialogService = SimpleIoc.Default.GetInstance<IDialogService>();
        private readonly IScaleService _scaleService = SimpleIoc.Default.GetInstance<IScaleService>();
        private EventHandler<string> _weightHandler;
        private DispatcherTimer _timerRelay;

        #endregion

        #region Properties

        // public PictureBox RealPlayWnd { get; set; }

        // private PictureBox _RealPlayWnd;
        // public PictureBox RealPlayWnd
        // {
        //     get { return _RealPlayWnd; }
        //     set { Set(ref _RealPlayWnd, value); }
        // }

        private TcpClient _RelayTcpConnection;
        public TcpClient RelayTcpConnection
        {
            get { return _RelayTcpConnection; }
            set { Set(ref _RelayTcpConnection, value); }
        }

        private List<vwWeighbridgePending> _PendingList;
        public List<vwWeighbridgePending> PendingList
        {
            get { return _PendingList; }
            set { Set(ref _PendingList, value); }
        }

        private List<vwWeighbridgeOrderAssigned> _AssignedOrderList;
        public List<vwWeighbridgeOrderAssigned> AssignedOrderList
        {
            get { return _AssignedOrderList; }
            set { Set(ref _AssignedOrderList, value); }
        }

        private vwWeighbridgeOrderAssigned _SelectedAssignedOrder;
        public vwWeighbridgeOrderAssigned SelectedAssignedOrder
        {
            get { return _SelectedAssignedOrder; }
            set { Set(ref _SelectedAssignedOrder, value); }
        }

        private List<vwWeighbridgeComplete> _WeighbridgeRecordList;
        public List<vwWeighbridgeComplete> WeighbridgeRecordList
        {
            get { return _WeighbridgeRecordList; }
            set { Set(ref _WeighbridgeRecordList, value); }
        }

        private vwWeighbridgePending _SelectedPending;
        public vwWeighbridgePending SelectedPending
        {
            get { return _SelectedPending; }
            set
            {
                Set(ref _SelectedPending, value);
                if (SelectedPending == null)
                {
                    AssignedOrderList = null;
                    return;
                }
                RefreshAssignedOrders();
            }
        }

        private vwWeighbridgeComplete _SelectedWeighbridgeRecord;
        public vwWeighbridgeComplete SelectedWeighbridgeRecord
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

        private bool _GateOpen;
        public bool GateOpen
        {
            get { return _GateOpen; }
            set { Set(ref _GateOpen, value); }
        }

        private bool _BarrierOpen;
        public bool BarrierOpen
        {
            get { return _BarrierOpen; }
            set { Set(ref _BarrierOpen, value); }
        }

        private string _LiveWeight;
        public string LiveWeight
        {
            get { return _LiveWeight; }
            set { Set(ref _LiveWeight, value); }
        }

        private bool _CameraOn;
        public bool CameraOn
        {
            get { return _CameraOn; }
            set
            {
                Set(ref _CameraOn, value);
                // CameraOnSwitchChanged();
            }
        }

        #endregion

        #region Commands

        public RelayCommand WeighOutCommand => new RelayCommand(OnWeightOut);
        public RelayCommand AddIncomingCommand => new RelayCommand(OnAddIncoming);
        public RelayCommand AddOutgoingCommand => new RelayCommand(OnAddOutgoing);
        public RelayCommand DeletePendingCommand => new RelayCommand(OnDeletePending);
        public RelayCommand EditPendingCommand => new RelayCommand(OnEditPending);
        public RelayCommand PrintDocketCommand => new RelayCommand(OnPrintDocket);
        public RelayCommand RefreshCommand => new RelayCommand(async () => await RefreshWeighbridgeRecords());
        public RelayCommand EditCommand => new RelayCommand(OnEdit);
        public RelayCommand RefreshPendingCommand => new RelayCommand(async () => await RefreshPending(false));
        public RelayCommand AssignOrderCommand => new RelayCommand(OnAssignOrder);
        // public RelayCommand RemoveOrderCommand => new RelayCommand(OnRemoveOrder);
        public RelayCommand ViewCommand => new RelayCommand(OnViewCommand);
        public RelayCommand BarrierCommand => new RelayCommand(OnBarrier);
        public RelayCommand GateCommand => new RelayCommand(OnGate);
        // public RelayCommand CameraOnCommand => new RelayCommand(OnCameraOn);
        // public RelayCommand CameraOffCommand => new RelayCommand(OnCameraOff);

        #endregion

        private void OnGate()
        {
            int x;
            x = Convert.ToInt16(1);
            if (GateOpen)
            {
                SerBuf[0] = (byte)commands.DIG_INACTIVE;
            }
            else
            {
                SerBuf[0] = (byte)commands.DIG_ACTIVE;

            }
            SerBuf[1] = (byte)x;
            SerBuf[2] = (byte)0;
            transmit(3);
            receive(1);
        }

        private void OnBarrier()
        {
            int x;
            x = Convert.ToInt16(2);
            if (BarrierOpen)
            {
                SerBuf[0] = (byte)commands.DIG_INACTIVE;
            }
            else
            {
                SerBuf[0] = (byte)commands.DIG_ACTIVE;

            }
            SerBuf[1] = (byte)x;
            SerBuf[2] = (byte)0;
            transmit(3);
            receive(1);
        }

        private async void OnViewCommand()
        {
            if (SelectedAssignedOrder == null) return;
            var dialogVm = SimpleIoc.Default.GetInstanceWithoutCaching<FullDispatchOrderViewModel>();
            dialogVm.DispatchOrderId = SelectedAssignedOrder.OrderId;
            bool? success = _dialogService.ShowDialog<FullDispatchOrderView>(this, dialogVm);
            if (success == true)
            {
                //await RefreshDispatchOrders();
            }
        }

        private async void RefreshAssignedOrders()
        {
            AssignedOrderList = new List<vwWeighbridgeOrderAssigned>(await _weighbridgeRepo.GetAssignedOrders(SelectedPending.id));
            SelectedAssignedOrder = null;
        }

        // private async void OnRemoveOrder()
        // {
        //     if (SelectedAssignedOrder == null) return;
        //
        //     var dlgRes = MessageBox.Show("Remove Order from Pending record?", "", MessageBoxButton.OKCancel);
        //     if (dlgRes == MessageBoxResult.OK)
        //     {
        //         await _weighbridgeRepo.DeleteWeighbridgeRecordOrder(SelectedAssignedOrder.id);
        //         await RefreshPending(true);
        //     }
        //
        // }

        private async void OnAssignOrder()
        {
            if (SelectedPending == null) return;
            var dialogVm = SimpleIoc.Default.GetInstanceWithoutCaching<SelectSalesOrderViewModel>();
            dialogVm.SelectedPending = SelectedPending;
            bool? success = _dialogService.ShowDialog<SelectSalesOrderView>(this, dialogVm);
            if (success == true)
            {
                await RefreshPending(true);
            }
        }

        private async void OnAddOutgoing()
        {
            var dialogVm = SimpleIoc.Default.GetInstanceWithoutCaching<AddIncomingViewModel>();
            dialogVm.HeaderText = "Record Incoming Weigh";
            dialogVm.IsOutgoing = true;
            bool? success = _dialogService.ShowDialog<AddIncomingView>(this, dialogVm);
            if (success == true)
            {
                await RefreshPending(false);
            }
        }

        private async void OnEdit()
        {
            var dialogVm = SimpleIoc.Default.GetInstanceWithoutCaching<EditWeighbrideRecordViewModel>();
            dialogVm.HeaderText = "Edit Record";
            dialogVm.WeighbridgeRecordId = SelectedWeighbridgeRecord.id;
            bool? success = _dialogService.ShowDialog<EditWeighbrideRecordView>(this, dialogVm);
            if (success == true)
            {
                await RefreshWeighbridgeRecords();
            }
        }

        private void OnPrintDocket()
        {
            // var report = new rptWeighbridgeDocket1(SelectedWeighbridgeRecord);
            // report.ShowPreviewDialog();
        }

        private async void OnWeightOut()
        {
            var dialogVm = SimpleIoc.Default.GetInstanceWithoutCaching<AddIncomingViewModel>();
            dialogVm.HeaderText = "Record Weigh";
            dialogVm.WeighbridgeRecordId = SelectedPending.id;
            dialogVm.IsOutgoing = !SelectedPending.IsOutgoing;
            bool? success = _dialogService.ShowDialog<AddIncomingView>(this, dialogVm);
            if (success == true)
            {
                await RefreshPending(false);
                await RefreshWeighbridgeRecords();
            }
        }

        private async void OnAddIncoming()
        {
            var dialogVm = SimpleIoc.Default.GetInstanceWithoutCaching<AddIncomingViewModel>();
            dialogVm.HeaderText = "Record Incoming Weigh";
            bool? success = _dialogService.ShowDialog<AddIncomingView>(this, dialogVm);
            if (success == true)
            {
                await RefreshPending(false);
            }
        }

        private async void OnDeletePending()
        {
            var result = _dialogService.ShowMessageBox(this, "Delete Pending Item?", "", MessageBoxButton.OKCancel);
            if (result == MessageBoxResult.Cancel) return;
            await _weighbridgeRepo.MarkAsDeletedAsync(SelectedPending.id);
            await RefreshPending(false);
            SelectedPending = null;
        }

        private async void OnEditPending()
        {
            var dialogVm = SimpleIoc.Default.GetInstanceWithoutCaching<EditWeighbrideRecordViewModel>();
            dialogVm.HeaderText = "Edit Incoming Weigh";
            dialogVm.WeighbridgeRecordId = SelectedPending.id;
            bool? success = _dialogService.ShowDialog<EditWeighbrideRecordView>(this, dialogVm);
            if (success == true)
            {
                await RefreshPending(true);
            }
        }

        private async Task RefreshPending(bool keepSelection)
        {
            int selectedPendingId = 0;
            if (keepSelection) { selectedPendingId = SelectedPending.id; }
            PendingList = new List<vwWeighbridgePending>(await _weighbridgeRepo.GetPendingWeighbridgeRecords());
            if (keepSelection) { SelectedPending = PendingList.FirstOrDefault(a => a.id == selectedPendingId); }
            else { SelectedPending = null; }

        }

        private async Task RefreshWeighbridgeRecords()
        {
            WeighbridgeRecordList = new List<vwWeighbridgeComplete>(await _weighbridgeRepo.GetWeighbridgeCompleteRecords(DateFrom.Date, DateTo.Date));
            SelectedWeighbridgeRecord = null;
        }

        private void OnWeightChanged(object sender, string weight)
        {
            LiveWeight = weight;
        }

        public async void Load(FrameworkElement element)
        {
            var vm = (WeighbridgeView)element;
            // RealPlayWnd = vm.RealPlayWnd;
            DateFrom = DateTime.Today.AddMonths(-1);
            DateTo = DateTime.Today.AddDays(1);
            await RefreshPending(false);
            await RefreshWeighbridgeRecords();

            _weightHandler = OnWeightChanged;
            _scaleService.WeightChanged += _weightHandler;
            await _scaleService.EnsureConnectedAsync();
            LiveWeight = _scaleService.CurrentWeight;

            if (UtilitiesShared.IsMyMachine) return;
            await SetupRelay();
        }

        public void Unload(FrameworkElement element)
        {
            IsUnloading = true;

            if (_weightHandler != null)
            {
                _scaleService.WeightChanged -= _weightHandler;
                _weightHandler = null;
            }

            if (_timerRelay != null)
            {
                _timerRelay.Stop();
                _timerRelay = null;
            }

            if (RelayTcpConnection != null)
            {
                RelayTcpConnection.Close();
                RelayTcpConnection = null;
            }

            CameraOn = false;
            // OnCameraOff();
            // LogOffCamera();


            WeighbridgeRecordList = null;
            PendingList = null;
            SelectedPending = null;
            SelectedWeighbridgeRecord = null;
            IsUnloading = false;
        }

        private async Task SetupRelay()
        {
            if(!GlobalVariables.GlobalDbSettings.GateEnabled)return;
            RelayTcpConnection = new TcpClient();
            string ipAddress;

            if (UtilitiesShared.IsMyMachine)
            {
                // ipAddress = "192.168.1.150";
                ipAddress = GlobalVariables.GlobalDbSettings.GateRelayIpAddress;
            }
            else
            {
                ipAddress = GlobalVariables.GlobalDbSettings.GateRelayIpAddress;
            }

            IAsyncResult result = RelayTcpConnection.BeginConnect(ipAddress, GlobalVariables.GlobalDbSettings.GateRelayPortNo, null, null);

            byte connected = 0;

            bool successRelay = result.AsyncWaitHandle.WaitOne(2000, true);
            if (!successRelay)
            {
                connected = 0;
                RelayTcpConnection.Close();
                RelayTcpConnection = null;
                // MessageBox.Show("Failed to connect");
            }
            else
            {
                connected = 1;
            }

            if (connected == 1)
            {
                System.Threading.Thread.Sleep(500); //make sure port is open before comms
                ns = RelayTcpConnection.GetStream();
                ns.ReadTimeout = 5000;
                ns.WriteTimeout = 5000;
                SerBuf[0] = (byte)commands.GET_VER;
                transmit(1);
                receive(3);
                device_found = true;

                // ns = RelayTcpConnection.GetStream();
                // ns.ReadTimeout = 1000;
                // SerBuf[0] = (byte)commands.GET_VER;   
                // transmit(1);
                // receive(3);
                // device_found = true;


                _timerRelay = new DispatcherTimer();
                _timerRelay.Interval = new TimeSpan(0, 0, 0, 0, 100);
                _timerRelay.Tick += timerRelay_tick;
                _timerRelay.Start();
            }
        }

        private void timerRelay_tick(object sender, EventArgs e)
        {
            byte x;
            int temp;
            // if (UtilitiesShared.IsMyMachine)
            // {
            //     if (device_found == true)
            //     {
            //         SerBuf[0] = (byte)commands.GET_VOLTS; ;
            //         transmit(1);
            //         receive(1);
            //
            //         SerBuf[0] = (byte)commands.GET_UNLOCK_TIME;
            //         transmit(1);
            //         receive(1);
            //
            //         SerBuf[0] = (byte)commands.DIG_GET_OUTPUTS;      // get states command
            //         transmit(1);
            //         receive(1);
            //
            //         temp = SerBuf[2] << 16;
            //         temp += SerBuf[1] << 8;
            //         temp += SerBuf[0];
            //
            //         if ((temp & (0x01 << 0)) > 0)
            //         {
            //             GateOpen = true;
            //         }
            //         else
            //         {
            //             GateOpen = false;
            //         }
            //
            //         if ((temp & (0x01 << 1)) > 0)
            //         {
            //             BarrierOpen = true;
            //         }
            //         else
            //         {
            //             BarrierOpen = false;
            //         }
            //     }
            // }
            // else
            {
                if (device_found == true)
                {
                    SerBuf[0] = (byte)commands.DIG_GET_INPUTS;
                    transmit(1);
                    receive(2);

                    SerBuf[0] = (byte)commands.GET_VOLTS;
                    transmit(1);
                    receive(1);

                    SerBuf[0] = (byte)commands.GET_UNLOCK_TIME;
                    transmit(1);
                    receive(1);

                    SerBuf[0] = (byte)commands.DIG_GET_OUTPUTS;      // get states command
                    transmit(1);
                    receive(2);

                    temp = SerBuf[2] << 16; //puts all the states into 32 bit int temp in long string of bits
                    temp += SerBuf[1] << 8;
                    temp += SerBuf[0];
                    if ((temp & (0x01 << 0)) > 0) { GateOpen = true; }
                    else { GateOpen = false; }

                    if ((temp & (0x01 << 1)) > 0) { BarrierOpen = true; }
                    else { BarrierOpen = false; }
                }
            }

        }

        #region Relay Commands

        bool device_found = false;
        NetworkStream ns;
        byte[] SerBuf = new byte[70];

        enum commands
        {
            GET_VER = 0x10,
            DIG_ACTIVE = 0x20, DIG_INACTIVE, DIG_DIRECTION, DIG_SET_OUTPUTS, DIG_GET_OUTPUTS, DIG_GET_INPUTS,
            VAR_SET_OUTPUT = 0x30, VAR_GET_OUTPUT, VAR_GET_INPUT,

            GET_SER_NUM = 0x77, GET_VOLTS, PASSWORD_ENTRY, GET_UNLOCK_TIME, LOG_OUT,
        };

        private void transmit(byte write_bytes)
        {
            try
            {
                ns.Write(SerBuf, 0, write_bytes);
                ns.Flush();
            }
            catch
            {
                device_found = false;
                MessageBox.Show("write fail");
            }
        }

        private void receive(byte read_bytes)
        {
            byte x;

            for (x = 0; x < read_bytes; x++)       // this will call the read function for the passed number times, 
            {                                      // this way it ensures each byte has been correctly recieved while
                try                                // still using timeouts
                {
                    ns.Read(SerBuf, x, 1);     // retrieves 1 byte at a time and places in SerBuf at position x
                }
                catch (Exception)                   // timeout or other error occured, set lost comms indicator
                {
                    device_found = false;
                    MessageBox.Show("read fail");
                }
            }
        }

        #endregion
    }
}

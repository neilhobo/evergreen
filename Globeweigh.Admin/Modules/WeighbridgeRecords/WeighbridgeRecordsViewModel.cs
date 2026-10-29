using DevExpress.Xpf.Grid;
using GalaSoft.MvvmLight.CommandWpf;
using GalaSoft.MvvmLight.Ioc;
using Globeweigh.Admin.Helpers;
using Globeweigh.Model;
using Globeweigh.Shared;
using Globeweigh.Shared.Helpers;
using Globeweigh.Shared.Services;
using MvvmDialogs;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Threading;
using MessageBox = System.Windows.MessageBox;

namespace Globeweigh.Admin
{
    public class WeighbridgeRecordsViewModel : AdminViewModelBase, IViewModel
    {
        #region private fields

        private readonly IDispatchOrderRepository _dispatchRepo = SimpleIoc.Default.GetInstance<IDispatchOrderRepository>();
        private readonly IDialogService _dialogService = SimpleIoc.Default.GetInstance<IDialogService>();
        private readonly IWeighbridgeRepository _weighbridgeRepo = SimpleIoc.Default.GetInstance<IWeighbridgeRepository>();
        private readonly IScaleService _scaleService = SimpleIoc.Default.GetInstance<IScaleService>();
        private EventHandler<string> _weightHandler;
        private DispatcherTimer _timerRelay;

        #endregion

        #region Properties

        private TcpClient _RelayTcpConnection;
        public TcpClient RelayTcpConnection
        {
            get { return _RelayTcpConnection; }
            set { Set(ref _RelayTcpConnection, value); }
        }

        private List<vwWeighbridgeComplete> _CompletedWeighbridgeRecordList;
        public List<vwWeighbridgeComplete> CompletedWeighbridgeRecordList
        {
            get { return _CompletedWeighbridgeRecordList; }
            set { Set(ref _CompletedWeighbridgeRecordList, value); }
        }

        private vwWeighbridgeComplete _SelectedCompletedWeighbridgeRecord;
        public vwWeighbridgeComplete SelectedCompletedWeighbridgeRecord
        {
            get { return _SelectedCompletedWeighbridgeRecord; }
            set
            {
                Set(ref _SelectedCompletedWeighbridgeRecord, value);
            }
        }

        private List<vwWeighbridgePending> _PendingList;
        public List<vwWeighbridgePending> PendingList
        {
            get { return _PendingList; }
            set { Set(ref _PendingList, value); }
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

        private vwWeighbridgePending _SelectedPending;
        public vwWeighbridgePending SelectedPending
        {
            get { return _SelectedPending; }
            set
            {
                Set(ref _SelectedPending, value);
                if (SelectedPending == null) return;
            }
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

        #endregion

        #region Commands

        public RelayCommand AddManualCommand => new RelayCommand(OnAddManual);
        public RelayCommand EditCommand => new RelayCommand(OnEdit);
        public RelayCommand DeletePendingCommand => new RelayCommand(OnDeletePending);
        public RelayCommand DeleteCommand => new RelayCommand(OnDelete);
        public RelayCommand EditPendingCommand => new RelayCommand(OnEditPending);
        public RelayCommand RefreshPendingCommand => new RelayCommand(async () => await RefreshPending());
        public RelayCommand RefreshCompletedCommand => new RelayCommand(OnRefreshCompleted);
        public RelayCommand BarrierCommand => new RelayCommand(OnBarrier);
        public RelayCommand VncTouchInCommand => new RelayCommand(OnVncTouchIn);
        public RelayCommand VncTouchOutCommand => new RelayCommand(OnVncTouchOut);

        public RelayCommand<GridControl> ExportCompletedOrdersQuickExcelCommand =>
            new RelayCommand<GridControl>(OnExportCompletedOrdersQuickExcel);

        #endregion

        // In OnExportCompletedOrdersQuickExcel
        private void OnExportCompletedOrdersQuickExcel(GridControl tableView)
        {
            SaveFileDialog dlg = new SaveFileDialog
            {
                Filter = "Excel (*.xlsx)|*.xlsx",
                DefaultExt = "xlsx",
                AddExtension = true,
                InitialDirectory = "C:"
            };
            if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                return;

            // Assuming CompletedWeighbridgeRecordList is your ItemsSource (ObservableCollection or List)
            // var filtered = tableView.DataControl.FilteredList.Cast<vwWeighbridgeComplete>().ToList();
            // or if not available, use:
            // var filtered = ((ICollectionView)tableView.DataControl.ItemsSource).Cast<vwWeighbridgeComplete>().ToList();

            Utilities.ExportCompletedOrdersQuickExcel(tableView, dlg.FileName);
        }


        private void OnVncTouchIn()
        {
            // LaunchRealVnc("192.168.250.177");
            LaunchRealVnc("TOUCH-IN");
        }

        private void OnVncTouchOut()
        {
            // LaunchRealVnc("192.168.250.178");
            LaunchRealVnc("TOUCH-OUT");
        }

        private void LaunchRealVnc(string hostWithOptionalPort)
        {
            var viewer = FindRealVncViewer();
            if (viewer == null)
            {
                MessageBox.Show("RealVNC Viewer not found. Please install it on this PC.");
                return;
            }

            try
            {
                // Minimal args: just the host (and :port if non-default).
                // RealVNC will prompt for credentials unless you use a .vnc file (see note below).
                Process.Start(new ProcessStartInfo
                {
                    FileName = viewer,
                    Arguments = hostWithOptionalPort,
                    UseShellExecute = false
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show("Could not start RealVNC Viewer.\n" + ex.Message);
            }
        }

        private string FindRealVncViewer()
        {
            // Common install paths; adjust if your environment differs
            var candidates = new[]
            {
                @"C:\Program Files\RealVNC\VNC Viewer\vncviewer.exe",
                // @"C:\Program Files (x86)\RealVNC\VNC Viewer\vncviewer.exe"
            };

            foreach (var p in candidates)
                if (File.Exists(p)) return p;

            return null;
        }

        private async void OnAddManual()
        {
            var dialogVm = SimpleIoc.Default.GetInstanceWithoutCaching<AddManualWeighbridgeViewModel>();
            dialogVm.HeaderText = "Add Manual Record";
            bool? success = _dialogService.ShowDialog<AddManualWeighbridgeView>(this, dialogVm);
            if (success == true)
            {
                await RefreshPending();
                await RefreshCompleted();
            }
        }

        private async void OnEdit()
        {
            var dialogVm = SimpleIoc.Default.GetInstanceWithoutCaching<AddManualWeighbridgeViewModel>();
            dialogVm.HeaderText = "Edit Record";
            dialogVm.IsEditMode = true;
            dialogVm.WeighbridgeRecordId = SelectedCompletedWeighbridgeRecord.id;
            bool? success = _dialogService.ShowDialog<AddManualWeighbridgeView>(this, dialogVm);
            if (success == true)
            {
                await RefreshPending();
                await RefreshCompleted();
            }
        }

        private async void OnEditPending()
        {
            var dialogVm = SimpleIoc.Default.GetInstanceWithoutCaching<EditWeighbrideRecordViewModel>();
            dialogVm.HeaderText = "Edit Incoming Weigh";
            dialogVm.WeighbridgeRecordId = SelectedPending.id;
            bool? success = _dialogService.ShowDialog<EditWeighbrideRecordView>(this, dialogVm);
            if (success == true)
            {
                await RefreshPending();
                await RefreshCompleted();
            }
        }

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

        private async void OnRefreshCompleted()
        {
            await RefreshCompleted();
        }

        private async void OnDeletePending()
        {
            var result = _dialogService.ShowMessageBox(this, "Delete Pending Item?", "", MessageBoxButton.OKCancel);
            if (result == MessageBoxResult.Cancel) return;
            await _weighbridgeRepo.MarkAsDeletedAsync(SelectedPending.id);
            await RefreshPending();
            SelectedPending = null;
        }

        private async void OnDelete()
        {
            var result = _dialogService.ShowMessageBox(this, "Delete Record?", "", MessageBoxButton.OKCancel);
            if (result == MessageBoxResult.Cancel) return;
            await _weighbridgeRepo.MarkAsDeletedAsync(SelectedCompletedWeighbridgeRecord.id);
            await RefreshCompleted();
            SelectedPending = null;
        }

        private async Task RefreshCompleted()
        {
            CompletedWeighbridgeRecordList = new List<vwWeighbridgeComplete>(await _weighbridgeRepo.GetCompletedWeighbridgeRecordViews(DateFrom, DateTo.AddDays(1)));
            SelectedCompletedWeighbridgeRecord = null;
        }

        private async Task RefreshPending()
        {
            PendingList = new List<vwWeighbridgePending>(await _weighbridgeRepo.GetPendingWeighbridgeRecords());
            SelectedPending = null;
        }

        private void OnWeightChanged(object sender, string weight)
        {
            LiveWeight = weight;
        }

        private async Task SetupRelay()
        {
            if (!GlobalVariables.GlobalDbSettings.GateEnabled) return;
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

        public async void Load(FrameworkElement element)
        {
            DateFrom = DateTime.Today.AddMonths(-1);
            DateTo = DateTime.Today.AddDays(1);
            await RefreshPending();
            await RefreshCompleted();

            _weightHandler = OnWeightChanged;
            _scaleService.WeightChanged += _weightHandler;
            await _scaleService.EnsureConnectedAsync();
            LiveWeight = _scaleService.CurrentWeight;

            await SetupRelay();
        }

        public void Unload(FrameworkElement element)
        {
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


            CompletedWeighbridgeRecordList = null;
            PendingList = null;
            SelectedPending = null;
            SelectedCompletedWeighbridgeRecord = null;
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

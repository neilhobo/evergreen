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

namespace Globeweigh.Admin
{
    public class AddIncomingViewModel : DialogViewModelBase, IModalDialogViewModel, IViewModel
    {
        #region private fields

        private readonly IWeighbridgeRepository _weighbridgeRepo = SimpleIoc.Default.GetInstance<IWeighbridgeRepository>();
        private readonly IReferenceDataRepository _refDataRepo = SimpleIoc.Default.GetInstance<IReferenceDataRepository>();
        private readonly IHaulierRepository _haulierRepo = SimpleIoc.Default.GetInstance<IHaulierRepository>();
        private readonly IDialogService _dialogService = SimpleIoc.Default.GetInstance<IDialogService>();
        private readonly IScaleService _scaleService = SimpleIoc.Default.GetInstance<IScaleService>();
        private EventHandler<string> _weightHandler;

        private DispatcherTimer _timerRelay;
        #endregion

        #region Properties

        public int WeighbridgeRecordId { get; set; }

        private TcpClient _RelayTcpConnection;
        public TcpClient RelayTcpConnection
        {
            get { return _RelayTcpConnection; }
            set { Set(ref _RelayTcpConnection, value); }
        }

        private string _ManualWeight;
        public string ManualWeight
        {
            get { return _ManualWeight; }
            set { Set(ref _ManualWeight, value); }
        }

        private bool _IsManual;
        public bool IsManual
        {
            get { return _IsManual; }
            set { Set(ref _IsManual, value); }
        }


        private bool _IsOutgoing;
        public bool IsOutgoing
        {
            get { return _IsOutgoing; }
            set { Set(ref _IsOutgoing, value); }
        }

        private bool _InEditMode;
        public bool InEditMode
        {
            get { return _InEditMode; }
            set { Set(ref _InEditMode, value); }
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
            }
        }

        //
        //
        // private List<ReferenceData> _VehicleList;
        // public List<ReferenceData> VehicleList
        // {
        //     get { return _VehicleList; }
        //     private set { Set(ref _VehicleList, value); }
        // }
        //
        //
        //
        // private ReferenceData _SelectedVehicle;
        // public ReferenceData SelectedVehicle
        // {
        //     get { return _SelectedVehicle; }
        //     set { Set(ref _SelectedVehicle, value); }
        // }
        private string _LiveWeight;
        public string LiveWeight
        {
            get { return _LiveWeight; }
            set { Set(ref _LiveWeight, value); }
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

        #endregion

        #region Commands

        public RelayCommand EditSaveCommand => new RelayCommand(OnEditSave);
        public RelayCommand SaveCommand => new RelayCommand(OnSave, CanSave);
        public RelayCommand BarrierCommand => new RelayCommand(OnBarrier);
        public RelayCommand GateCommand => new RelayCommand(OnGate);

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

        private async void OnEditSave()
        {
            // CurrentWeighbridgeRecord.HaulierName = SelectedHaulier.DisplayName;
            // CurrentWeighbridgeRecord.VehicleReg = SelectedVehicle.DisplayName;
            await _weighbridgeRepo.AddUpdateAsync(CurrentWeighbridgeRecord);
            DialogResult = true;
        }

        private async void OnSave()
        {
            // CurrentWeighbridgeRecord.HaulierName = SelectedHaulier.Name;
            // CurrentWeighbridgeRecord.VehicleReg = SelectedVehicle.DisplayName;

            if (!InEditMode)
            {
                int weight = 0;
                if (IsManual) { weight = Convert.ToInt32(ManualWeight); }
                else { weight = Convert.ToInt32(LiveWeight); }

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
            }

            if (CurrentWeighbridgeRecord.IsOutgoing && CurrentWeighbridgeRecord.DateWeightIn!=null && CurrentWeighbridgeRecord.DateWeightOut !=null)
            {
                await _weighbridgeRepo.CompleteWeighBridgeRecord(CurrentWeighbridgeRecord);
            }
            else
            {

                await _weighbridgeRepo.AddUpdateAsync(CurrentWeighbridgeRecord);
            }

            DialogResult = true;
        }

        private bool CanSave()
        {
            if (CurrentWeighbridgeRecord == null) return false;
            if (CurrentWeighbridgeRecord.HaulierId == 0) return false;
            if (SelectedHaulier == null) return false;
            // if (InEditMode) return false;
            if (IsManual)
            {
                if (string.IsNullOrEmpty(ManualWeight)) return false;
            }
            else
            {
                if (string.IsNullOrEmpty(LiveWeight)) return false;
            }
            return true;
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

        private void OnWeightChanged(object sender, string weight)
        {
            LiveWeight = weight;
        }

        private async Task SetupRelay()
        {
            RelayTcpConnection = new TcpClient();
            string ipAddress;

            if (UtilitiesShared.IsMyMachine)
            {
                // ipAddress = "192.168.1.150";
                ipAddress = "192.168.250.251";
            }
            else
            {
                ipAddress = "192.168.250.251";
            }

            IAsyncResult result = RelayTcpConnection.BeginConnect(ipAddress, 17494, null, null);

            byte connected = 0;

            bool successRelay = result.AsyncWaitHandle.WaitOne(2000, true);
            if (!successRelay)
            {
                connected = 0;
                RelayTcpConnection.Close();
                RelayTcpConnection = null;
                MessageBox.Show("Failed to connect");
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
                _timerRelay.Interval = new TimeSpan(0, 0, 0,0,100);
                _timerRelay.Tick += timerRelay_tick;
                _timerRelay.Start();
            }
        }

        public async void Load(FrameworkElement element)
        {
            if (UtilitiesShared.InDesignMode) return;
            if (IsOutgoing) HeaderText = "Add Outgoing Weigh Record";
            else { HeaderText = "Add Incoming Weigh Record"; }
            HaulierList = new List<Haulier>(await _haulierRepo.GetHaulierList());

            if (WeighbridgeRecordId == 0)
            {
                CurrentWeighbridgeRecord = new WeighbridgeRecord();
                if (IsOutgoing) { CurrentWeighbridgeRecord.DateWeightIn = DateTime.Now; }
                else { CurrentWeighbridgeRecord.DateWeightOut = DateTime.Now; }
                CurrentWeighbridgeRecord.IsOutgoing = IsOutgoing;
            }
            else
            {
                CurrentWeighbridgeRecord = await _weighbridgeRepo.GetWeighbridgeRecord(WeighbridgeRecordId,  true, false);
                if (!InEditMode)
                {
                    if (IsOutgoing) { CurrentWeighbridgeRecord.DateWeightOut = DateTime.Now; }
                    else { CurrentWeighbridgeRecord.DateWeightIn = DateTime.Now; }
                }
            }

            CurrentWeighbridgeRecord.RaisePropertiesChangedManual();
            // await SetupRelay();

            _weightHandler = OnWeightChanged;
            _scaleService.WeightChanged += _weightHandler;
            await _scaleService.EnsureConnectedAsync();
            LiveWeight = _scaleService.CurrentWeight;
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


            CurrentWeighbridgeRecord = null;
            WeighbridgeRecordId = 0;
            DialogResult = null;

            SelectedHaulier = null;
            IsOutgoing = false;
            InEditMode = false;
            IsManual = false;
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

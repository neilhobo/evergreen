using DevExpress.XtraReports.UI;
using GalaSoft.MvvmLight.CommandWpf;
using GalaSoft.MvvmLight.Ioc;
using Globeweigh.Model;
using Globeweigh.Reports.Dispatch;
using Globeweigh.Shared;
using Globeweigh.Shared.Helpers;
using Globeweigh.Shared.Network;
using Globeweigh.Shared.Services;
using LibVLCSharp.Shared;
using MvvmDialogs;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace Globeweigh.Admin
{
    public class GateBarrierViewModel : AdminViewModelBase, IViewModel
    {
        #region private fields

        private readonly IDialogService _dialogService = SimpleIoc.Default.GetInstance<IDialogService>();
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
            _cameraCts = new CancellationTokenSource();

            if (_cameraInitialized && CameraPlayer != null && _libVlc != null)
            {
                // Already set up from previous visit — just (re)start on the UI thread
                element.Dispatcher.BeginInvoke(new Action(CameraStart), DispatcherPriority.Loaded);
            }
            else
            {
                // First time (or after a failure): do the async init
                var _ = SetupCameraAsync(element, _cameraCts.Token);
            }

            _weightHandler = OnWeightChanged;
            _scaleService.WeightChanged += _weightHandler;
            await _scaleService.EnsureConnectedAsync();
            LiveWeight = _scaleService.CurrentWeight;

            await SetupRelay();
        }

        public void Unload(FrameworkElement element)
        {
            // Cancel any pending init
            try { _cameraCts?.Cancel(); } catch { }
            _cameraCts = null;

            // Stop the video but KEEP LibVLC/MediaPlayer
            CameraStop();

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
        }

        #region === CAMERA (LibVLC) ===

        private Media _currentMedia;

        private LibVLC _libVlc;
        private string _rtspUrl;

        private MediaPlayer _cameraPlayer;
        public MediaPlayer CameraPlayer
        {
            get => _cameraPlayer;
            private set => Set(ref _cameraPlayer, value);
        }

        private bool _isCameraBusy;
        public bool IsCameraBusy
        {
            get { return _isCameraBusy; }
            set { Set(ref _isCameraBusy, value); }
        }

        public enum CameraChoice { Main, Gate, Production }

        private CameraChoice _selectedCamera = CameraChoice.Main;
        public CameraChoice SelectedCamera
        {
            get => _selectedCamera;
            set
            {
                if (_selectedCamera == value) return;
                _selectedCamera = value;

                RaisePropertyChanged<CameraChoice>(() => SelectedCamera);  // <- explicit type arg

                // Only auto-switch after we’ve finished first-time initialization.
                if (_cameraInitialized)
                    _ = SwitchCameraAsync(value);

            }
        }
        
        private CancellationTokenSource _cameraCts;
        private bool _cameraInitialized;


        public RelayCommand<object> CameraStartCommand => new RelayCommand<object>(_ => CameraStart(), _ => CameraPlayer != null);
        public RelayCommand<object> CameraStopCommand => new RelayCommand<object>(_ => CameraStop(), _ => CameraPlayer != null);

        private readonly string _camUser = "admin";
        private readonly string _camPass = "mac111111";
        private readonly int _channel = 1; // keep your channel choice

        private async Task SwitchCameraAsync(CameraChoice choice)
        {
            if (CameraPlayer == null || _libVlc == null) return;

            IsCameraBusy = true;
            try
            {
                // Stop current stream on UI thread
                await Application.Current.Dispatcher.InvokeAsync(new Action(CameraStop));

                // Small settle delay helps VLC fully tear down before new media
                await Task.Delay(120);

                string ip;

                switch (choice)
                {
                    case CameraChoice.Main:
                        ip = "192.168.250.158";
                        break;

                    case CameraChoice.Gate:
                        ip = "192.168.250.162";
                        break;

                    case CameraChoice.Production:
                        ip = "192.168.250.191";
                        break;

                    default:
                        ip = "192.168.250.158";
                        break;
                }

                // Build RTSP & create media
                _rtspUrl = BuildRtsp(ip, _camUser, _camPass, _channel);

                // Create and keep media alive
                if (_currentMedia != null) { try { _currentMedia.Dispose(); } catch { } _currentMedia = null; }
                _currentMedia = new Media(_libVlc, new Uri(_rtspUrl));
                _currentMedia.AddOption(":rtsp-tcp");
                _currentMedia.AddOption(":network-caching=200");

                // Play on UI thread (queue to a safe priority)
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    CameraPlayer.Play(_currentMedia);
                }, DispatcherPriority.Render);
            }
            catch (Exception ex)
            {
                ErrorLogging.LogError(ex, "SwitchCameraAsync");
            }
            finally
            {
                IsCameraBusy = false;
            }
        }



        private async Task SetupCameraAsync(FrameworkElement host, CancellationToken token)
        {
            if (_cameraInitialized) return;

            IsCameraBusy = true;
            try
            {
                // Heavy work (native discovery / LibVLC creation) off the UI thread
                await Task.Run(() =>
                {
                    token.ThrowIfCancellationRequested();

                    var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                    var vlcBase = System.IO.Path.Combine(baseDir, "libvlc", "win-x64");

                    LibVLCSharp.Shared.Core.Initialize(vlcBase);
                    _libVlc = new LibVLC($"--plugin-path={System.IO.Path.Combine(vlcBase, "plugins")}");
                }, token).ConfigureAwait(false);

                // Back on the UI thread: create MediaPlayer, set up URL, and schedule Start
                await host.Dispatcher.InvokeAsync(new Action(() =>
                {
                    CameraPlayer = new MediaPlayer(_libVlc);
                    CameraPlayer.Scale = 0f;
                    CameraPlayer.AspectRatio = null;
                    SelectedCamera = CameraChoice.Main;
                    host.Dispatcher.BeginInvoke(new Action(() => _ = SwitchCameraAsync(SelectedCamera)),
                        DispatcherPriority.Loaded);
                    _cameraInitialized = true;


                }));

            }
            catch (OperationCanceledException) { /* ignore */ }
            catch (Exception ex)
            {
                ErrorLogging.LogError(ex, "SetupCameraAsync");
            }
            finally
            {
                IsCameraBusy = false;
            }
        }



        private static string BuildRtsp(string host, string user, string pass, int channel)
        {
            return $"rtsp://{Uri.EscapeDataString(user)}:{Uri.EscapeDataString(pass)}@{host}/Streaming/Channels/{channel}";
        }


        private void CameraStart()
        {
            if (CameraPlayer == null || string.IsNullOrWhiteSpace(_rtspUrl)) return;

            try
            {
                // Dispose any previous media
                if (_currentMedia != null)
                {
                    try { _currentMedia.Dispose(); } catch { }
                    _currentMedia = null;
                }

                _currentMedia = new Media(_libVlc, new Uri(_rtspUrl));
                _currentMedia.AddOption(":rtsp-tcp");
                _currentMedia.AddOption(":network-caching=200");

                CameraPlayer.Play(_currentMedia);
            }
            catch (Exception ex)
            {
                ErrorLogging.LogError(ex, "CameraStart");
            }
        }

        // In CameraStop() and DisposeCamera(), also dispose the media:
        private void CameraStop()
        {
            try { CameraPlayer?.Stop(); } catch { }
            try { if (_currentMedia != null) _currentMedia.Dispose(); } catch { }
            _currentMedia = null;
        }

        private void DisposeCamera()
        {
            try { CameraPlayer?.Stop(); } catch { }
            try { if (_currentMedia != null) _currentMedia.Dispose(); } catch { }
            _currentMedia = null;

            try { CameraPlayer?.Dispose(); } catch { }
            CameraPlayer = null;

            try { _libVlc?.Dispose(); } catch { }
            _libVlc = null;
        }

        #endregion

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

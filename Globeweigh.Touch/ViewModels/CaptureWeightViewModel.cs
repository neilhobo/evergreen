using Globeweigh.Shared;
using MvvmDialogs;
using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Windows;
using System.Windows.Threading;
using GalaSoft.MvvmLight.CommandWpf;
using GalaSoft.MvvmLight.Ioc;
using GalaSoft.MvvmLight.Threading;
using Globeweigh.Model.Custom;
using Globeweigh.Shared.Helpers;

namespace Globeweigh.Touch
{
    public class CaptureWeightViewModel : DialogViewModelBase, IModalDialogViewModel, IViewModel
    {
        #region private fields

        private readonly IDialogService _dialogService = SimpleIoc.Default.GetInstance<IDialogService>();
        private SerialPort _serialCom1;
        private DispatcherTimer _timer;

        #endregion

        #region Properties

        private string _LiveWeight;
        public string LiveWeight
        {
            get { return _LiveWeight; }
            set { Set(ref _LiveWeight, value); }
        }

        private string _ExpectedWeightDisplay;
        public string ExpectedWeightDisplay
        {
            get { return _ExpectedWeightDisplay; }
            set { Set(ref _ExpectedWeightDisplay, value); }
        }

        private double _ReturnedWeight;
        public double ReturnedWeight
        {
            get { return _ReturnedWeight; }
            set { Set(ref _ReturnedWeight, value); }
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

        #endregion

        #region Commands

        public RelayCommand SaveCommand => new RelayCommand(OnSave);

        #endregion

        private async void _serialCom1_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            try
            {
                int bytesToRead = _serialCom1.BytesToRead;
                if (bytesToRead == 0)
                {
                    LiveWeight = "Error reading weight";
                    return;
                }
                string strWeight = _serialCom1.ReadLine();
                if (strWeight.Length < 13) return;

                strWeight = strWeight.Substring(4, strWeight.Length - 4).Trim();
                if (!strWeight.Contains("kg"))
                {
                    LiveWeight = "Error reading weight";
                    return;
                }

                strWeight = strWeight.Replace("kg", "").Trim();
                LiveWeight = strWeight.Replace(",", ".").Trim();
//                DispatcherHelper.CheckBeginInvokeOnUI(() => SaveCommand.RaiseCanExecuteChanged());
                SaveCommand.RaiseCanExecuteChanged();
            }
            catch (Exception ex)
            {

            }
        }

        private void timer_tick(object sender, EventArgs e)
        {
            try
            {
                if (!_serialCom1.IsOpen)
                    _serialCom1.Open();
                _serialCom1.WriteLine("q%\r");
            }
            catch (Exception ex)
            {
                LiveWeight = string.Empty;
            }
        }

        public async void MouseUpCommand(string name)
        {
            if (name == "ManualWeight")
            {
                var dialogViewModel = SimpleIoc.Default.GetInstance<KeypadViewModel>();
                dialogViewModel.HeaderText = "Enter Weight (Kg)";
                bool? success = _dialogService.ShowDialog<KeypadView>(this, dialogViewModel);
                if (success == true)
                {
                    ManualWeight = dialogViewModel.KeypadValue;
                }
            }
        }
        
        private async void OnSave()
        {
            if (IsManual)
            {
                if (string.IsNullOrEmpty(ManualWeight)) return;
                ReturnedWeight = Convert.ToDouble(ManualWeight);
            }
            else
            {
                if (string.IsNullOrEmpty(LiveWeight)) return;
                ReturnedWeight = Convert.ToDouble(LiveWeight);
            }
            DialogResult = true;
        }

        private bool CanSave()
        {
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

        public async void Load(FrameworkElement element)
        {
            if (UtilitiesShared.IsMyMachine)
            {
                LiveWeight = "1020.2";
                
                string strWeight = "+!     1025kg";
                if (strWeight.Length < 13) return;

                strWeight = strWeight.Substring(4, strWeight.Length - 4).Trim();
                if (!strWeight.Contains("kg"))
                {
                    LiveWeight = "Error reading weight";
                    return;
                }

                strWeight = strWeight.Replace("kg", "").Trim();
                LiveWeight = strWeight.Replace(",", ".").Trim();

                ReturnedWeight = Convert.ToDouble(LiveWeight);
                return;
            }

            _serialCom1 = new SerialPort("COM1", 9600, Parity.None);
            _serialCom1.DataReceived += _serialCom1_DataReceived;

            _timer = new DispatcherTimer();
            _timer.Interval = new TimeSpan(0, 0, 0, 1);
            _timer.Tick += timer_tick;
            _timer.Start();

            SaveCommand.RaiseCanExecuteChanged();
        }

        public void Unload(FrameworkElement element)
        {
            if (_timer != null)
            {
                _timer.Tick -= timer_tick;
                _timer.Stop();
                _timer = null;
            }

            if (_serialCom1 != null)
            {
                _serialCom1.DataReceived -= _serialCom1_DataReceived;
                _serialCom1.Close();
                _serialCom1.Dispose();
            }

            LiveWeight = null;
            DialogResult = null;
            ReturnedWeight = 0;
            IsManual = false;
            ManualWeight = null;
            ExpectedWeightDisplay = null;
        }
    }
}

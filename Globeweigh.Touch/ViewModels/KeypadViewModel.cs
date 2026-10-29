using System;
using System.Globalization;
using Globeweigh.Shared;
using Globeweigh.Model;
using MvvmDialogs;
using System.Windows;
using GalaSoft.MvvmLight.CommandWpf;

namespace Globeweigh.Touch
{
    public class KeypadViewModel : BindableBase, IModalDialogViewModel, IViewModel
    {
        #region Properties

        public bool IsPassword { get; set; }

        // NEW: enforce password behavior
        public bool EnforcePassword { get; set; }
        public string ExpectedPassword { get; set; }

        private bool _HeaderIsError;
        public bool HeaderIsError
        {
            get { return _HeaderIsError; }
            private set { Set(ref _HeaderIsError, value); }
        }

        private bool? _DialogResult;
        public bool? DialogResult
        {
            get { return _DialogResult; }
            private set { Set(ref _DialogResult, value); }
        }

        private string _KeypadValue;
        public string KeypadValue
        {
            get { return _KeypadValue; }
            private set
            {
                Set(ref _KeypadValue, value);
                if (IsPassword)
                {
                    int charCount = KeypadValue.Length;
                    KeypadDisplayValue = "";
                    for (var i = 0; i < charCount; i++) KeypadDisplayValue += "*";
                }
                else
                {
                    KeypadDisplayValue = KeypadValue;
                }
            }
        }

        private string _TimeValue;
        public string TimeValue
        {
            get { return _TimeValue; }
            private set { Set(ref _TimeValue, value); TimeDisplayValue = TimeValue; }
        }

        private string _KeypadDisplayValue;
        public string KeypadDisplayValue
        {
            get { return _KeypadDisplayValue; }
            set { Set(ref _KeypadDisplayValue, value); }
        }

        private string _TimeDisplayValue;
        public string TimeDisplayValue
        {
            get { return _TimeDisplayValue; }
            set { Set(ref _TimeDisplayValue, value); }
        }

        private string _HeaderText;
        public string HeaderText
        {
            get { return _HeaderText; }
            set { Set(ref _HeaderText, value); }
        }

        private bool _IsDecimalPointVisible;
        public bool IsDecimalPointVisible
        {
            get { return _IsDecimalPointVisible; }
            set { Set(ref _IsDecimalPointVisible, value); }
        }

        private bool _IsTimePicker;
        public bool IsTimePicker
        {
            get { return _IsTimePicker; }
            set { Set(ref _IsTimePicker, value); }
        }

        private bool _ShowMinus;
        public bool ShowMinus
        {
            get { return _ShowMinus; }
            set { Set(ref _ShowMinus, value); }
        }

        private int _MaxValue;
        public int MaxValue
        {
            get { return _MaxValue; }
            set { Set(ref _MaxValue, value); }
        }

        #endregion

        #region Commands

        public RelayCommand OkCommand { get; private set; }
        public RelayCommand CancelCommand { get; private set; }
        public RelayCommand<string> KeypadCommand { get; private set; }

        #endregion

        public KeypadViewModel()
        {
            OkCommand = new RelayCommand(OnSave, CanSave);
            CancelCommand = new RelayCommand(OnCancel);
            KeypadCommand = new RelayCommand<string>(OnKeypadCommand);
        }

        private bool CanSave()
        {
            if (IsTimePicker)
            {
                if (TimeValue == null || TimeValue.Length != 5) return false;
                DateTime dt;
                if (!DateTime.TryParseExact(TimeValue, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out dt))
                    return false;
            }
            else
            {
                if (string.IsNullOrEmpty(KeypadValue)) return false;
                if (MaxValue > 0)
                {
                    int v;
                    if (!int.TryParse(KeypadValue, out v)) return false;
                    if (v > MaxValue) return false;
                }
            }
            return true;
        }

        private void OnKeypadCommand(string param)
        {
            // clear error UI on new input
            if (HeaderIsError) HeaderIsError = false;

            if (IsTimePicker)
            {
                if (param == "Clear") { TimeValue = string.Empty; return; }
                if (param == "Del")
                {
                    if (TimeValue == null || TimeValue.Length == 0) return;
                    if (TimeValue.Length == 4) TimeValue = TimeValue.Substring(0, TimeValue.Length - 2);
                    else TimeValue = TimeValue.Substring(0, TimeValue.Length - 1);
                    return;
                }
                if (TimeValue != null && TimeValue.Length == 5) return;
                if (TimeValue != null && TimeValue.Length == 2) TimeValue += ":";
                TimeValue += param;
            }
            else
            {
                if (param == "Clear") { KeypadValue = string.Empty; return; }
                if (param == "Del")
                {
                    if (KeypadValue == null || KeypadValue.Length == 0) return;
                    KeypadValue = KeypadValue.Substring(0, KeypadValue.Length - 1);
                    return;
                }
                if (param == "-")
                {
                    if (!string.IsNullOrEmpty(KeypadValue)) return;
                }
                else if (param == ".")
                {
                    if (string.IsNullOrEmpty(KeypadValue)) return;
                    if (KeypadValue.IndexOf(".", StringComparison.Ordinal) >= 0) return;
                }
                KeypadValue += param;
            }
        }

        public void Load(FrameworkElement element)
        {
            DialogResult = null;
            HeaderIsError = false;
        }

        private void OnSave()
        {
            if (IsPassword && EnforcePassword)
            {
                // Null-safe compare; treat null as empty
                string entered = KeypadValue ?? string.Empty;
                string expected = ExpectedPassword ?? string.Empty;

                if (!string.Equals(entered, expected, StringComparison.Ordinal))
                {
                    HeaderText = "Incorrect password";
                    HeaderIsError = true;
                    // do NOT close the dialog
                    return;
                }
            }

            if (KeypadValue == string.Empty) KeypadValue = "0";
            HeaderIsError = false;
            DialogResult = true;
        }

        private void OnCancel()
        {
            DialogResult = false;
        }

        public void Unload(FrameworkElement element)
        {
            KeypadValue = string.Empty;
            DialogResult = null;
            MaxValue = 0;
            IsPassword = false;
            EnforcePassword = false;
            ExpectedPassword = null;
            IsDecimalPointVisible = false;
            HeaderIsError = false;
        }
    }
}

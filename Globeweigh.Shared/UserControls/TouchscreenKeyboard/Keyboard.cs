using System;
using System.Windows;
using WpfKb.Controls;

namespace Globeweigh.Shared.UI.TouchScreenKeyboard
{
    public class Keyboard : IServiceProvider, ITouchScreenKeyboard
    {
        private FloatingTouchScreenKeyboard _keyboard;

        #region IServiceProvider Members

        public object GetService(Type serviceType)
        {
            if (serviceType == typeof(ITouchScreenKeyboard))
                return this;
            return null;
        }

        #endregion

        #region ITouchScreenKeyboard Members

        public void Close()
        {
            if (_keyboard != null)
                _keyboard.IsOpen = false;
        }



        public void Position(UIElement ctrl, Point r)
        {
            if (_keyboard == null) return;
            _keyboard.PlacementTarget = ctrl;
            _keyboard.Placement = System.Windows.Controls.Primitives.PlacementMode.Absolute;
            _keyboard.HorizontalOffset = r.X;
            _keyboard.VerticalOffset = r.Y ;
        }

        private void CreateKeyboard()
        {
            _keyboard = new FloatingTouchScreenKeyboard()
            {
                PlacementTarget = Application.Current.MainWindow,
                Width = 880,
                Height = 320,
                Placement = System.Windows.Controls.Primitives.PlacementMode.Center,
                KeyboardHideDelay = 8d
            };

        }

        public void Show()
        {
            if (_keyboard == null)
            {
                CreateKeyboard();
            }
            _keyboard.IsOpen = true;
        }

        public bool IsShowing
        {
            get
            {
                if (_keyboard != null)
                {
                    return _keyboard.IsOpen;
                }
                else
                    return false;
            }
            set
            {
                if (_keyboard == null)
                    CreateKeyboard();
                _keyboard.IsOpen = value;
            }
        }

        #endregion
    }
}

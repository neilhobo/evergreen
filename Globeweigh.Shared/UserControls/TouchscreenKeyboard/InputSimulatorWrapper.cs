using System.Collections.Generic;

namespace Globeweigh.Shared.UI.TouchScreenKeyboard
{
    public class InputSimulatorWrapper : IInputSimulator
    {
        public bool IsKeyDown(WindowsInput.VirtualKeyCode keyCode)
        {
            return WindowsInput.InputSimulator.IsKeyDown(keyCode);
        }

        public bool IsKeyDownAsync(WindowsInput.VirtualKeyCode keyCode)
        {
            return WindowsInput.InputSimulator.IsKeyDownAsync(keyCode);
        }

        public bool IsTogglingKeyInEffect(WindowsInput.VirtualKeyCode keyCode)
        {
            return WindowsInput.InputSimulator.IsTogglingKeyInEffect(keyCode);
        }

        public void SimulateKeyDown(WindowsInput.VirtualKeyCode keyCode)
        {
            WindowsInput.InputSimulator.SimulateKeyDown(keyCode);
        }

        public void SimulateKeyPress(WindowsInput.VirtualKeyCode keyCode)
        {
            WindowsInput.InputSimulator.SimulateKeyPress(keyCode);
        }

        public void SimulateKeyUp(WindowsInput.VirtualKeyCode keyCode)
        {
            WindowsInput.InputSimulator.SimulateKeyUp(keyCode);
        }

        public void SimulateModifiedKeyStroke(IEnumerable<WindowsInput.VirtualKeyCode> modifierKeyCodes, IEnumerable<WindowsInput.VirtualKeyCode> keyCodes)
        {
            WindowsInput.InputSimulator.SimulateModifiedKeyStroke(modifierKeyCodes, keyCodes);
        }

        public void SimulateModifiedKeyStroke(IEnumerable<WindowsInput.VirtualKeyCode> modifierKeyCodes, WindowsInput.VirtualKeyCode keyCode)
        {
            WindowsInput.InputSimulator.SimulateModifiedKeyStroke(modifierKeyCodes, keyCode);
        }

        public void SimulateModifiedKeyStroke(WindowsInput.VirtualKeyCode modifierKey, IEnumerable<WindowsInput.VirtualKeyCode> keyCodes)
        {
            WindowsInput.InputSimulator.SimulateModifiedKeyStroke(modifierKey, keyCodes);
        }

        public void SimulateModifiedKeyStroke(WindowsInput.VirtualKeyCode modifierKeyCode, WindowsInput.VirtualKeyCode keyCode)
        {
            WindowsInput.InputSimulator.SimulateModifiedKeyStroke(modifierKeyCode, keyCode);
        }

        public void SimulateTextEntry(string text)
        {
            WindowsInput.InputSimulator.SimulateTextEntry(text);
        }
    }
}

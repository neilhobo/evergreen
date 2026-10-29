using System.Collections.Generic;
using WindowsInput;

namespace Globeweigh.Shared.UI.TouchScreenKeyboard
{
    public interface IInputSimulator
    {
        bool IsKeyDown(VirtualKeyCode keyCode);
        bool IsKeyDownAsync(VirtualKeyCode keyCode);
        bool IsTogglingKeyInEffect(VirtualKeyCode keyCode);
        void SimulateKeyDown(VirtualKeyCode keyCode);
        void SimulateKeyPress(VirtualKeyCode keyCode);
        void SimulateKeyUp(VirtualKeyCode keyCode);
        void SimulateModifiedKeyStroke(IEnumerable<VirtualKeyCode> modifierKeyCodes, IEnumerable<VirtualKeyCode> keyCodes);
        void SimulateModifiedKeyStroke(IEnumerable<VirtualKeyCode> modifierKeyCodes, VirtualKeyCode keyCode);
        void SimulateModifiedKeyStroke(VirtualKeyCode modifierKey, IEnumerable<VirtualKeyCode> keyCodes);
        void SimulateModifiedKeyStroke(VirtualKeyCode modifierKeyCode, VirtualKeyCode keyCode);
        void SimulateTextEntry(string text);
    }
}

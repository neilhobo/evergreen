using System;

namespace Globeweigh.Shared.UI.TouchScreenKeyboard
{
    public class InputSimulatorScope : IDisposable
    {
        private static InputSimulatorWrapper _defaultImpl = new InputSimulatorWrapper();

        public InputSimulatorScope(IInputSimulator implementation) 
        {
            _inputSimulatorImpl = implementation;
        }

        [ThreadStatic]
        private static IInputSimulator _inputSimulatorImpl;

        public static IInputSimulator Current 
        {
            get
            {
                if (_inputSimulatorImpl != null)
                    return _inputSimulatorImpl;
                return _defaultImpl;
            }
        }

        public void Dispose()
        {
            _inputSimulatorImpl = null;
        }
    }
}

using System.Windows;

namespace Globeweigh.Shared.UI.TouchScreenKeyboard
{
    public interface ITouchScreenKeyboard
    {
        void Close();
        void Position(UIElement ctrl, Point r);
        void Show();
        bool IsShowing { get; set; }
    }
}

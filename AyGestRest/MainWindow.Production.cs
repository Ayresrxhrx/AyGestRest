using System.Windows;
using System.Windows.Input;
using AyGestRest.Views;

namespace AyGestRest
{
    public partial class MainWindow
    {
        static MainWindow()
        {
            EventManager.RegisterClassHandler(
                typeof(MainWindow),
                Keyboard.KeyDownEvent,
                new KeyEventHandler(HandleProductionShortcut));
        }

        private static void HandleProductionShortcut(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.P || Keyboard.Modifiers != (ModifierKeys.Control | ModifierKeys.Shift))
                return;

            if (sender is not MainWindow owner)
                return;

            var window = new ProductionCenterWindow { Owner = owner };
            window.ShowDialog();
            e.Handled = true;
        }
    }
}

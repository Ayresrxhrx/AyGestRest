using System.Windows;
using System.Windows.Input;
using AyGestRest.Views;

namespace AyGestRest
{
    public partial class MainWindow
    {
        static MainWindow()
        {
            EventManager.RegisterClassHandler(typeof(MainWindow), Keyboard.KeyDownEvent, new KeyEventHandler(HandleProductionShortcut));
        }

        private static void HandleProductionShortcut(object sender, KeyEventArgs e)
        {
            if (sender is not MainWindow owner) return;

            if (e.Key == Key.P && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
            {
                new ProductionCenterWindow { Owner = owner }.ShowDialog();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.F && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
            {
                new FaturasWindow { Owner = owner }.ShowDialog();
                e.Handled = true;
            }
        }
    }
}

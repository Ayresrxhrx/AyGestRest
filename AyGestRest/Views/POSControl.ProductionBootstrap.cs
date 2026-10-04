using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace AyGestRest.Views
{
    internal static class POSControlProductionBootstrap
    {
        [ModuleInitializer]
        internal static void Initialize()
        {
            EventManager.RegisterClassHandler(
                typeof(POSControl),
                FrameworkElement.LoadedEvent,
                new RoutedEventHandler(OnLoaded));
        }

        private static void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (sender is POSControl control)
                control.RegisterProductionHandlersDirect();
        }
    }

    public partial class POSControl
    {
        private void RegisterProductionHandlersDirect()
        {
            if (_productionHandlersRegistered) return;
            _productionHandlersRegistered = true;

            if (btnFinalizarMultiplo != null)
            {
                btnFinalizarMultiplo.AddHandler(UIElement.PreviewMouseLeftButtonDownEvent, new MouseButtonEventHandler(ProductionPaymentMouseDown), true);
                btnFinalizarMultiplo.AddHandler(UIElement.PreviewKeyDownEvent, new KeyEventHandler(ProductionPaymentKeyDown), true);
            }

            if (btnFinalizarPagamentoMultiplo != null)
            {
                btnFinalizarPagamentoMultiplo.AddHandler(UIElement.PreviewMouseLeftButtonDownEvent, new MouseButtonEventHandler(ProductionPaymentMouseDown), true);
                btnFinalizarPagamentoMultiplo.AddHandler(UIElement.PreviewKeyDownEvent, new KeyEventHandler(ProductionPaymentKeyDown), true);
            }

            if (pnlBotoesMetodosSimplificado != null)
            {
                pnlBotoesMetodosSimplificado.AddHandler(UIElement.PreviewMouseLeftButtonDownEvent, new MouseButtonEventHandler(ProductionSimplePaymentMouseDown), true);
                pnlBotoesMetodosSimplificado.AddHandler(UIElement.PreviewKeyDownEvent, new KeyEventHandler(ProductionSimplePaymentKeyDown), true);
            }
        }
    }
}

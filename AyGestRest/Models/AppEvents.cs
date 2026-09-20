// AppEvents.cs
using System;
using System.Collections.ObjectModel;
using AyGestRest.Views;

namespace AyGestRest
{
    public static class AppEvents
    {
        // Eventos para atualização em tempo real
        public static event Action<ObservableCollection<POSControl.CartItem>>? CartUpdated;
        public static event Action<decimal>? DiscountUpdated;
        public static event Action<string>? PaymentMethodUpdated;
        public static event Action<string, string>? OrderInfoUpdated; // orderNumber, table
        public static event Action? SaleFinalized;
        public static event Action? TableStatusChanged;
        public static event Action<int>? SpecificTableStatusChanged;

        // Métodos para disparar eventos
        public static void RaiseCartUpdated(ObservableCollection<POSControl.CartItem> cart)
        {
            CartUpdated?.Invoke(cart);
        }

        public static void RaiseDiscountUpdated(decimal discount)
        {
            DiscountUpdated?.Invoke(discount);
        }

        public static void RaisePaymentMethodUpdated(string paymentMethod)
        {
            PaymentMethodUpdated?.Invoke(paymentMethod);
        }

        public static void RaiseOrderInfoUpdated(string orderNumber, string table)
        {
            OrderInfoUpdated?.Invoke(orderNumber, table);
        }

        public static void RaiseSaleFinalized()
        {
            SaleFinalized?.Invoke();
        }

        // Para mudanças gerais de todas as mesas
        public static void RaiseTableStatusChanged()
        {
            TableStatusChanged?.Invoke();
        }

        // Para mudanças específicas de uma mesa
        public static void RaiseSpecificTableStatusChanged(int tableId)
        {
            SpecificTableStatusChanged?.Invoke(tableId);
        }
    }
}
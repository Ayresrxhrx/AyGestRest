using AyGestRest.Data;
using AyGestRest.Models;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace AyGestRest.Services
{
    public sealed class TableSaleStateCoordinator : IDisposable
    {
        private bool _disposed;

        public TableSaleStateCoordinator()
        {
            AppEvents.SaleFinalized += OnSaleFinalized;
        }

        private async void OnSaleFinalized()
        {
            if (_disposed) return;

            try
            {
                using var db = new AyGestRestContext();
                var occupiedTables = await db.Tables
                    .Where(t => t.Status == TableStatus.Ocupada)
                    .ToListAsync();

                if (occupiedTables.Count == 0) return;

                var tableIdsWithOpenSales = await db.Orders
                    .Where(o => o.TableId.HasValue && !o.IsClosed && !o.IsPaid &&
                                o.Status != OrderStatus.Cancelado && o.Status != OrderStatus.Fechado)
                    .Select(o => o.TableId!.Value)
                    .Distinct()
                    .ToListAsync();

                var changed = false;
                foreach (var table in occupiedTables)
                {
                    if (!tableIdsWithOpenSales.Contains(table.Id))
                    {
                        table.Status = TableStatus.Livre;
                        changed = true;
                        AppEvents.RaiseSpecificTableStatusChanged(table.Id);
                    }
                }

                if (changed)
                    await db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Erro ao sincronizar estado das mesas após fecho: {ex.Message}");
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            AppEvents.SaleFinalized -= OnSaleFinalized;
        }
    }
}

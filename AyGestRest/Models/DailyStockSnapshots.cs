using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AyGestRest.Models
{
    // Adicione estas classes ao seu Models

    public class DailyStockSnapshot
    {
        public int Id { get; set; }

        public int ProductId { get; set; }
        public Product? Product { get; set; }

        public string ProductName { get; set; } = string.Empty;
        public string? ProductCode { get; set; }
        public string? CategoryName { get; set; }

        public decimal Quantity { get; set; }
        public string Unit { get; set; } = "unidade";

        public DateTime SnapshotDate { get; set; }
        public DateTime CreatedAt { get; set; }

        // Informações adicionais para análise
        public decimal MinimumStockLevel { get; set; }
        public bool IsActive { get; set; }
        public bool TrackInventory { get; set; }

        // Valor monetário do estoque (preço * quantidade)
        public decimal StockValue { get; set; }
    }



    public class StockComparisonResult
    {
        public int ProductId { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public string? ProductCode { get; set; }
        public string? Category { get; set; }
        public string Unit { get; set; } = "unidade";

        public decimal CurrentStock { get; set; }
        public decimal PreviousStock { get; set; }
        public decimal StockDifference { get; set; }
        public decimal PercentageChange { get; set; }

        public StockStatus Status { get; set; }
        public string StatusText => Status.ToString();
        public string StatusColor => GetStatusColor();

        // Para ordenação
        public bool IsPositiveChange => StockDifference > 0;
        public bool IsNegativeChange => StockDifference < 0;
        public bool HasChange => StockDifference != 0;

        private string GetStatusColor()
        {
            return Status switch
            {
                StockStatus.Critical => "#EF4444",     // Red
                StockStatus.Low => "#F59E0B",         // Orange
                StockStatus.Normal => "#10B981",      // Green
                StockStatus.High => "#3B82F6",        // Blue
                _ => "#6B7280"                        // Gray
            };
        }
    }

    public enum StockStatus
    {
        Critical,   // Abaixo de 30% do mínimo
        Low,        // Abaixo do mínimo
        Normal,     // Acima do mínimo
        High        // Acima de 150% do ideal
    }

    public class InventoryStats
    {
        public int TotalProducts { get; set; }
        public int ActiveProducts { get; set; }
        public int TrackedProducts { get; set; }

        public int CriticalStockItems { get; set; }
        public int LowStockItems { get; set; }

        public decimal TotalStockValue { get; set; }
        public decimal AverageStockValue { get; set; }

        // Comparação com período anterior
        public int IncreasedItems { get; set; }
        public int DecreasedItems { get; set; }
        public int UnchangedItems { get; set; }

        public decimal TotalIncreaseValue { get; set; }
        public decimal TotalDecreaseValue { get; set; }
        public decimal NetChangeValue { get; set; }

        // Estatísticas por categoria
        public Dictionary<string, int> ItemsByCategory { get; set; } = new();
        public Dictionary<string, decimal> ValueByCategory { get; set; } = new();
    }
}

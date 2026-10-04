using AyGestRest.Data;
using AyGestRest.Models;
using Microsoft.EntityFrameworkCore;

namespace AyGestRest.Services
{
    public sealed class FaturacaoService
    {
        private readonly AyGestRestContext _db;
        private readonly AppConfig _config;

        public FaturacaoService(AyGestRestContext db, AppConfig? config = null)
        {
            _db = db;
            _config = config ?? AppConfig.Load();
        }

        public async Task<Fatura> EmitirFaturaAsync(
            int orderId,
            int? clienteId = null,
            string? clienteNome = null,
            string? clienteNuit = null,
            decimal? taxaIva = null,
            CancellationToken cancellationToken = default)
        {
            var order = await _db.Orders
                .Include(o => o.Items)
                    .ThenInclude(i => i.Product)
                .Include(o => o.Payments)
                .FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken);

            if (order == null)
                throw new InvalidOperationException("O pedido indicado não existe.");

            var existente = await _db.Set<Fatura>()
                .AsNoTracking()
                .FirstOrDefaultAsync(f => f.OrderId == orderId && f.Estado != FaturaEstado.Anulada, cancellationToken);

            if (existente != null)
                return existente;

            var config = await _db.RestaurantConfigs.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
            var iva = taxaIva ?? await ObterTaxaIvaAsync(cancellationToken);
            var subtotal = order.Items.Sum(i => i.Total > 0 ? i.Total : i.Quantity * i.PriceAtMoment);
            var total = order.TotalAmount > 0 ? order.TotalAmount : order.Total;
            var imposto = iva > 0 ? Math.Round(total * iva / (100m + iva), 2) : 0m;
            var subtotalSemImposto = total - imposto;
            var valorPago = order.Payments.Sum(p => p.Amount > 0 ? p.Amount : p.Valor ?? 0m);
            var troco = Math.Max(0m, valorPago - total);

            var fatura = new Fatura
            {
                Numero = await GerarNumeroAsync(cancellationToken),
                Serie = _config.InvoiceSeries,
                DataEmissao = DateTime.Now,
                OrderId = order.Id,
                ClienteId = clienteId,
                ClienteNome = clienteNome ?? order.ClienteNome,
                ClienteNuit = clienteNuit ?? string.Empty,
                Moeda = _config.CurrencyCode,
                Subtotal = subtotalSemImposto,
                Desconto = Math.Max(0m, subtotal - total),
                Imposto = imposto,
                Total = total,
                ValorPago = valorPago,
                Troco = troco,
                MetodoPagamento = string.Join(", ", order.Payments.Select(p => !string.IsNullOrWhiteSpace(p.TipoPagamento) ? p.TipoPagamento : p.Type.ToString()).Distinct()),
                NUITEmitente = config?.Nuit ?? string.Empty,
                NomeEmitente = config?.RestaurantName ?? "AyGest POS",
                Estado = FaturaEstado.Emitida,
                TerminalId = _config.TerminalId,
                CreatedAt = DateTime.Now
            };

            foreach (var item in order.Items)
            {
                var bruto = item.Total > 0 ? item.Total : item.Quantity * item.PriceAtMoment;
                var itemIva = iva > 0 ? Math.Round(bruto * iva / (100m + iva), 2) : 0m;

                fatura.Itens.Add(new FaturaItem
                {
                    ProductId = item.ProductId,
                    Descricao = item.DisplayName,
                    Quantidade = item.Quantity,
                    PrecoUnitario = item.PriceAtMoment > 0 ? item.PriceAtMoment : item.UnitPrice,
                    TaxaIVA = iva,
                    ValorIVA = itemIva,
                    Total = bruto
                });
            }

            _db.Set<Fatura>().Add(fatura);
            await _db.SaveChangesAsync(cancellationToken);
            return fatura;
        }

        public async Task AnularFaturaAsync(int faturaId, string motivo, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(motivo))
                throw new ArgumentException("É obrigatório indicar o motivo da anulação.", nameof(motivo));

            var fatura = await _db.Set<Fatura>().FirstOrDefaultAsync(f => f.Id == faturaId, cancellationToken);
            if (fatura == null)
                throw new InvalidOperationException("Fatura não encontrada.");

            if (fatura.Estado == FaturaEstado.Anulada)
                return;

            fatura.Estado = FaturaEstado.Anulada;
            fatura.AnuladaEm = DateTime.Now;
            fatura.MotivoAnulacao = motivo.Trim();
            await _db.SaveChangesAsync(cancellationToken);
        }

        private async Task<decimal> ObterTaxaIvaAsync(CancellationToken cancellationToken)
        {
            var taxa = await _db.TaxasIVA
                .AsNoTracking()
                .OrderByDescending(t => t.Id)
                .FirstOrDefaultAsync(cancellationToken);

            return taxa?.Percentagem ?? 0m;
        }

        private async Task<string> GerarNumeroAsync(CancellationToken cancellationToken)
        {
            var prefixo = $"{_config.InvoiceSeries}-{DateTime.Now:yyyy}";
            var ultimo = await _db.Set<Fatura>()
                .Where(f => f.Serie == _config.InvoiceSeries && f.Numero.StartsWith(prefixo + "/"))
                .OrderByDescending(f => f.Id)
                .Select(f => f.Numero)
                .FirstOrDefaultAsync(cancellationToken);

            var sequencia = 1;
            if (!string.IsNullOrWhiteSpace(ultimo))
            {
                var parte = ultimo.Split('/').LastOrDefault();
                if (int.TryParse(parte, out var atual))
                    sequencia = atual + 1;
            }

            return $"{prefixo}/{sequencia:000000}";
        }
    }
}

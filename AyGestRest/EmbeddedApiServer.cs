using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AyGestRest.Data;
using AyGestRest.Models;
using AyGestRest.Services;
using Microsoft.EntityFrameworkCore;
using System.Windows;

namespace AyGestRest
{
    public sealed class EmbeddedApiServer : IDisposable
    {
        private const int DefaultPort = 5050;
        private readonly HttpListener _listener = new();
        private readonly string _url;
        private readonly CancellationTokenSource _cts = new();
        private readonly object _idempotencyLock = new();
        private readonly string _idempotencyFile;
        private Dictionary<string, int> _idempotency = new(StringComparer.OrdinalIgnoreCase);
        private Task? _serverTask;
        private NetworkDiscovery? _discovery;
        private bool _started;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = false
        };

        public EmbeddedApiServer(string port = "5050")
        {
            if (!int.TryParse(port, out var parsedPort) || parsedPort is < 1 or > 65535)
                parsedPort = DefaultPort;

            _url = $"http://*:{parsedPort}/";
            _idempotencyFile = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "AyGestRest", "mobile-idempotency.json");
            LoadIdempotency();
        }

        public void Start()
        {
            if (_started) return;
            try
            {
                _listener.Prefixes.Add(_url);
                _listener.Start();
                _discovery = new NetworkDiscovery(50555);
                _discovery.StartDiscovery();
                _serverTask = Task.Run(HandleRequests, _cts.Token);
                _started = true;
                System.Diagnostics.Debug.WriteLine($"API Mobile iniciado em {_url}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Erro ao iniciar API Mobile: {ex.Message}");
            }
        }

        private async Task HandleRequests()
        {
            while (!_cts.IsCancellationRequested && _listener.IsListening)
            {
                try
                {
                    var context = await _listener.GetContextAsync().ConfigureAwait(false);
                    _ = Task.Run(() => ProcessRequest(context), _cts.Token);
                }
                catch (HttpListenerException) { break; }
                catch (ObjectDisposedException) { break; }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Erro no HttpListener: {ex.Message}"); }
            }
        }

        private async Task ProcessRequest(HttpListenerContext context)
        {
            var response = context.Response;
            try
            {
                var request = context.Request;
                var path = request.Url?.AbsolutePath.TrimEnd('/').ToLowerInvariant() ?? string.Empty;
                var method = request.HttpMethod.ToUpperInvariant();
                ConfigureCors(response);

                if (method == "OPTIONS") { response.StatusCode = 204; return; }
                response.ContentType = "application/json; charset=utf-8";

                using var db = new AyGestRestContext();

                if (path == "/api/health" && method == "GET")
                {
                    await WriteResponse(response, 200, new { status = "OK", service = "AyGestRest Mobile API", time = DateTime.Now });
                    return;
                }

                if (path == "/api/products" && method == "GET")
                {
                    var products = await db.Products.AsNoTracking().Where(p => p.Active).OrderBy(p => p.Name)
                        .Select(p => new
                        {
                            p.Id, p.Name, p.Code, p.Description, p.Price, p.CategoryId,
                            category = p.Category == null ? null : new { id = p.Category.Id, name = p.Category.Nome, color = p.Category.CorHex, icon = p.Category.Icone },
                            p.Featured, p.Promo, p.Image, p.IsComposite, p.PrintToKitchen
                        }).ToListAsync();
                    await WriteResponse(response, 200, products);
                    return;
                }

                if (path.StartsWith("/api/products/", StringComparison.Ordinal) && method == "GET")
                {
                    if (!TryGetId(path, "/api/products/", out var productId)) { await WriteError(response, 400, "ID do produto inválido."); return; }
                    var product = await db.Products.AsNoTracking().Where(p => p.Id == productId && p.Active)
                        .Select(p => new
                        {
                            p.Id, p.Name, p.Code, p.Description, p.Price, p.CategoryId,
                            category = p.Category == null ? null : new { id = p.Category.Id, name = p.Category.Nome, color = p.Category.CorHex, icon = p.Category.Icone },
                            p.Featured, p.Promo, p.Image, p.IsComposite, p.PrintToKitchen
                        }).FirstOrDefaultAsync();
                    if (product == null) { await WriteError(response, 404, "Produto não encontrado ou indisponível."); return; }
                    await WriteResponse(response, 200, product);
                    return;
                }

                if (path == "/api/categories" && method == "GET")
                {
                    var categories = await db.ProductCategories.AsNoTracking().Where(c => c.Ativo).OrderBy(c => c.Nome)
                        .Select(c => new { id = c.Id, name = c.Nome, color = c.CorHex, icon = c.Icone }).ToListAsync();
                    await WriteResponse(response, 200, categories);
                    return;
                }

                if (path == "/api/tables" && method == "GET")
                {
                    var tables = await db.RestaurantTables.AsNoTracking().OrderBy(t => t.Id)
                        .Select(t => new
                        {
                            t.Id, t.Number, t.Name, t.Capacity,
                            status = t.Status.ToString(), statusValue = (int)t.Status, t.Notes,
                            hasOpenSale = db.Orders.Any(o => o.TableId == t.Id && !o.IsClosed && !o.IsPaid && o.Status != OrderStatus.Cancelado && o.Status != OrderStatus.Fechado)
                        }).ToListAsync();
                    await WriteResponse(response, 200, tables);
                    return;
                }

                if (path.StartsWith("/api/tables/", StringComparison.Ordinal) && path.EndsWith("/orders", StringComparison.Ordinal) && method == "GET")
                {
                    var idPart = path["/api/tables/".Length..^"/orders".Length].Trim('/');
                    if (!int.TryParse(idPart, out var tableId)) { await WriteError(response, 400, "ID da mesa inválido."); return; }
                    var orders = await GetOrdersQuery(db).Where(o => o.TableId == tableId && !o.IsClosed && !o.IsPaid && o.Status != OrderStatus.Cancelado)
                        .OrderByDescending(o => o.OpenDate).Take(50).ToListAsync();
                    await WriteResponse(response, 200, orders);
                    return;
                }

                if (path == "/api/orders" && method == "GET")
                {
                    var orders = await GetOrdersQuery(db).OrderByDescending(o => o.OpenDate).Take(200).ToListAsync();
                    await WriteResponse(response, 200, orders);
                    return;
                }

                if (path.StartsWith("/api/orders/", StringComparison.Ordinal) && path.EndsWith("/status", StringComparison.Ordinal) && method == "GET")
                {
                    var idPart = path["/api/orders/".Length..^"/status".Length].Trim('/');
                    if (!int.TryParse(idPart, out var orderId)) { await WriteError(response, 400, "ID do pedido inválido."); return; }
                    var orderStatus = await db.Orders.AsNoTracking().Where(o => o.Id == orderId).Select(o => new
                    {
                        o.Id, status = o.Status.ToString(), statusValue = (int)o.Status,
                        isPaid = o.IsPaid, isClosed = o.IsClosed,
                        items = o.Items.Select(i => new { i.Id, i.ProductId, kitchenStatus = i.KitchenStatus.ToString(), kitchenStatusValue = (int)i.KitchenStatus, i.Estado }).ToList()
                    }).FirstOrDefaultAsync();
                    if (orderStatus == null) { await WriteError(response, 404, "Pedido não encontrado."); return; }
                    await WriteResponse(response, 200, orderStatus);
                    return;
                }

                if (path.StartsWith("/api/orders/", StringComparison.Ordinal) && method == "GET")
                {
                    if (!TryGetId(path, "/api/orders/", out var orderId)) { await WriteError(response, 400, "ID do pedido inválido."); return; }
                    var order = await GetOrdersQuery(db).Where(o => o.Id == orderId).FirstOrDefaultAsync();
                    if (order == null) { await WriteError(response, 404, "Pedido não encontrado."); return; }
                    await WriteResponse(response, 200, order);
                    return;
                }

                if (path == "/api/orders" && method == "POST")
                {
                    await CreateMobileOrder(request, response, db);
                    return;
                }

                if (path == "/api/login" && method == "POST")
                {
                    await Login(request, response, db);
                    return;
                }

                if (path == "/api/mobile/config" && method == "GET")
                {
                    var config = await db.RestaurantConfigs.AsNoTracking().FirstOrDefaultAsync();
                    await WriteResponse(response, 200, new
                    {
                        restaurantName = config?.RestaurantName ?? "AyGest Rest",
                        currency = config?.CurrencySymbol ?? "MT",
                        logoPath = config?.LogoPath,
                        hasLogo = config?.LogoBytes != null && config.LogoBytes.Length > 0,
                        kitchenPrintingEnabled = config?.PrintOrderToKitchen == true,
                        kitchenPrinterConfigured = !string.IsNullOrWhiteSpace(config?.KitchenPrinterName)
                    });
                    return;
                }

                await WriteError(response, 404, "Endpoint não encontrado.");
            }
            catch (JsonException) { await WriteError(response, 400, "JSON inválido."); }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Erro API Mobile: {ex}");
                await WriteError(response, 500, "Erro interno do servidor.");
            }
            finally { try { response.Close(); } catch { } }
        }

        private static IQueryable<Order> GetOrdersQuery(AyGestRestContext db)
        {
            return db.Orders.AsNoTracking()
                .Include(o => o.Items).ThenInclude(i => i.Product)
                .Include(o => o.Table).Include(o => o.User);
        }

        private static async Task CreateMobileOrder(HttpListenerRequest request, HttpListenerResponse response, AyGestRestContext db)
        {
            var payload = await ReadJson<MobileOrderRequest>(request);
            if (payload == null || payload.Items == null || payload.Items.Count == 0)
            { await WriteError(response, 400, "O pedido deve conter pelo menos um item."); return; }
            if (payload.Items.Any(i => i.ProductId <= 0 || i.Quantity <= 0 || i.Quantity > 999))
            { await WriteError(response, 400, "Itens do pedido inválidos."); return; }

            var requestKey = payload.ClientRequestId?.Trim();
            if (!string.IsNullOrWhiteSpace(requestKey) && TryGetIdempotentOrder(requestKey, out var existingOrderId))
            {
                var existing = await GetOrdersQuery(db).FirstOrDefaultAsync(o => o.Id == existingOrderId);
                if (existing != null) { await WriteResponse(response, 200, new { success = true, duplicate = true, order = existing }); return; }
            }

            if (!payload.TableId.HasValue)
            { await WriteError(response, 400, "A mesa é obrigatória para pedidos Mobile."); return; }

            var table = await db.RestaurantTables.FirstOrDefaultAsync(t => t.Id == payload.TableId.Value);
            if (table == null) { await WriteError(response, 400, "Mesa inválida."); return; }

            var user = payload.UserId.HasValue
                ? await db.Users.FirstOrDefaultAsync(u => u.Id == payload.UserId.Value && u.IsActive)
                : await db.Users.Where(u => u.IsActive && u.Role == UserRole.Funcionario).OrderBy(u => u.Id).FirstOrDefaultAsync();
            if (user == null) user = await db.Users.Where(u => u.IsActive).OrderBy(u => u.Id).FirstOrDefaultAsync();
            if (user == null) { await WriteError(response, 409, "Não existe nenhum funcionário activo para registar o pedido."); return; }

            var productIds = payload.Items.Select(i => i.ProductId).Distinct().ToList();
            var products = await db.Products.Include(p => p.Category).Where(p => productIds.Contains(p.Id) && p.Active).ToDictionaryAsync(p => p.Id);
            if (products.Count != productIds.Count)
            {
                var unavailable = productIds.Where(id => !products.ContainsKey(id));
                await WriteResponse(response, 409, new { success = false, message = "Um ou mais produtos estão indisponíveis.", unavailableProductIds = unavailable });
                return;
            }

            // Um pedido Mobile de uma mesa já ocupada entra na venda aberta dessa mesa.
            // O Mobile nunca paga nem fecha a venda.
            var order = await db.Orders
                .Include(o => o.Items).ThenInclude(i => i.Product)
                .Include(o => o.Table)
                .Where(o => o.TableId == table.Id && !o.IsClosed && !o.IsPaid && o.Status != OrderStatus.Cancelado && o.Status != OrderStatus.Fechado)
                .OrderByDescending(o => o.OpenDate)
                .FirstOrDefaultAsync();

            var isNewSale = order == null;
            if (order == null)
            {
                order = new Order
                {
                    TableId = table.Id,
                    UserId = user.Id,
                    Status = OrderStatus.Aberto,
                    OpenDate = DateTime.Now,
                    Data = DateTime.Now,
                    DataPedido = DateTime.Now,
                    Observations = payload.Observations?.Trim() ?? string.Empty,
                    Total = 0m,
                    TotalAmount = 0m,
                    IsPaid = false,
                    IsClosed = false
                };
                db.Orders.Add(order);
            }
            else if (!string.IsNullOrWhiteSpace(payload.Observations))
            {
                var observation = payload.Observations.Trim();
                order.Observations = string.IsNullOrWhiteSpace(order.Observations)
                    ? observation
                    : $"{order.Observations}\n{observation}";
            }

            var newlyAddedItems = new List<OrderItem>();
            foreach (var requestedItem in payload.Items)
            {
                var product = products[requestedItem.ProductId];
                var unitPrice = product.Price;
                var total = decimal.Round(unitPrice * requestedItem.Quantity, 2, MidpointRounding.AwayFromZero);
                var item = new OrderItem
                {
                    ProductId = product.Id,
                    Product = product,
                    Name = product.Name,
                    Categoria = product.Category?.Nome ?? string.Empty,
                    Quantity = requestedItem.Quantity,
                    PriceAtMoment = unitPrice,
                    UnitPrice = unitPrice,
                    Total = total,
                    Notes = requestedItem.Notes?.Trim() ?? string.Empty,
                    KitchenStatus = KitchenStatus.Pendente,
                    Estado = "Pendente",
                    CreatedAt = DateTime.Now
                };
                order.Items.Add(item);
                newlyAddedItems.Add(item);
                order.Total += total;
            }

            order.Total = decimal.Round(order.Total, 2, MidpointRounding.AwayFromZero);
            order.TotalAmount = order.Total;
            order.IsPaid = false;
            order.IsClosed = false;
            if (order.Status == OrderStatus.Pago || order.Status == OrderStatus.Fechado || order.Status == OrderStatus.Cancelado)
                order.Status = OrderStatus.Aberto;

            // Pedido confirmado => mesa ocupada. Só o Desktop/libertação normal da venda volta a Livre.
            table.Status = TableStatus.Ocupada;

            await db.SaveChangesAsync();

            if (!string.IsNullOrWhiteSpace(requestKey)) SaveIdempotentOrder(requestKey, order.Id);

            var config = await db.RestaurantConfigs.AsNoTracking().FirstOrDefaultAsync();
            var printResult = await KitchenPrinterService.TryPrintItemsAsync(order, config ?? new RestaurantConfig(), newlyAddedItems);

            RaiseTableChangedOnUiThread(table.Id);

            var created = await GetOrdersQuery(db).FirstAsync(o => o.Id == order.Id);
            response.StatusCode = isNewSale ? 201 : 200;
            await WriteResponse(response, response.StatusCode, new
            {
                success = true,
                duplicate = false,
                saleCreated = isNewSale,
                saleRemainsOpen = true,
                tableOccupied = true,
                kitchenPrint = new { printed = printResult.Printed, message = printResult.Message, printer = printResult.PrinterName },
                order = created
            });
        }

        private static void RaiseTableChangedOnUiThread(int tableId)
        {
            try
            {
                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher == null) return;
                dispatcher.BeginInvoke(new Action(() => AppEvents.RaiseSpecificTableStatusChanged(tableId)));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Erro ao notificar alteração de mesa: {ex.Message}");
            }
        }

        private static async Task Login(HttpListenerRequest request, HttpListenerResponse response, AyGestRestContext db)
        {
            var loginData = await ReadJson<LoginRequest>(request);
            if (loginData == null || string.IsNullOrWhiteSpace(loginData.Username) || string.IsNullOrWhiteSpace(loginData.Password))
            { await WriteError(response, 400, "Utilizador e palavra-passe são obrigatórios."); return; }

            var username = loginData.Username.Trim();
            var user = await db.Users.FirstOrDefaultAsync(u => u.IsActive && u.Username.ToLower() == username.ToLower() && u.PasswordHash == loginData.Password);
            if (user == null) { await WriteError(response, 401, "Credenciais inválidas."); return; }

            await WriteResponse(response, 200, new
            {
                success = true,
                user = new { id = user.Id, username = user.Username, fullName = user.FullName, role = (int)user.Role, roleName = user.Role.ToString(), isActive = user.IsActive }
            });
        }

        private static async Task<T?> ReadJson<T>(HttpListenerRequest request)
        {
            using var reader = new StreamReader(request.InputStream, request.ContentEncoding ?? Encoding.UTF8);
            var body = await reader.ReadToEndAsync();
            return string.IsNullOrWhiteSpace(body) ? default : JsonSerializer.Deserialize<T>(body, JsonOptions);
        }

        private static async Task WriteResponse(HttpListenerResponse response, int statusCode, object data)
        {
            response.StatusCode = statusCode;
            response.ContentType = "application/json; charset=utf-8";
            var buffer = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(data, JsonOptions));
            response.ContentLength64 = buffer.Length;
            await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
        }

        private static Task WriteError(HttpListenerResponse response, int statusCode, string message)
            => WriteResponse(response, statusCode, new { success = false, error = message });

        private static void ConfigureCors(HttpListenerResponse response)
        {
            response.Headers["Access-Control-Allow-Origin"] = "*";
            response.Headers["Access-Control-Allow-Methods"] = "GET, POST, OPTIONS";
            response.Headers["Access-Control-Allow-Headers"] = "Content-Type, Accept, X-Idempotency-Key";
            response.Headers["Cache-Control"] = "no-store";
        }

        private static bool TryGetId(string path, string prefix, out int id)
        {
            var value = path[prefix.Length..].Trim('/');
            return int.TryParse(value, out id) && id > 0;
        }

        private void LoadIdempotency()
        {
            try
            {
                if (!File.Exists(_idempotencyFile)) return;
                var data = JsonSerializer.Deserialize<Dictionary<string, int>>(File.ReadAllText(_idempotencyFile), JsonOptions);
                if (data != null) _idempotency = new Dictionary<string, int>(data, StringComparer.OrdinalIgnoreCase);
            }
            catch { _idempotency = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase); }
        }

        private bool TryGetIdempotentOrder(string key, out int orderId)
        {
            lock (_idempotencyLock) return _idempotency.TryGetValue(key, out orderId);
        }

        private void SaveIdempotentOrder(string key, int orderId)
        {
            lock (_idempotencyLock)
            {
                _idempotency[key] = orderId;
                try
                {
                    var directory = Path.GetDirectoryName(_idempotencyFile);
                    if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
                    File.WriteAllText(_idempotencyFile, JsonSerializer.Serialize(_idempotency, JsonOptions));
                }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Falha ao guardar idempotência Mobile: {ex.Message}"); }
            }
        }

        public void Stop()
        {
            if (!_started && !_listener.IsListening) return;
            try { _cts.Cancel(); } catch { }
            try { _discovery?.Stop(); } catch { }
            try { _listener.Stop(); } catch { }
            _started = false;
        }

        public void Dispose()
        {
            Stop();
            try { _listener.Close(); } catch { }
            try { _discovery?.Dispose(); } catch { }
            _cts.Dispose();
        }
    }

    public sealed class LoginRequest
    {
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
    }
}

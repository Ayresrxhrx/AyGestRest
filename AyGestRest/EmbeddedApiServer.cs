using System;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AyGestRest.Data;
using AyGestRest.Models;
using Microsoft.EntityFrameworkCore;

namespace AyGestRest
{
    public class EmbeddedApiServer : IDisposable
    {
        private HttpListener _listener;
        private readonly string _url;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private Task _serverTask;
        private NetworkDiscovery _discovery;

        public EmbeddedApiServer(string port = "5050")
        {
            _url = $"http://*:{port}/";
        }

        public void Start()
        {
            try
            {
                _listener = new HttpListener();
                _listener.Prefixes.Add(_url);
                _listener.Start();

                // Also start discovery to respond to broadcasts
                _discovery = new NetworkDiscovery(50555);
                _discovery.StartDiscovery();

                _serverTask = Task.Run(async () => await HandleRequests(), _cts.Token);

                System.Diagnostics.Debug.WriteLine($"🚀 API Server (HttpListener) iniciado em {_url}");
                System.Diagnostics.Debug.WriteLine($"📡 Discovery listening on port 50555");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Erro ao iniciar API Server: {ex.Message}");
            }
        }

        private async Task HandleRequests()
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                try
                {
                    var context = await _listener.GetContextAsync();
                    _ = Task.Run(() => ProcessRequest(context));
                }
                catch (HttpListenerException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Erro no HttpListener: {ex.Message}");
                }
            }
        }

        private async Task ProcessRequest(HttpListenerContext context)
        {
            try
            {
                var request = context.Request;
                var response = context.Response;

                string path = request.Url.AbsolutePath.ToLower();
                string method = request.HttpMethod;

                // Configurar CORS
                response.AppendHeader("Access-Control-Allow-Origin", "*");
                response.AppendHeader("Access-Control-Allow-Methods", "GET, POST, PUT, DELETE, OPTIONS");
                response.AppendHeader("Access-Control-Allow-Headers", "Content-Type, Accept");

                if (method == "OPTIONS")
                {
                    response.StatusCode = 200;
                    response.Close();
                    return;
                }

                response.ContentType = "application/json";

                using var db = new AyGestRestContext();

                // Health check
                if (path == "/api/health" && method == "GET")
                {
                    await WriteResponse(response, new { status = "OK", time = DateTime.Now });
                    return;
                }

                // Users
                if (path == "/api/users" && method == "GET")
                {
                    var users = await db.Users.ToListAsync();
                    await WriteResponse(response, users);
                    return;
                }

                // Products with all includes
                if (path == "/api/products" && method == "GET")
                {
                    var products = await db.Products
                        .Include(p => p.Category)
                        .Include(p => p.ProductIngredients)
                            .ThenInclude(pi => pi.Ingredient)
                        .ToListAsync();
                    await WriteResponse(response, products);
                    return;
                }

                // Categories
                if (path == "/api/categories" && method == "GET")
                {
                    var categories = await db.ProductCategories.ToListAsync();
                    await WriteResponse(response, categories);
                    return;
                }

                // Orders with all details
                if (path == "/api/orders" && method == "GET")
                {
                    var orders = await db.Orders
                        .Include(o => o.Items)
                            .ThenInclude(i => i.Product)
                        .Include(o => o.Payments)
                        .ToListAsync();
                    await WriteResponse(response, orders);
                    return;
                }

                // Create Order
                if (path == "/api/orders" && method == "POST")
                {
                    using var reader = new StreamReader(request.InputStream, request.ContentEncoding);
                    var body = await reader.ReadToEndAsync();

                    try
                    {
                        var order = JsonSerializer.Deserialize<Order>(body);

                        if (order != null)
                        {
                            db.Orders.Add(order);
                            await db.SaveChangesAsync();

                            response.StatusCode = 201;
                            await WriteResponse(response, order);
                            return;
                        }
                    }
                    catch (Exception ex)
                    {
                        response.StatusCode = 400;
                        await WriteResponse(response, new { error = ex.Message });
                        return;
                    }
                }

                // Tables
                if (path == "/api/tables" && method == "GET")
                {
                    var tables = await db.RestaurantTables.ToListAsync();
                    await WriteResponse(response, tables);
                    return;
                }

                // Clients
                if (path == "/api/clients" && method == "GET")
                {
                    var clients = await db.Clientes.ToListAsync();
                    await WriteResponse(response, clients);
                    return;
                }

                // Login
                if (path == "/api/login" && method == "POST")
                {
                    using var reader = new StreamReader(request.InputStream, request.ContentEncoding);
                    var body = await reader.ReadToEndAsync();

                    try
                    {
                        var loginData = JsonSerializer.Deserialize<LoginRequest>(body);

                        if (loginData != null)
                        {
                            var user = await db.Users
                                .FirstOrDefaultAsync(u => u.Username.ToLower() == loginData.Username.ToLower() &&
                                                          u.PasswordHash == loginData.Password);

                            if (user != null)
                            {
                                await WriteResponse(response, new
                                {
                                    success = true,
                                    user = new
                                    {
                                        id = user.Id,
                                        username = user.Username,
                                        fullName = user.FullName,
                                        role = (int)user.Role,
                                        isActive = user.IsActive
                                    }
                                });
                                return;
                            }
                        }

                        response.StatusCode = 401;
                        await WriteResponse(response, new { success = false, message = "Credenciais inválidas" });
                        return;
                    }
                    catch (Exception ex)
                    {
                        response.StatusCode = 400;
                        await WriteResponse(response, new { error = ex.Message });
                        return;
                    }
                }

                // Not found
                response.StatusCode = 404;
                await WriteResponse(response, new { error = "Endpoint not found" });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Erro ao processar request: {ex.Message}");
                context.Response.StatusCode = 500;
                await WriteResponse(context.Response, new { error = "Internal server error" });
            }
            finally
            {
                context.Response.Close();
            }
        }

        private async Task WriteResponse(HttpListenerResponse response, object data)
        {
            try
            {
                string json = JsonSerializer.Serialize(data, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    WriteIndented = false
                });

                byte[] buffer = Encoding.UTF8.GetBytes(json);
                response.ContentLength64 = buffer.Length;
                await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Erro ao escrever resposta: {ex.Message}");
            }
        }

        public void Stop()
        {
            _cts.Cancel();
            _discovery?.Stop();
            _listener?.Stop();
        }

        public void Dispose()
        {
            Stop();
            _listener?.Close();
            _discovery?.Dispose();
        }
    }

    public class LoginRequest
    {
        public string Username { get; set; }
        public string Password { get; set; }
    }
}
using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AyGestRest
{
    public class ApiClient : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly string _baseUrl;

        public ApiClient(string serverIp, string port = "5050", string? terminalId = null, string? terminalName = null)
        {
            _baseUrl = serverIp.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || serverIp.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                ? serverIp.TrimEnd('/')
                : $"http://{serverIp}:{port}";

            _httpClient = new HttpClient
            {
                BaseAddress = new Uri(_baseUrl + "/"),
                Timeout = TimeSpan.FromSeconds(15)
            };

            _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "AyGestPOS-Desktop");
            _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("X-Terminal-Id", string.IsNullOrWhiteSpace(terminalId) ? Environment.MachineName : terminalId);
            _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("X-Terminal-Name", string.IsNullOrWhiteSpace(terminalName) ? Environment.MachineName : terminalName);
        }

        public ApiClient(AppConfig config)
            : this(config.GetApiBaseUrl(), config.ServerPort, config.TerminalId, config.TerminalName)
        {
        }

        public async Task<T> GetAsync<T>(string endpoint, CancellationToken cancellationToken = default)
        {
            try
            {
                using var response = await _httpClient.GetAsync(endpoint, cancellationToken);
                response.EnsureSuccessStatusCode();
                string json = await response.Content.ReadAsStringAsync(cancellationToken);
                var result = JsonSerializer.Deserialize<T>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                return result ?? throw new InvalidOperationException("O servidor devolveu uma resposta vazia.");
            }
            catch (Exception ex)
            {
                throw new Exception($"Erro na requisição GET: {ex.Message}", ex);
            }
        }

        public async Task<TResponse> PostAsync<TRequest, TResponse>(string endpoint, TRequest data, CancellationToken cancellationToken = default)
        {
            try
            {
                string json = JsonSerializer.Serialize(data);
                using var content = new StringContent(json, Encoding.UTF8, "application/json");
                using var response = await _httpClient.PostAsync(endpoint, content, cancellationToken);
                response.EnsureSuccessStatusCode();
                string responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
                var result = JsonSerializer.Deserialize<TResponse>(responseJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                return result ?? throw new InvalidOperationException("O servidor devolveu uma resposta vazia.");
            }
            catch (Exception ex)
            {
                throw new Exception($"Erro na requisição POST: {ex.Message}", ex);
            }
        }

        public async Task<bool> TestConnection(CancellationToken cancellationToken = default)
        {
            try
            {
                using var response = await _httpClient.GetAsync("api/health", cancellationToken);
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        public void Dispose() => _httpClient.Dispose();
    }
}

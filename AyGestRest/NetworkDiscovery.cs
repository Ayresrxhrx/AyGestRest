using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AyGestRest
{
    public class ServerInfo
    {
        public string IpAddress { get; set; } = string.Empty;
        public string MachineName { get; set; } = string.Empty;
        public int ApiPort { get; set; } = 5050;
        public DateTime LastSeen { get; set; }
    }

    public class NetworkDiscovery : IDisposable
    {
        private readonly int _discoveryPort;
        private readonly int _apiPort;
        private UdpClient? _udpClient;
        private CancellationTokenSource? _cts;
        private readonly List<ServerInfo> _servers = new();
        private readonly object _lock = new();

        public event Action<List<ServerInfo>>? ServersUpdated;

        public NetworkDiscovery(int discoveryPort = 50555, int apiPort = 5050)
        {
            _discoveryPort = discoveryPort;
            _apiPort = apiPort;
        }

        public void StartDiscovery()
        {
            try
            {
                _cts = new CancellationTokenSource();
                Task.Run(() => StartListening(_cts.Token), _cts.Token);
                Thread.Sleep(250);
                Task.Run(BroadcastDiscovery);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Erro ao iniciar discovery: {ex.Message}");
            }
        }

        private async Task StartListening(CancellationToken token)
        {
            try
            {
                using (_udpClient = new UdpClient())
                {
                    _udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                    _udpClient.Client.Bind(new IPEndPoint(IPAddress.Any, _discoveryPort));

                    while (!token.IsCancellationRequested)
                    {
                        try
                        {
                            var result = await _udpClient.ReceiveAsync().WithCancellation(token);
                            string message = Encoding.UTF8.GetString(result.Buffer);

                            if (message == "AYGEST_DISCOVER")
                            {
                                await RespondToDiscovery(result.RemoteEndPoint);
                            }
                            else if (message.StartsWith("AYGEST_SERVER|", StringComparison.Ordinal))
                            {
                                string payload = message.Substring("AYGEST_SERVER|".Length);
                                var parts = payload.Split('|', StringSplitOptions.RemoveEmptyEntries);
                                var machineName = parts.ElementAtOrDefault(0) ?? "AyGestRest";
                                var apiPort = int.TryParse(parts.ElementAtOrDefault(1), out var parsedPort)
                                    ? parsedPort
                                    : 5050;

                                AddOrUpdateServer(result.RemoteEndPoint.Address.ToString(), machineName, apiPort);
                            }
                        }
                        catch (OperationCanceledException)
                        {
                            break;
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Discovery receive error: {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Discovery listener error: {ex.Message}");
            }
        }

        public async Task BroadcastDiscovery()
        {
            try
            {
                using var client = new UdpClient { EnableBroadcast = true };
                client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                byte[] data = Encoding.UTF8.GetBytes("AYGEST_DISCOVER");
                await client.SendAsync(data, data.Length, new IPEndPoint(IPAddress.Broadcast, _discoveryPort));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Erro ao broadcast discovery: {ex.Message}");
            }
        }

        public async Task RespondToDiscovery(IPEndPoint clientEndPoint)
        {
            try
            {
                using var client = new UdpClient();
                string response = $"AYGEST_SERVER|{Environment.MachineName}|{_apiPort}";
                byte[] data = Encoding.UTF8.GetBytes(response);
                await client.SendAsync(data, data.Length, clientEndPoint);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Erro ao responder discovery: {ex.Message}");
            }
        }

        private void AddOrUpdateServer(string ip, string machineName, int apiPort)
        {
            lock (_lock)
            {
                var existing = _servers.FirstOrDefault(s => s.IpAddress == ip);
                if (existing != null)
                {
                    existing.LastSeen = DateTime.Now;
                    existing.MachineName = machineName;
                    existing.ApiPort = apiPort;
                }
                else
                {
                    _servers.Add(new ServerInfo
                    {
                        IpAddress = ip,
                        MachineName = machineName,
                        ApiPort = apiPort,
                        LastSeen = DateTime.Now
                    });
                }

                _servers.RemoveAll(s => (DateTime.Now - s.LastSeen).TotalSeconds > 10);
                ServersUpdated?.Invoke(_servers.ToList());
            }
        }

        public List<ServerInfo> GetServers()
        {
            lock (_lock)
                return _servers.ToList();
        }

        public void Stop()
        {
            try { _cts?.Cancel(); } catch { }
            try { _udpClient?.Close(); } catch { }
        }

        public void Dispose()
        {
            Stop();
            try { _udpClient?.Dispose(); } catch { }
            _cts?.Dispose();
        }
    }

    public static class TaskExtensions
    {
        public static async Task<T> WithCancellation<T>(this Task<T> task, CancellationToken token)
        {
            var tcs = new TaskCompletionSource<bool>();
            using (token.Register(s => ((TaskCompletionSource<bool>)s!).TrySetResult(true), tcs))
            {
                if (task != await Task.WhenAny(task, tcs.Task))
                    throw new OperationCanceledException(token);
            }

            return await task;
        }
    }
}

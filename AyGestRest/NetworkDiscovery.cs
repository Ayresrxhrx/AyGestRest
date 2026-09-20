using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace AyGestRest
{
    public class ServerInfo
    {
        public string IpAddress { get; set; }
        public string MachineName { get; set; }
        public DateTime LastSeen { get; set; }
    }

    public class NetworkDiscovery : IDisposable
    {
        private readonly int _discoveryPort;
        private UdpClient _udpClient;
        private CancellationTokenSource _cts;
        private readonly List<ServerInfo> _servers = new List<ServerInfo>();
        private readonly object _lock = new object();
        private bool _isListening = false;

        public event Action<List<ServerInfo>> ServersUpdated;

        public NetworkDiscovery(int discoveryPort = 50555)
        {
            _discoveryPort = discoveryPort;
        }

        public void StartDiscovery()
        {
            try
            {
                _cts = new CancellationTokenSource();

                // Iniciar listener em background
                Task.Run(async () => await StartListening(_cts.Token), _cts.Token);

                // Pequena pausa para garantir que o listener iniciou
                Thread.Sleep(500);

                _isListening = true;

                // Fazer um broadcast inicial
                Task.Run(async () => await BroadcastDiscovery());
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

                    // Bind to all interfaces
                    var localEndPoint = new IPEndPoint(IPAddress.Any, _discoveryPort);
                    _udpClient.Client.Bind(localEndPoint);

                    System.Diagnostics.Debug.WriteLine($"🎧 Discovery listening on port {_discoveryPort}");

                    while (!token.IsCancellationRequested)
                    {
                        try
                        {
                            var result = await _udpClient.ReceiveAsync().WithCancellation(token);
                            string message = Encoding.UTF8.GetString(result.Buffer);

                            System.Diagnostics.Debug.WriteLine($"📩 Received: {message} from {result.RemoteEndPoint}");

                            if (message == "AYGEST_DISCOVER")
                            {
                                // Received discovery request - respond if we're a server (handled elsewhere)
                                System.Diagnostics.Debug.WriteLine($"🔍 Discovery request from {result.RemoteEndPoint}");
                            }
                            else if (message.StartsWith("AYGEST_SERVER|"))
                            {
                                // Received server response
                                string machineName = message.Substring("AYGEST_SERVER|".Length);
                                AddOrUpdateServer(result.RemoteEndPoint.Address.ToString(), machineName);
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
                using (var client = new UdpClient())
                {
                    client.EnableBroadcast = true;
                    client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);

                    byte[] data = Encoding.UTF8.GetBytes("AYGEST_DISCOVER");

                    // Broadcast to all network interfaces
                    await client.SendAsync(data, data.Length, new IPEndPoint(IPAddress.Broadcast, _discoveryPort));

                    System.Diagnostics.Debug.WriteLine($"📢 Broadcast discovery sent on port {_discoveryPort}");
                }
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
                using (var client = new UdpClient())
                {
                    string response = $"AYGEST_SERVER|{Environment.MachineName}";
                    byte[] data = Encoding.UTF8.GetBytes(response);
                    await client.SendAsync(data, data.Length, clientEndPoint);

                    System.Diagnostics.Debug.WriteLine($"📤 Responded to {clientEndPoint} with {response}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Erro ao responder discovery: {ex.Message}");
            }
        }

        private void AddOrUpdateServer(string ip, string machineName)
        {
            lock (_lock)
            {
                var existing = _servers.FirstOrDefault(s => s.IpAddress == ip);
                if (existing != null)
                {
                    existing.LastSeen = DateTime.Now;
                    existing.MachineName = machineName;
                }
                else
                {
                    _servers.Add(new ServerInfo
                    {
                        IpAddress = ip,
                        MachineName = machineName,
                        LastSeen = DateTime.Now
                    });

                    System.Diagnostics.Debug.WriteLine($"✅ New server found: {machineName} ({ip})");
                }

                // Remove servers not seen in last 10 seconds
                _servers.RemoveAll(s => (DateTime.Now - s.LastSeen).TotalSeconds > 10);

                ServersUpdated?.Invoke(_servers.ToList());
            }
        }

        public List<ServerInfo> GetServers()
        {
            lock (_lock)
            {
                return _servers.ToList();
            }
        }

        public void Stop()
        {
            _cts?.Cancel();
            _udpClient?.Close();
        }

        public void Dispose()
        {
            Stop();
            _udpClient?.Dispose();
        }
    }

    public static class TaskExtensions
    {
        public static async Task<T> WithCancellation<T>(this Task<T> task, CancellationToken token)
        {
            var tcs = new TaskCompletionSource<bool>();
            using (token.Register(s => ((TaskCompletionSource<bool>)s).TrySetResult(true), tcs))
            {
                if (task != await Task.WhenAny(task, tcs.Task))
                    throw new OperationCanceledException(token);
            }
            return await task;
        }
    }
}
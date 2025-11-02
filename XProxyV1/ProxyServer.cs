using System;
using System.Buffers;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace XProxyV1
{
    public class ProxyServer
    {
        private readonly int _port;
        private TcpListener _listener;
        private CancellationTokenSource _cts;
        private HashSet<string> _blockedDomains;
        private Dictionary<string, string> _redirects;
        private readonly SemaphoreSlim _blockedDomainsLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _redirectsLock = new SemaphoreSlim(1, 1);

        public ProxyServer(int port)
        {
            _port = port;
            _blockedDomains = ConfigManager.LoadBlacklist();
            _redirects = ConfigManager.LoadRedirects();
            _cts = new CancellationTokenSource();
        }

        public async Task ReloadBlacklistAsync()
        {
            await _blockedDomainsLock.WaitAsync();
            try
            {
                _blockedDomains = ConfigManager.LoadBlacklist();
                SafePrint("Blacklist reloaded!");
            }
            finally
            {
                _blockedDomainsLock.Release();
            }
        }

        public async Task ReloadRedirectsAsync()
        {
            await _redirectsLock.WaitAsync();
            try
            {
                _redirects = ConfigManager.LoadRedirects();
                SafePrint("Redirects reloaded!");
            }
            finally
            {
                _redirectsLock.Release();
            }
        }

        public async Task StartAsync()
        {
            _listener = new TcpListener(IPAddress.Any, _port);
            _listener.Server.NoDelay = true;
            _listener.Server.ReceiveBufferSize = 524288;
            _listener.Server.SendBufferSize = 524288;
            _listener.Start(500);
            SafePrint($"Proxy running on Port: {_port}");

            try
            {
                while (!_cts.Token.IsCancellationRequested)
                {
                    var client = await _listener.AcceptTcpClientAsync();
                    _ = Task.Run(() => HandleClientAsync(client), _cts.Token);
                }
            }
            catch (Exception ex)
            {
                SafePrint($"Proxy error: {ex.Message}");
            }
            finally
            {
                _listener?.Stop();
                SafePrint("Proxy closed.");
            }
        }

        public void Stop()
        {
            _cts?.Cancel();
            _listener?.Stop();
        }

        private async Task<string> ApplyRedirectAsync(string host)
        {
            await _redirectsLock.WaitAsync();
            try
            {
                foreach (var redirect in _redirects)
                {
                    if (host.Equals(redirect.Key, StringComparison.OrdinalIgnoreCase))
                    {
                        SafePrint($"Redirect: {host} -> {redirect.Value}");
                        return redirect.Value;
                    }
                }
                return host;
            }
            finally
            {
                _redirectsLock.Release();
            }
        }

        private async Task<bool> IsBlockedAsync(string host)
        {
            await _blockedDomainsLock.WaitAsync();
            try
            {
                foreach (var blocked in _blockedDomains)
                {
                    if (host.Equals(blocked, StringComparison.OrdinalIgnoreCase) ||
                        host.EndsWith("." + blocked, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
                return false;
            }
            finally
            {
                _blockedDomainsLock.Release();
            }
        }

        private async Task HandleClientAsync(TcpClient client)
        {
            using (client)
            {
                try
                {
                    client.NoDelay = true;
                    client.ReceiveBufferSize = 524288; // 512KB
                    client.SendBufferSize = 524288; // 512KB
                    
                    var stream = client.GetStream();
                    stream.ReadTimeout = 30000;
                    stream.WriteTimeout = 30000;
                    
                    var buffer = ArrayPool<byte>.Shared.Rent(262144); // 256KB

                    try
                    {
                        var bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, _cts.Token);
                        if (bytesRead == 0) return;

                        var request = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                        
                        if (request.StartsWith("CONNECT"))
                        {
                            await HandleConnectRequestAsync(client, stream, request, buffer);
                        }
                        else
                        {
                            await HandleHttpRequestAsync(client, stream, request, buffer, bytesRead);
                        }
                    }
                    finally
                    {
                        ArrayPool<byte>.Shared.Return(buffer);
                    }
                }
                catch (Exception ex)
                {
                    SafePrint($"Error handling client: {ex.Message}");
                }
            }
        }

        private async Task HandleConnectRequestAsync(TcpClient client, NetworkStream clientStream, string request, byte[] buffer)
        {
            var firstLine = request.Split('\n')[0];
            var parts = firstLine.Split(' ');
            
            if (parts.Length < 2) return;

            var target = parts[1];
            var targetParts = target.Split(':');
            
            if (targetParts.Length != 2) return;

            var targetHost = targetParts[0];
            if (!int.TryParse(targetParts[1], out var targetPort)) return;

            var originalHost = targetHost;
            targetHost = await ApplyRedirectAsync(targetHost);

            if (await IsBlockedAsync(targetHost))
            {
                var response = Encoding.UTF8.GetBytes(
                    "HTTP/1.1 403 Forbidden\r\n" +
                    "Content-Type: text/html\r\n" +
                    "Connection: close\r\n" +
                    "\r\n" +
                    "<html><body><h1>403 Forbidden</h1><p>Access Denied by XProxy</p></body></html>"
                );
                await clientStream.WriteAsync(response, 0, response.Length);
                SafePrint($"Blocked HTTPS: {targetHost}");
                return;
            }

            using (var remoteClient = new TcpClient())
            {
                try
                {
                    remoteClient.NoDelay = true;
                    remoteClient.ReceiveBufferSize = 524288; // 512KB
                    remoteClient.SendBufferSize = 524288; // 512KB

                    await remoteClient.ConnectAsync(targetHost, targetPort);
                    var remoteStream = remoteClient.GetStream();
                    remoteStream.ReadTimeout = 30000;
                    remoteStream.WriteTimeout = 30000;

                    var response = Encoding.UTF8.GetBytes("HTTP/1.1 200 Connection established\r\n\r\n");
                    await clientStream.WriteAsync(response, 0, response.Length);
                    
                    if (originalHost != targetHost)
                    {
                        SafePrint($"HTTPS (redirected): {originalHost} -> {targetHost}:{targetPort}");
                    }
                    else
                    {
                        SafePrint($"HTTPS: {targetHost}:{targetPort}");
                    }

                    var task1 = ForwardDataAsync(clientStream, remoteStream, "client->remote");
                    var task2 = ForwardDataAsync(remoteStream, clientStream, "remote->client");

                    await Task.WhenAny(task1, task2);
                }
                catch (Exception ex)
                {
                    SafePrint($"HTTPS Connect error for {targetHost}: {ex.Message}");
                }
            }
        }

        private async Task HandleHttpRequestAsync(TcpClient client, NetworkStream clientStream, string request, byte[] buffer, int bytesRead)
        {
            string host = null;
            var lines = request.Split(new[] { "\r\n" }, StringSplitOptions.None);
            
            foreach (var line in lines)
            {
                if (line.StartsWith("Host:", StringComparison.OrdinalIgnoreCase))
                {
                    host = line.Substring(5).Trim();
                    break;
                }
            }

            if (string.IsNullOrEmpty(host)) return;

            var port = 80;
            if (host.Contains(":"))
            {
                var hostParts = host.Split(':');
                host = hostParts[0];
                if (!int.TryParse(hostParts[1], out port)) return;
            }

            var originalHost = host;
            host = await ApplyRedirectAsync(host);

            if (await IsBlockedAsync(host))
            {
                var response = Encoding.UTF8.GetBytes(
                    "HTTP/1.1 403 Forbidden\r\n" +
                    "Content-Type: text/html\r\n" +
                    "Connection: close\r\n" +
                    "\r\n" +
                    "<html><body><h1>403 Forbidden</h1><p>Access Denied by XProxyV1</p></body></html>"
                );
                await clientStream.WriteAsync(response, 0, response.Length);
                SafePrint($"Blocked HTTP: {host}");
                return;
            }

            if (host != originalHost)
            {
                request = request.Replace($"Host: {originalHost}", $"Host: {host}");
                var modifiedBytes = Encoding.UTF8.GetBytes(request);
                Array.Copy(modifiedBytes, buffer, Math.Min(modifiedBytes.Length, buffer.Length));
                bytesRead = Math.Min(modifiedBytes.Length, buffer.Length);
            }

            using (var remoteClient = new TcpClient())
            {
                try
                {
                    remoteClient.NoDelay = true;
                    remoteClient.ReceiveBufferSize = 524288; // 512KB
                    remoteClient.SendBufferSize = 524288; // 512KB

                    await remoteClient.ConnectAsync(host, port);
                    var remoteStream = remoteClient.GetStream();
                    remoteStream.ReadTimeout = 30000;
                    remoteStream.WriteTimeout = 30000;

                    await remoteStream.WriteAsync(buffer, 0, bytesRead);
                    
                    if (originalHost != host)
                    {
                        SafePrint($"HTTP (redirected): {originalHost} -> {host}:{port}");
                    }
                    else
                    {
                        SafePrint($"HTTP: {host}:{port}");
                    }

                    await ForwardDataAsync(remoteStream, clientStream, "http");
                }
                catch (Exception ex)
                {
                    SafePrint($"HTTP error for {host}: {ex.Message}");
                }
            }
        }

        private async Task ForwardDataAsync(NetworkStream source, NetworkStream destination, string direction)
        {
            var buffer = ArrayPool<byte>.Shared.Rent(262144); // 256KB buffer
            try
            {
                int bytesRead;
                while ((bytesRead = await source.ReadAsync(buffer, 0, buffer.Length, _cts.Token)) > 0)
                {
                    await destination.WriteAsync(buffer, 0, bytesRead, _cts.Token);
                    await destination.FlushAsync(_cts.Token);
                }
            }
            catch
            {
                // yeah connection closed prob
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        private static readonly object _printLock = new object();
        private static void SafePrint(string message)
        {
            lock (_printLock)
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {message}");
            }
        }
    }
}
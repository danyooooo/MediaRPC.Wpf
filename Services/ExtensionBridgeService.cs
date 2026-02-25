using System;
using System.Collections.Generic;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MediaRPC.Services;

/// <summary>
/// Defines messages received from the extension.
/// </summary>
public record ExtensionMessage(string type, JsonElement data);

/// <summary>
/// Service that listens for messages from the browser extension via WebSockets.
/// </summary>
public class ExtensionBridgeService : IDisposable
{
    private const string ServerUrl = "http://127.0.0.1:8765/";
    private readonly HttpListener _listener = new();
    private readonly CancellationTokenSource _cts = new();
    private Task? _serverTask;

    private readonly List<WebSocket> _clients = new();
    private readonly object _clientsLock = new();

    public event EventHandler<ExtensionMessage>? MessageReceived;
    public event EventHandler<int>? ClientsCountChanged;
    public void StartListening()
    {
        try
        {
            _listener.Prefixes.Add(ServerUrl);
            _listener.Start();
            _serverTask = Task.Run(ListenForConnectionsAsync, _cts.Token);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to start WebSocket server: {ex.Message}");
        }
    }

    private async Task ListenForConnectionsAsync()
    {
        try
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                var context = await _listener.GetContextAsync();
                if (context.Request.IsWebSocketRequest)
                {
                    var wsContext = await context.AcceptWebSocketAsync(null);
                    _ = HandleWebSocketAsync(wsContext.WebSocket);
                }
                else
                {
                    context.Response.StatusCode = 400;
                    context.Response.Close();
                }
            }
        }
        catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException)
        {
            // Expected during shutdown
        }
    }

    private async Task HandleWebSocketAsync(WebSocket webSocket)
    {
        int count;
        lock (_clientsLock)
        {
            _clients.Add(webSocket);
            count = _clients.Count;
        }
        ClientsCountChanged?.Invoke(this, count);
        var buffer = new byte[1024 * 64]; // 64KB buffer for large payloads like base64 artwork
        
        try
        {
            while (webSocket.State == WebSocketState.Open && !_cts.Token.IsCancellationRequested)
            {
                var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), _cts.Token);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
                    break;
                }
                
                if (result.MessageType == WebSocketMessageType.Text)
                {
                    var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    try
                    {
                        var msg = JsonSerializer.Deserialize<ExtensionMessage>(json);
                        if (msg != null)
                        {
                            MessageReceived?.Invoke(this, msg);
                        }
                    }
                    catch
                    {
                        // Ignore malformed JSON
                    }
                }
            }
        }
        catch (WebSocketException)
        {
            // Disconnected
        }
        finally
        {
            lock (_clientsLock)
            {
                _clients.Remove(webSocket);
                count = _clients.Count;
            }
            ClientsCountChanged?.Invoke(this, count);
            webSocket.Dispose();
        }
    }

    public async Task BroadcastAsync(object payload)
    {
        var json = JsonSerializer.Serialize(payload);
        var bytes = Encoding.UTF8.GetBytes(json);
        var segment = new ArraySegment<byte>(bytes);

        WebSocket[] clientsSnapshot;
        lock (_clientsLock) clientsSnapshot = _clients.ToArray();

        foreach (var client in clientsSnapshot)
        {
            if (client.State == WebSocketState.Open)
            {
                try
                {
                    await client.SendAsync(segment, WebSocketMessageType.Text, true, CancellationToken.None);
                }
                catch { }
            }
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _listener.Stop(); } catch { }
        _listener.Close();
        
        lock (_clientsLock)
        {
            foreach (var client in _clients)
            {
                client.Dispose();
            }
            _clients.Clear();
        }

        _cts.Dispose();
        _serverTask?.Wait(1000);
        GC.SuppressFinalize(this);
    }
}

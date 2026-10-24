using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace Loupedeck.YamahaDirectPlugin;

internal static class WorkspaceVisibility
{
    internal static async Task<bool> IsVisible(CancellationToken stopping, string workspaceId)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stopping);
        timeout.CancelAfter(1500);
        var ct = timeout.Token;
        using var ws = new ClientWebSocket();
        var portFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Logi/LogiPluginService/Temp/WebSocketPort.txt");
        var port = int.Parse((await File.ReadAllTextAsync(portFile, ct)).Trim());
        await ws.ConnectAsync(new Uri($"ws://localhost:{port}/configui3"), ct);
        await ws.SendAsync(Encoding.UTF8.GetBytes("{\"id\":1,\"name\":\"GetServiceState\",\"parameters\":{}}"), WebSocketMessageType.Text, true, ct);
        var buffer = new byte[16384];
        using var bytes = new MemoryStream();
        while (true)
        {
            bytes.SetLength(0);
            WebSocketReceiveResult result;
            do
            {
                result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                if (result.MessageType == WebSocketMessageType.Close) return false;
                bytes.Write(buffer, 0, result.Count);
                if (bytes.Length > 2_000_000) return false;
            } while (!result.EndOfMessage);
            using var json = JsonDocument.Parse(bytes.ToArray());
            if (!json.RootElement.TryGetProperty("id", out var id) || id.GetInt32() != 1) continue;
            if (!json.RootElement.TryGetProperty("data", out var data) || !data.TryGetProperty("deviceStates", out var devices)) return false;
            return devices.EnumerateArray().Any(d => d.TryGetProperty("deviceType", out var type) && type.GetString() == "Loupedeck20" && d.TryGetProperty("workspaceName", out var workspace) && workspace.GetString() == workspaceId);
        }
    }
}

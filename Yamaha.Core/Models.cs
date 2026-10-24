using System.Net;
using System.Xml.Linq;

namespace Yamaha.Core;

public record ReceiverSettings
{
    public string Host { get; set; } = "";
    public string SystemId { get; set; } = "";
    public string Model { get; set; } = "R-N301";
    public string WorkspaceId { get; set; } = "F871B198F2CB522CBD3A9FCCB35136BB";
    public string[] Stations { get; set; } = ["Sky Plus", "Raadio 2", "Raadio Kuku", "Vikerraadio", "Power Hit Radio", "Raadio Elmar", "NRJ", "Star FM"];
    public int MaxVolume { get; set; } = 100;
    public string LastStation { get; set; } = "";
}

public record ReceiverState(bool Available = false, bool Power = false, int Volume = 0,
    bool Muted = false, string Input = "", string Playback = "Stop", string Title = "",
    string Station = "", string Shuffle = "Off", string Repeat = "Off", int Preset = 0,
    string Error = "Connecting…");
public record InputItem(string Id, string Name, string Tag);
public record PresetItem(int Id, string Name);
public record ListItem(int Index, string Name, string Kind);
public record ReceiverList(int Layer, string Name, int Current, int Total, string Status, ListItem[] Items);

public class ReceiverException(string message) : Exception(message);

public interface IYamahaTransport : IDisposable
{
    Task<XElement> RequestAsync(string host, XElement request, CancellationToken cancellationToken);
}

public sealed class HttpYamahaTransport : IYamahaTransport
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(5) };
    public async Task<XElement> RequestAsync(string host, XElement request, CancellationToken ct)
    {
        if (!IPAddress.TryParse(host, out var ip) || ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
            throw new ReceiverException("Receiver IPv4 address is not configured");
        using var body = new StringContent(request.ToString(SaveOptions.DisableFormatting), System.Text.Encoding.UTF8, "text/xml");
        using var response = await _http.PostAsync($"http://{ip}/YamahaRemoteControl/ctrl", body, ct);
        if (!response.IsSuccessStatusCode) throw new ReceiverException($"Receiver HTTP {(int)response.StatusCode}");
        var xml = await response.Content.ReadAsStringAsync(ct);
        // No DTDs or external entities from the LAN.
        using var reader = System.Xml.XmlReader.Create(new StringReader(xml), new() { DtdProcessing = System.Xml.DtdProcessing.Prohibit, XmlResolver = null });
        return XElement.Load(reader);
    }
    public void Dispose() => _http.Dispose();
}

public static class XmlValue
{
    public static string Value(this XElement e, params string[] path)
    {
        XElement? current = e;
        foreach (var part in path) current = current?.Element(part);
        return current?.Value ?? "";
    }
    public static int Number(this XElement e, params string[] path) => int.TryParse(e.Value(path), out var n) ? n : 0;
    public static string Clean(string text)
    {
        text = WebUtility.HtmlDecode(text);
        try
        {
            if (text.Contains('Ã') || text.Contains('Â'))
            {
                var bytes = System.Text.Encoding.Latin1.GetBytes(text);
                var fixedText = new System.Text.UTF8Encoding(false, true).GetString(bytes);
                if (!fixedText.Contains('\uFFFD')) text = fixedText;
            }
        }
        catch (System.Text.DecoderFallbackException) { }
        return text;
    }
}

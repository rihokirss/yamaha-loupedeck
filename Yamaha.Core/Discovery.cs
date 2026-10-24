using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Xml.Linq;

namespace Yamaha.Core;

public static class Discovery
{
    public static async Task<string?> FindAsync(ReceiverSettings settings, CancellationToken ct)
    {
        using var udp = new UdpClient();
        var query = Encoding.ASCII.GetBytes("M-SEARCH * HTTP/1.1\r\nHOST: 239.255.255.250:1900\r\nMAN: \"ssdp:discover\"\r\nMX: 2\r\nST: urn:schemas-upnp-org:device:MediaRenderer:1\r\n\r\n");
        await udp.SendAsync(query, new IPEndPoint(IPAddress.Parse("239.255.255.250"), 1900), ct);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(4));
        var candidates = new HashSet<string>();
        try
        {
            while (!timeout.IsCancellationRequested)
            {
                var packet = await udp.ReceiveAsync(timeout.Token);
                candidates.Add(packet.RemoteEndPoint.Address.ToString());
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
        using var transport = new HttpYamahaTransport();
        var matches = new List<string>();
        foreach (var host in candidates)
        {
            try
            {
                var response = await transport.RequestAsync(host, new XElement("YAMAHA_AV", new XAttribute("cmd", "GET"), new XElement("System", new XElement("Config", "GetParam"))), ct);
                if (Matches(settings, response)) matches.Add(host);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested) { }
        }
        // Never silently pick the first of multiple R-N301 receivers.
        return matches.Count == 1 ? matches[0] : null;
    }

    public static bool Matches(ReceiverSettings s, XElement response) =>
        response.Attribute("RC")?.Value == "0" && response.Value("System", "Config", "Model_Name") == s.Model &&
        (s.SystemId.Length == 0 || string.Equals(response.Value("System", "Config", "System_ID"), s.SystemId, StringComparison.OrdinalIgnoreCase));
}

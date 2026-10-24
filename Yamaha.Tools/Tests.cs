using System.Xml.Linq;
using Yamaha.Core;

internal static class Tests
{
    private static int _passed;
    private static void Assert(bool ok, string message) { if (!ok) throw new Exception("FAIL " + message); Console.WriteLine("PASS " + message); _passed++; }
    private static Receiver Make(FakeTransport transport) => new(new ReceiverSettings { Host = "127.0.0.1", SystemId = "TEST" }, transport);
    public static async Task Run()
    {
        var f = new FakeTransport(); using var r = Make(f); await r.RefreshAsync();
        Assert(r.State.Available && r.Inputs.Length == 6 && r.Presets.Length == 2, "identity, inputs and actual stored presets");
        await r.SetVolumeAsync(-2); Assert(f.Volume == 0, "volume lower bound");
        await r.SetVolumeAsync(150); Assert(f.Volume == 100, "volume upper bound");
        r.Settings.MaxVolume = 35; await r.SetVolumeAsync(70); Assert(f.Volume == 35, "configured volume ceiling");
        f.Power = false; await r.RefreshAsync(); var writes = f.Writes.Count;
        try { await r.SetVolumeAsync(20); } catch (ReceiverException) { }
        Assert(f.Writes.Count == writes && !f.Power, "volume never powers on standby receiver");
        await r.PlayStationAsync("Raadio Kuku"); Assert(r.State.Power && r.State.Station == "Raadio Kuku", "radio powers on, waits for busy menu, selects by name");
        await r.StepAsync(1); Assert(r.State.Station == "Vikerraadio", "next radio favorite");
        await r.StepAsync(-1); Assert(r.State.Station == "Raadio Kuku", "previous radio favorite");
        await r.PlaybackAsync("stop"); await r.PlaybackAsync(); Assert(r.State.Station == "Raadio Kuku" && r.State.Playback == "Play", "radio resumes by reselecting favorite");
        await r.SelectPresetAsync(4); await r.StepAsync(1); Assert(r.State.Preset == 1, "FM wraps actual preset IDs, not assumed consecutive slots");
        await r.SelectInputAsync("OPTICAL"); var before = f.Writes.Count;
        try { await r.PlaybackAsync(); } catch (ReceiverException) { }
        Assert(f.Writes.Count == before, "passive input rejects playback locally");
        await r.SelectInputAsync("NET RADIO"); f.Volume = 14; await r.RefreshAsync();
        using (var pump = new VolumePump(r))
        {
            r.Settings.MaxVolume = 100; f.VolumeTimes.Clear();
            var feedback = 0; pump.Changed += () => feedback++;
            for (var n = 0; n < 20; n++) pump.Adjust(1);
            Assert(pump.PendingTarget == 34 && feedback >= 20 && f.Volume == 14, "volume feedback updates immediately before receiver round-trip");
            await pump.DrainAsync();
            Assert(f.Volume == 34 && f.VolumeTimes.Count == 1, "rapid ticks combine into one accurate target");
            for (var n = 0; n < 12; n++) { pump.Adjust(-1); await Task.Delay(60); }
            await pump.DrainAsync();
            Assert(f.Volume == 22 && f.VolumeTimes.Zip(f.VolumeTimes.Skip(1)).All(t => (t.Second-t.First).TotalMilliseconds >= 180), "volume requests stay at most five per second");
            f.FailNext = true; pump.Adjust(3); await pump.DrainAsync(); var count = f.Writes.Count;
            Assert(!r.State.Available && !pump.Adjust(5), "outage disables and discards pending volume");
            await r.RefreshAsync(); await Task.Delay(450); Assert(f.Writes.Count == count, "reconnection never replays old volume commands");
        }
        var race = new FakeTransport { RaceOnSelection = true }; using var rr = Make(race); await rr.RefreshAsync();
        try { await rr.PlayStationAsync("Sky Plus"); } catch (ReceiverException) { }
        Assert(!race.Writes.Any(x => x.Contains("Direct_Sel")), "menu revalidation prevents wrong selection after external navigation");
        var wrong = new XElement("YAMAHA_AV", new XAttribute("RC", "0"), new XElement("System", new XElement("Config", new XElement("Model_Name", "RX-V4A"), new XElement("System_ID", "TEST"))));
        Assert(!Discovery.Matches(r.Settings, wrong), "discovery excludes RX-V4A");
        wrong.Element("System")!.Element("Config")!.Element("Model_Name")!.Value = "R-N301";
        wrong.Element("System")!.Element("Config")!.Element("System_ID")!.Value = "OTHER";
        Assert(!Discovery.Matches(r.Settings, wrong), "discovery excludes another R-N301 identity");
        Assert(f.MaxConcurrency == 1, "receiver operations serialize requests");
        Assert(XmlValue.Clean("PÃ¤ev") == "Päev" && XmlValue.Clean("Päev") == "Päev", "metadata encoding repair preserves good Estonian text");
        Console.WriteLine($"{_passed}/{_passed} checks passed");
    }
}

internal sealed class FakeTransport : IYamahaTransport
{
    public bool Power = true, Muted, FailNext, RaceOnSelection;
    public int Volume = 14, Preset = 1, Layer = 1, Busy, Reads, MaxConcurrency;
    private int _concurrent;
    public string Input = "NET RADIO", Station = "Sky Plus", Playback = "Play";
    public List<string> Writes = [];
    public List<DateTime> VolumeTimes = [];
    private readonly string[] _stations = new ReceiverSettings().Stations;
    public async Task<XElement> RequestAsync(string host, XElement request, CancellationToken ct)
    {
        var concurrent = Interlocked.Increment(ref _concurrent); MaxConcurrency = Math.Max(MaxConcurrency, concurrent);
        try
        {
            await Task.Delay(2, ct);
            if (FailNext) { FailNext = false; throw new HttpRequestException("Simulated network outage"); }
            var put = request.Attribute("cmd")!.Value == "PUT";
            XElement Wrap(XElement payload, string rc = "0") => new("YAMAHA_AV", new XAttribute("RC", rc), payload);
            if (put)
            {
                Writes.Add(request.ToString());
                var v = request.Descendants("Power").FirstOrDefault(); if (v != null) Power = v.Value == "On";
                v = request.Descendants("Input_Sel").FirstOrDefault(); if (v != null) Input = v.Value;
                v = request.Descendants("Val").FirstOrDefault(); if (v != null) { Volume = int.Parse(v.Value); VolumeTimes.Add(DateTime.UtcNow); }
                v = request.Descendants("Mute").FirstOrDefault(); if (v != null) Muted = v.Value == "On";
                v = request.Descendants("Playback").FirstOrDefault(); if (v != null) Playback = v.Value;
                v = request.Descendants("Preset_Sel").FirstOrDefault(); if (v != null) Preset = int.Parse(v.Value);
                v = request.Descendants("Cursor").FirstOrDefault(); if (v != null) Layer = Math.Max(1, Layer - 1);
                v = request.Descendants("Direct_Sel").FirstOrDefault();
                if (v != null)
                {
                    var line = int.Parse(v.Value[5..]);
                    if (Layer < 3) { Layer++; Busy = 1; }
                    else { Station = _stations[line - 1]; Playback = "Play"; }
                }
                return Wrap(new XElement("OK"));
            }
            if (request.Descendants("Config").Any()) return XElement.Parse("<YAMAHA_AV RC='0'><System><Config><Model_Name>R-N301</Model_Name><System_ID>TEST</System_ID></Config></System></YAMAHA_AV>");
            if (request.Descendants("Input_Sel_Item").Any())
            {
                var items = new[] { "NET RADIO", "TUNER", "SERVER", "Spotify", "AirPlay", "OPTICAL" }.Select((i, n) => new XElement("Item_" + n, new XElement("Param", i), new XElement("Title", i), new XElement("Src_Name", i == "NET RADIO" ? "NET_RADIO" : i == "TUNER" ? "Tuner" : i == "OPTICAL" ? "" : i)));
                return Wrap(new XElement("Main_Zone", new XElement("Input", new XElement("Input_Sel_Item", items))));
            }
            if (request.Descendants("Preset_Sel_Item").Any()) return XElement.Parse("<YAMAHA_AV RC='0'><Tuner><Play_Control><Preset><Preset_Sel_Item><Item_1><Param>1</Param><Title>FM 1</Title></Item_1><Item_2><Param>4</Param><Title>FM 4</Title></Item_2></Preset_Sel_Item></Preset></Play_Control></Tuner></YAMAHA_AV>");
            if (request.Descendants("Basic_Status").Any()) return XElement.Parse($"<YAMAHA_AV RC='0'><Main_Zone><Basic_Status><Power_Control><Power>{(Power ? "On" : "Standby")}</Power></Power_Control><Volume><Lvl><Val>{Volume}</Val></Lvl><Mute>{(Muted ? "On" : "Off")}</Mute></Volume><Input><Input_Sel>{Input}</Input_Sel></Input></Basic_Status></Main_Zone></YAMAHA_AV>");
            if (request.Descendants("List_Info").Any())
            {
                Reads++;
                var layer = RaceOnSelection && Reads > 1 ? 9 : Layer;
                var names = layer == 1 ? new[] { "Radiobrowser", "My Stations" } : layer == 2 ? new[] { "Bookmarks" } : _stations;
                return Wrap(new XElement("NET_RADIO", new XElement("List_Info", new XElement("Menu_Status", Busy-- > 0 ? "Busy" : "Ready"), new XElement("Menu_Layer", layer), new XElement("Menu_Name", layer == 1 ? "NET RADIO" : layer == 2 ? "My Stations" : "Bookmarks"), new XElement("Cursor_Position", new XElement("Current_Line", 1), new XElement("Max_Line", names.Length)), new XElement("Current_List", names.Select((n, i) => new XElement("Line_" + (i + 1), new XElement("Txt", n), new XElement("Attribute", layer < 3 ? "Container" : "Item")))))));
            }
            if (request.Descendants("Play_Info").Any())
            {
                var tag = request.Elements().First().Name;
                return Wrap(new XElement(tag, new XElement("Play_Info", new XElement("Playback_Info", Playback), new XElement("Preset", new XElement("Preset_Sel", Preset)), new XElement("Meta_Info", new XElement("Station", Station), new XElement("Song", "Test Song")))));
            }
            throw new Exception("Unknown fake request: " + request);
        }
        finally { Interlocked.Decrement(ref _concurrent); }
    }
    public void Dispose() { }
}

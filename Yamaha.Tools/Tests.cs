using System.Xml.Linq;
using Yamaha.Core;

internal static class Tests
{
    private static int _passed;
    internal static void Assert(bool ok, string message) { if (!ok) throw new Exception("FAIL " + message); Console.WriteLine("PASS " + message); _passed++; }
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
        TestSourceControls();
        await TestContextExecution();
        await RdsTests.Run();
        Console.WriteLine($"{_passed}/{_passed} checks passed");
    }

    private static void TestSourceControls()
    {
        var settings = new ReceiverSettings();
        PresetItem[] presets = [new(1, "1 : FM 95.40 MHz"), new(4, "4 : FM 101,6 MHz")];
        var state = new ReceiverState(Available: true, Power: true, Input: "NET RADIO", Station: "Raadio Kuku", Playback: "Play");
        var radio = SourceControls.Slots(state, settings, presets);
        Assert(radio.Length == 8 && radio.Select(a => a.Label).SequenceEqual(settings.Stations) && radio.Single(a => a.Active).Label == "Raadio Kuku", "NET RADIO slots use configured favorite order and live highlight");
        Assert(SourceControls.Menu(state, settings, presets).All(a => !a.Id.StartsWith("preset:")), "NET RADIO menu contains no FM presets");
        var fm = SourceControls.Slots(state with { Input = "TUNER", Preset = 4 }, settings, presets);
        Assert(fm[0].Id == "preset:1" && fm[1].Id == "preset:4" && fm[1].Active && fm[0].Detail == "95.4" && fm[1].Detail == "101.6" && fm.Skip(2).All(a => !a.Enabled), "TUNER uses actual IDs, normalized frequency labels and blank unused slots");
        Assert(SourceControls.Menu(state with { Input = "TUNER" }, settings, presets).Length == 2, "TUNER menu contains only actual stored FM presets");
        var server = SourceControls.Slots(state with { Input = "SERVER", Shuffle = "On", Repeat = "One" }, settings, presets);
        Assert(server.Take(6).Select(a => a.Id).SequenceEqual(new[] { "previous", "play", "next", "stop", "shuffle", "repeat" }) && server[4].Active && server[5].Label == "Korda ühte" && server.Skip(6).All(a => !a.Enabled), "SERVER has six contextual controls and two blank slots");
        foreach (var input in new[] { "Spotify", "AirPlay" })
        {
            var media = SourceControls.Slots(state with { Input = input }, settings, presets);
            Assert(media.Take(4).All(a => a.Enabled) && media.Skip(4).All(a => !a.Enabled) && SourceControls.Menu(state with { Input = input }, settings, presets).Length == 4, input + " has four transport controls, no FM or SERVER modes");
        }
        Assert(new[] { "CD", "OPTICAL", "COAXIAL", "LINE1", "LINE2", "LINE3" }.All(input => SourceControls.Slots(state with { Input = input }, settings, presets).All(a => !a.Enabled) && SourceControls.Menu(state with { Input = input }, settings, presets).Length == 0), "passive inputs have eight inactive slots and no media menu");
        Assert(SourceControls.Slots(state with { Available = false }, settings, presets).All(a => !a.Enabled) && SourceControls.Menu(state with { Available = false }, settings, presets).Length == 0, "offline blanks controls instead of retaining stale commands");
        Assert(SourceControls.Slots(state with { Power = false }, settings, presets).All(a => a.Enabled && !a.Active) && SourceControls.Slots(state with { Power = false, Input = "SERVER" }, settings, presets).All(a => !a.Enabled), "standby keeps radio wake shortcuts but disables transport");
        Assert(SourceControls.ResolveMenu(SourceControls.Menu(state, settings, presets)[0].Token, state with { Input = "Spotify" }, settings, presets) == null, "dynamic folder tokens cannot resolve on a different input");
    }

    private static async Task TestContextExecution()
    {
        var f = new FakeTransport(); using var r = Make(f); await r.RefreshAsync();
        var radio = SourceControls.Slots(r.State, r.Settings, r.Presets)[2];
        f.Input = "OPTICAL"; var count = f.Writes.Count; var cancelled = false;
        try { await r.ExecuteContextAsync(radio); } catch (ReceiverException) { cancelled = true; }
        Assert(cancelled && f.Writes.Count == count && r.State.Input == "OPTICAL", "stale favorite press re-reads actual external input and sends no PUT");
        f.Input = "TUNER"; await r.RefreshAsync();
        var preset = SourceControls.Slots(r.State, r.Settings, r.Presets)[1];
        f.Input = "NET RADIO"; count = f.Writes.Count; cancelled = false;
        try { await r.ExecuteContextAsync(preset); } catch (ReceiverException) { cancelled = true; }
        Assert(cancelled && f.Writes.Count == count, "stale FM press cannot switch a newly selected source back to TUNER");
        await r.ExecuteContextAsync(radio);
        Assert(r.State.Station == "Raadio Kuku" && r.State.Input == "NET RADIO", "valid context favorite executes by configured station name");
        f.Input = "TUNER"; await r.RefreshAsync(); count = f.Writes.Count;
        await r.ExecuteContextAsync(SourceControls.Slots(r.State, r.Settings, r.Presets)[1]);
        Assert(r.State.Preset == 4 && f.Writes.Skip(count).All(x => !x.Contains("Input_Sel")), "valid context FM selection uses actual preset ID without changing input");
        f.Input = "SERVER"; await r.RefreshAsync();
        await r.ExecuteContextAsync(SourceControls.Menu(r.State, r.Settings, r.Presets).Single(a => a.Id == "shuffle"));
        await r.ExecuteContextAsync(SourceControls.Menu(r.State, r.Settings, r.Presets).Single(a => a.Id == "repeat"));
        Assert(r.State.Shuffle == "On" && r.State.Repeat == "One", "SERVER context actions update live shuffle and repeat feedback");
        f.Power = false; await r.RefreshAsync(); count = f.Writes.Count;
        Assert(SourceControls.Slots(r.State, r.Settings, r.Presets).All(a => !a.Enabled) && f.Writes.Count == count, "standby context rendering does not issue receiver commands");
    }
}

internal sealed class FakeTransport : IYamahaTransport
{
    public bool Power = true, Muted, FailNext, RaceOnSelection;
    public int Volume = 14, Preset = 1, Layer = 1, Busy, Reads, MaxConcurrency;
    private int _concurrent;
    public string Input = "NET RADIO", Station = "Sky Plus", Playback = "Play", Shuffle = "Off", Repeat = "Off";
    public List<string> Writes = [];
    public List<DateTime> VolumeTimes = [];
    public XElement? TunerInfo;
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
                v = request.Descendants("Shuffle").FirstOrDefault(); if (v != null) Shuffle = v.Value;
                v = request.Descendants("Repeat").FirstOrDefault(); if (v != null) Repeat = v.Value;
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
                if (request.Element("SERVER") != null) return XElement.Parse("<YAMAHA_AV RC='0'><SERVER><List_Info><Menu_Status>Ready</Menu_Status><Menu_Layer>3</Menu_Layer><Menu_Name>Test Album</Menu_Name><Cursor_Position><Current_Line>1</Current_Line><Max_Line>1</Max_Line></Cursor_Position><Current_List><Line_1><Txt>Test Song</Txt><Attribute>Item</Attribute></Line_1></Current_List></List_Info></SERVER></YAMAHA_AV>");
                Reads++;
                var layer = RaceOnSelection && Reads > 1 ? 9 : Layer;
                var names = layer == 1 ? new[] { "Radiobrowser", "My Stations" } : layer == 2 ? new[] { "Bookmarks" } : _stations;
                return Wrap(new XElement("NET_RADIO", new XElement("List_Info", new XElement("Menu_Status", Busy-- > 0 ? "Busy" : "Ready"), new XElement("Menu_Layer", layer), new XElement("Menu_Name", layer == 1 ? "NET RADIO" : layer == 2 ? "My Stations" : "Bookmarks"), new XElement("Cursor_Position", new XElement("Current_Line", 1), new XElement("Max_Line", names.Length)), new XElement("Current_List", names.Select((n, i) => new XElement("Line_" + (i + 1), new XElement("Txt", n), new XElement("Attribute", layer < 3 ? "Container" : "Item")))))));
            }
            if (request.Descendants("Play_Info").Any())
            {
                var tag = request.Elements().First().Name;
                if (tag == "Tuner" && TunerInfo != null) return Wrap(new XElement(tag, new XElement(TunerInfo)));
                return Wrap(new XElement(tag, new XElement("Play_Info", new XElement("Playback_Info", Playback), new XElement("Play_Mode", new XElement("Shuffle", Shuffle), new XElement("Repeat", Repeat)), new XElement("Preset", new XElement("Preset_Sel", Preset)), new XElement("Meta_Info", new XElement("Station", Station), new XElement("Song", "Test Song")))));
            }
            throw new Exception("Unknown fake request: " + request);
        }
        finally { Interlocked.Decrement(ref _concurrent); }
    }
    public void Dispose() { }
}

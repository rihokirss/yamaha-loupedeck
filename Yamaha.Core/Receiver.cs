using System.Xml.Linq;

namespace Yamaha.Core;

/// <summary>All composite operations and polling share one receiver lock.</summary>
public sealed class Receiver : IDisposable
{
    private readonly IYamahaTransport _transport;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationToken _ct;
    private readonly TimeProvider _time;
    private readonly RdsStationLearner _rds = new();
    private DateTime _poweredAt;
    private bool _verified;
    private string? _lastStation;
    private (string Menu, string Track)? _lastServerTrack;
    public ReceiverSettings Settings { get; }
    public ReceiverState State { get; private set; } = new();
    public InputItem[] Inputs { get; private set; } = [];
    public PresetItem[] Presets { get; private set; } = [];
    public event Action? Changed;
    public event Action? AddressChanged;
    public event Action? SettingsChanged;
    public Receiver(ReceiverSettings settings, IYamahaTransport transport, CancellationToken ct = default, TimeProvider? timeProvider = null)
    { Settings = settings; _transport = transport; _ct = ct; _time = timeProvider ?? TimeProvider.System; _lastStation = settings.Stations.Contains(settings.LastStation) ? settings.LastStation : null; }

    private static XElement Path(string[] path, object value)
    {
        XElement node = new(path[^1], value);
        for (var i = path.Length - 2; i >= 0; --i) node = new XElement(path[i], node);
        return node;
    }
    private async Task<XElement> Send(bool put, string[] path, object value)
    {
        var request = new XElement("YAMAHA_AV", new XAttribute("cmd", put ? "PUT" : "GET"), Path(path, value));
        var retryUntil = _poweredAt.AddSeconds(3);
        while (true)
        {
            var result = await _transport.RequestAsync(Settings.Host, request, _ct);
            var rc = result.Attribute("RC")?.Value;
            if (rc == "0") return result;
            if (rc == "4" && put && DateTime.UtcNow < retryUntil)
            { await Task.Delay(200, _ct); continue; }
            throw new ReceiverException(rc switch { "3" => "Receiver refused this value", "4" => "Receiver is in standby", _ => $"Receiver error RC={rc ?? "missing"}" });
        }
    }
    private Task<XElement> Get(params string[] path) => Send(false, path, "GetParam");
    private Task<XElement> Put(string[] path, object value) => Send(true, path, value);
    private void Publish(ReceiverState s) { State = s; Changed?.Invoke(); }
    private async Task Run(Func<Task> action, bool requireOnline = true)
    {
        // Do not retain commands across an outage.
        if (requireOnline && !State.Available) throw new ReceiverException("Receiver offline; command discarded");
        await _gate.WaitAsync(_ct);
        try
        {
            if (requireOnline && !State.Available) throw new ReceiverException("Receiver offline; command discarded");
            await action();
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !_ct.IsCancellationRequested)
        {
            var offline = ex is HttpRequestException or OperationCanceledException or System.Net.Sockets.SocketException or System.Xml.XmlException;
            if (offline) { _verified = false; _rds.Reset(); }
            Publish(State with { Available = offline ? false : State.Available, Error = ex.Message });
            throw;
        }
        finally { _gate.Release(); }
    }

    public Task RefreshAsync() => Run(async () =>
    {
        if (!_verified)
        {
            var config = await Get("System", "Config");
            if (!Discovery.Matches(Settings, config)) throw new ReceiverException("Receiver identity mismatch: expected R-N301");
            if (Settings.SystemId.Length == 0) Settings.SystemId = config.Value("System", "Config", "System_ID");
            _verified = true;
            var inputXml = await Get("Main_Zone", "Input", "Input_Sel_Item");
            Inputs = inputXml.Element("Main_Zone")?.Element("Input")?.Element("Input_Sel_Item")?.Elements()
                .Select(e => new InputItem(e.Value("Param"), e.Value("Title"), e.Value("Src_Name"))).Where(e => e.Id.Length > 0).ToArray() ?? [];
            await ReadPresets();
        }
        await RefreshInternal();
    }, false);

    public async Task RediscoverAsync()
    {
        var host = await Discovery.FindAsync(Settings, _ct);
        if (host == null) return;
        await _gate.WaitAsync(_ct);
        try { Settings.Host = host; _verified = false; AddressChanged?.Invoke(); }
        finally { _gate.Release(); }
        await RefreshAsync();
    }

    private async Task ReadPresets()
    {
        var xml = await Get("Tuner", "Play_Control", "Preset", "Preset_Sel_Item");
        Presets = xml.Element("Tuner")?.Element("Play_Control")?.Element("Preset")?.Element("Preset_Sel_Item")?.Elements()
            .Where(e => int.TryParse(e.Value("Param"), out var n) && n > 0)
            .Select(e => new PresetItem(e.Number("Param"), e.Value("Title"))).ToArray() ?? [];
    }

    private async Task RefreshInternal()
    {
        var xml = await Get("Main_Zone", "Basic_Status");
        var basic = xml.Element("Main_Zone")?.Element("Basic_Status") ?? throw new ReceiverException("Basic_Status missing");
        var input = basic.Value("Input", "Input_Sel");
        if (input == "TUNER" && State.Input != "TUNER") await ReadPresets();
        var state = State with { Available = true, Power = basic.Value("Power_Control", "Power") == "On", Volume = basic.Number("Volume", "Lvl", "Val"), Muted = basic.Value("Volume", "Mute") == "On", Input = input, Error = "", Frequency = "" };
        if (!state.Power || input != "TUNER") _rds.Reset();
        if (state.Power && Tag(input) is { Length: > 0 } tag)
        {
            var info = (await Get(tag, "Play_Info")).Element(tag)?.Element("Play_Info");
            if (info != null)
            {
                var title = info.Value("Meta_Info", "Song");
                if (title.Length == 0) title = info.Value("Meta_Info", "Track");
                var artist = info.Value("Meta_Info", "Artist");
                var station = info.Value("Meta_Info", "Station");
                if (tag == "Tuner")
                {
                    var rds = Rds.Read(info);
                    if (_rds.Observe(rds, Settings, _time.GetUtcNow())) SettingsChanged?.Invoke();
                    station = rds.Station;
                    if (station.Length == 0 && rds.Tuned) station = Settings.FmStationNames?.GetValueOrDefault(rds.Frequency) ?? "";
                    if (station.Length == 0 && rds.Frequency.Length > 0) station = "FM " + rds.Frequency + " MHz";
                    title = rds.Text; artist = "";
                    state = state with { Frequency = rds.Frequency };
                }
                state = state with { Playback = info.Value("Playback_Info"), Title = XmlValue.Clean(artist.Length > 0 ? artist + " – " + title : title), Station = XmlValue.Clean(station), Preset = info.Number("Preset", "Preset_Sel"), Shuffle = info.Value("Play_Mode", "Shuffle"), Repeat = info.Value("Play_Mode", "Repeat") };
                if (input == "NET RADIO" && station.Length > 0 && Settings.Stations.Contains(station)) { _lastStation = station; Settings.LastStation = station; }
                if (input == "SERVER" && title.Length > 0)
                {
                    var list = await ReadList("SERVER");
                    _lastServerTrack = (list.Name, XmlValue.Clean(title));
                }
            }
            else { _rds.Reset(); state = state with { Playback = "Stop", Title = "", Station = "" }; }
        }
        else state = state with { Playback = "Stop", Title = "", Station = "" };
        Publish(state);
    }
    public string Tag(string input) => Inputs.FirstOrDefault(i => i.Id == input)?.Tag ?? input switch { "NET RADIO" => "NET_RADIO", "TUNER" => "Tuner", "SERVER" => "SERVER", "Spotify" => "Spotify", "AirPlay" => "AirPlay", _ => "" };
    private async Task PowerOn()
    {
        if (State.Power) return;
        await Put(["System", "Power_Control", "Power"], "On");
        _poweredAt = DateTime.UtcNow;
        // RC=0 on Input_Sel can still be ignored during boot.
        await Task.Delay(650, _ct);
        await WaitForPower(true);
    }
    public Task TogglePowerAsync() => Run(async () =>
    {
        if (State.Power) { await Put(["System", "Power_Control", "Power"], "Standby"); await WaitForPower(false); }
        else await PowerOn();
    });
    private async Task WaitForPower(bool power)
    {
        var deadline = DateTime.UtcNow.AddSeconds(3);
        do { await RefreshInternal(); if (State.Power == power) return; await Task.Delay(150, _ct); } while (DateTime.UtcNow < deadline);
        throw new ReceiverException(power ? "Receiver did not power on" : "Receiver did not enter standby");
    }
    private void RequirePower() { if (!State.Power) throw new ReceiverException("Receiver is in standby"); }
    public Task SetVolumeAsync(int target) => Run(async () =>
    {
        RequirePower();
        var volume = Math.Clamp(target, 0, Math.Clamp(Settings.MaxVolume, 0, 100));
        await Put(["Main_Zone", "Volume", "Lvl"], new object[] { new XElement("Val", volume), new XElement("Exp", 0), new XElement("Unit", "") });
        await RefreshInternal();
    });
    public Task ToggleMuteAsync() => Run(async () =>
    { RequirePower(); await Put(["Main_Zone", "Volume", "Mute"], State.Muted ? "Off" : "On"); await RefreshInternal(); });
    private async Task SelectInput(string input)
    {
        if (!Inputs.Any(i => i.Id == input)) throw new ReceiverException("Unknown input");
        await PowerOn();
        if (State.Input == input) return;
        await Put(["Main_Zone", "Input", "Input_Sel"], input);
        var until = DateTime.UtcNow.AddSeconds(3);
        do { await RefreshInternal(); if (State.Input == input) return; await Task.Delay(150, _ct); } while (DateTime.UtcNow < until);
        throw new ReceiverException("Input did not change");
    }
    public Task SelectInputAsync(string input) => Run(() => SelectInput(input));

    public Task PlayStationAsync(string name) => Run(() => PlayStation(name));
    private async Task PlayStation(string name)
    {
        if (!Settings.Stations.Contains(name)) throw new ReceiverException("Unknown radio favorite");
        await SelectInput("NET RADIO");
        var list = await ReadyList("NET_RADIO");
        // Return to the known root: a previous client may have moved the cursor.
        for (var depth = 0; list.Layer > 1 && depth < 12; depth++)
        {
            var oldLayer = list.Layer;
            await Put(["NET_RADIO", "List_Control", "Cursor"], "Return");
            list = await WaitList("NET_RADIO", l => l.Layer < oldLayer);
        }
        if (list.Layer != 1) throw new ReceiverException("Cannot find NET RADIO root");
        list = await EnterContainer("NET_RADIO", list, "My Stations");
        // YCast may contain the extra Bookmarks container, or expose its items directly.
        for (var depth = 0; depth < 6 && !list.Items.Any(i => i.Kind == "Item"); depth++)
        {
            var folder = list.Items.FirstOrDefault(i => i.Kind == "Container" && i.Name.Equals("Bookmarks", StringComparison.OrdinalIgnoreCase))
                ?? (list.Total == 1 ? list.Items.FirstOrDefault(i => i.Kind == "Container") : null);
            if (folder == null) break;
            list = await EnterContainer("NET_RADIO", list, folder.Name);
        }
        await SelectNamedItem("NET_RADIO", list, name);
        _lastStation = name;
        Settings.LastStation = name;
        await Task.Delay(600, _ct);
        var deadline = DateTime.UtcNow.AddSeconds(5);
        var confirmed = 0;
        do
        {
            await Task.Delay(200, _ct); await RefreshInternal();
            if (State.Station == name && State.Playback == "Play") { if (++confirmed >= 2) return; }
            else confirmed = 0;
        } while (DateTime.UtcNow < deadline);
        throw new ReceiverException($"Station not confirmed: {name}");
    }

    private async Task<ReceiverList> ReadList(string tag)
    {
        var xml = (await Get(tag, "List_Info")).Element(tag)?.Element("List_Info") ?? throw new ReceiverException("List_Info missing");
        var current = xml.Number("Cursor_Position", "Current_Line");
        var start = Math.Max(0, (current - 1) / 8) * 8;
        var items = xml.Element("Current_List")?.Elements().Select((e, i) => new ListItem(start + i + 1, XmlValue.Clean(e.Value("Txt")), e.Value("Attribute"))).Where(e => e.Kind != "Unselectable" && e.Name.Length > 0).ToArray() ?? [];
        return new(xml.Number("Menu_Layer"), XmlValue.Clean(xml.Value("Menu_Name")), current, xml.Number("Cursor_Position", "Max_Line"), xml.Value("Menu_Status"), items);
    }
    private Task<ReceiverList> ReadyList(string tag) => WaitList(tag, _ => true);
    private async Task<ReceiverList> WaitList(string tag, Func<ReceiverList, bool> accept)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        do { var list = await ReadList(tag); if (list.Status == "Ready" && accept(list)) return list; await Task.Delay(150, _ct); } while (DateTime.UtcNow < deadline);
        throw new ReceiverException("Receiver menu did not finish loading");
    }
    private async Task<(ReceiverList List, ListItem Item)> Find(string tag, ReceiverList list, string name, string kind)
    {
        var menuName = list.Name; var layer = list.Layer;
        for (var start = 1; start <= Math.Max(1, list.Total); start += 8)
        {
            if ((list.Current - 1) / 8 != (start - 1) / 8)
            {
                await Put([tag, "List_Control", "Jump_Line"], start);
                var resend = DateTime.UtcNow.AddSeconds(2);
                var deadline = DateTime.UtcNow.AddSeconds(10);
                while (true)
                {
                    list = await ReadList(tag);
                    if (list.Status == "Ready" && (list.Name != menuName || list.Layer != layer)) throw new ReceiverException("Receiver menu changed; try again");
                    if (list.Status == "Ready" && (list.Current - 1) / 8 == (start - 1) / 8) break;
                    if (DateTime.UtcNow > deadline) throw new ReceiverException("Receiver menu page did not load");
                    if (DateTime.UtcNow > resend) { await Put([tag, "List_Control", "Jump_Line"], start); resend = DateTime.UtcNow.AddSeconds(2); }
                    await Task.Delay(150, _ct);
                }
            }
            if (list.Name != menuName || list.Layer != layer) throw new ReceiverException("Receiver menu changed; try again");
            var item = list.Items.FirstOrDefault(i => i.Kind == kind && i.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (item != null) return (list, item);
        }
        throw new ReceiverException($"Not found: {name}");
    }
    private async Task<ReceiverList> EnterContainer(string tag, ReceiverList list, string name)
    {
        var found = await Find(tag, list, name, "Container");
        await RevalidateSelect(tag, found.List, found.Item);
        return await WaitList(tag, l => l.Layer > found.List.Layer && l.Name == name);
    }
    private async Task SelectNamedItem(string tag, ReceiverList list, string name)
    {
        var found = await Find(tag, list, name, "Item");
        await RevalidateSelect(tag, found.List, found.Item);
    }
    private async Task RevalidateSelect(string tag, ReceiverList expected, ListItem item)
    {
        var current = await ReadyList(tag);
        if (current.Layer != expected.Layer || current.Name != expected.Name || !current.Items.Any(i => i.Index == item.Index && i.Name == item.Name && i.Kind == item.Kind))
            throw new ReceiverException("Receiver menu changed; selection cancelled");
        await Put([tag, "List_Control", "Direct_Sel"], $"Line_{(item.Index - 1) % 8 + 1}");
    }

    public Task SelectPresetAsync(int id) => Run(() => SelectPreset(id));
    private async Task SelectPreset(int id)
    {
        if (!Presets.Any(p => p.Id == id)) throw new ReceiverException("Unknown tuner preset");
        var switched = State.Input != "TUNER";
        await SelectInput("TUNER");
        if (switched) await Task.Delay(1000, _ct);
        await Put(["Tuner", "Play_Control", "Preset", "Preset_Sel"], id);
        var until = DateTime.UtcNow.AddSeconds(2);
        do { await Task.Delay(150, _ct); await RefreshInternal(); if (State.Preset == id) return; } while (DateTime.UtcNow < until);
        throw new ReceiverException("Tuner preset was not confirmed");
    }
    public Task StepAsync(int diff) => Run(async () => { await RefreshInternal(); await Step(diff); });
    private async Task Step(int diff)
    {
        RequirePower();
        if (diff == 0) return;
        if (State.Input == "NET RADIO")
        {
            var i = Array.IndexOf(Settings.Stations, State.Station);
            await PlayStation(Settings.Stations[((i < 0 ? 0 : i) + Math.Sign(diff) + Settings.Stations.Length) % Settings.Stations.Length]);
        }
        else if (State.Input == "TUNER")
        {
            await ReadPresets(); if (Presets.Length == 0) throw new ReceiverException("No FM presets stored");
            var i = Array.FindIndex(Presets, p => p.Id == State.Preset);
            await SelectPreset(Presets[((i < 0 ? 0 : i) + Math.Sign(diff) + Presets.Length) % Presets.Length].Id);
        }
        else if (State.Input is "SERVER" or "Spotify" or "AirPlay")
        { await Put([Tag(State.Input), "Play_Control", "Playback"], diff > 0 ? "Skip Fwd" : "Skip Rev"); await Task.Delay(200, _ct); await RefreshInternal(); }
        else throw new ReceiverException("This input has no track controls");
    }
    public Task PlaybackAsync(string action = "toggle") => Run(async () => { await RefreshInternal(); await Playback(action); });
    private async Task Playback(string action)
    {
        RequirePower();
        var expected = "Stop";
        if (State.Input == "NET RADIO")
        {
            if (action == "stop" || action == "toggle" && State.Playback == "Play") await Put(["NET_RADIO", "Play_Control", "Playback"], "Stop");
            else if (_lastStation != null) { await PlayStation(_lastStation); return; }
            else throw new ReceiverException("Choose a radio favorite to start");
        }
        else if (State.Input is "SERVER" or "Spotify" or "AirPlay")
        {
            var command = action == "stop" ? "Stop" : State.Playback == "Play" ? "Pause" : "Play";
            expected = command;
            if (State.Input == "SERVER" && State.Playback == "Stop" && command == "Play")
            {
                if (_lastServerTrack is not { } last) throw new ReceiverException("Start a SERVER track in HA or on the receiver first");
                var list = await ReadyList("SERVER");
                if (list.Name != last.Menu) throw new ReceiverException("SERVER folder changed; select the track again in HA");
                await SelectNamedItem("SERVER", list, last.Track);
            }
            else await Put([Tag(State.Input), "Play_Control", "Playback"], command);
        }
        else throw new ReceiverException("This input has no playback control");
        var until = DateTime.UtcNow.AddSeconds(2);
        do { await Task.Delay(150, _ct); await RefreshInternal(); if (State.Playback == expected) return; } while (DateTime.UtcNow < until);
        throw new ReceiverException($"Playback state not confirmed: {expected}");
    }
    public Task ToggleShuffleAsync() => Run(ToggleShuffle);
    private async Task ToggleShuffle()
    { RequirePower(); if (State.Input != "SERVER") throw new ReceiverException("Shuffle requires SERVER"); await Put(["SERVER", "Play_Control", "Play_Mode", "Shuffle"], State.Shuffle == "On" ? "Off" : "On"); await RefreshInternal(); }
    public Task CycleRepeatAsync() => Run(CycleRepeat);
    private async Task CycleRepeat()
    { RequirePower(); if (State.Input != "SERVER") throw new ReceiverException("Repeat requires SERVER"); await Put(["SERVER", "Play_Control", "Play_Mode", "Repeat"], State.Repeat switch { "Off" => "One", "One" => "All", _ => "Off" }); await RefreshInternal(); }

    public Task ExecuteContextAsync(SourceAction action) => Run(async () =>
    {
        // Re-read under the same gate as execution. A stale view must never switch inputs.
        await RefreshInternal();
        if (!SourceControls.IsCurrent(action, State, Settings, Presets))
            throw new ReceiverException("Input or controls changed; command cancelled");
        if (action.Id.StartsWith("station:")) await PlayStation(action.Id[8..]);
        else if (action.Id.StartsWith("preset:") && int.TryParse(action.Id[7..], out var id)) await SelectPreset(id);
        else switch (action.Id)
        {
            case "previous": await Step(-1); break;
            case "next": await Step(1); break;
            case "play": await Playback("toggle"); break;
            case "stop": await Playback("stop"); break;
            case "shuffle": await ToggleShuffle(); break;
            case "repeat": await CycleRepeat(); break;
            default: throw new ReceiverException("Unknown context action");
        }
    });
    public void Dispose() => _transport.Dispose();
}

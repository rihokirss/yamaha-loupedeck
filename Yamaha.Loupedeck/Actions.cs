using Yamaha.Core;
using YamahaPlugin = Loupedeck.YamahaDirectPlugin.YamahaDirectPlugin;

namespace Loupedeck.YamahaDirectPlugin;

public sealed class ReceiverCommand : PluginDynamicCommand
{
    private static readonly string[] Ids = ["power", "mute", "source_apply", "station0", "station1", "station2", "station3", "station4", "station5", "station6", "station7", "previous", "play", "next", "stop", "shuffle", "repeat"];
    public ReceiverCommand() : base()
    { foreach (var id in Ids) AddParameter(id, Label(id), "Yamaha"); }
    private static string Label(string id)
    {
        var p = YamahaPlugin.Current;
        if (id.StartsWith("station") && int.TryParse(id[7..], out var n))
        { var stations = p?.Receiver?.Settings.Stations ?? new ReceiverSettings().Stations; if (n < stations.Length) return stations[n]; }
        return id switch { "power" => "Toide", "mute" => p?.Receiver?.State.Muted == true ? "Heli tagasi" : "Vaigista", "source_apply" => "Vali sisend", "previous" => "Eelmine", "next" => "Järgmine", "play" => "Esita / peata", "stop" => "Peata", "shuffle" => "Juhuesitus", "repeat" => "Kordus", _ => "Yamaha" };
    }
    protected override bool OnLoad() { ((YamahaPlugin)Plugin).Updated += Refresh; return true; }
    protected override bool OnUnload() { ((YamahaPlugin)Plugin).Updated -= Refresh; return true; }
    private void Refresh() { foreach (var id in Ids) ActionImageChanged(id); }
    protected override string GetCommandDisplayName(string parameter, PluginImageSize size) => Label(parameter);
    protected override BitmapImage GetCommandImage(string id, PluginImageSize size)
    {
        var p = YamahaPlugin.Current; if (p?.Receiver == null) return Artwork.Icon("radio", "Connecting", Artwork.Gray);
        p.Touch(); var s = p.Receiver.State;
        if (id == "play") return Artwork.GlowGlyph(s.Playback == "Play" ? "pause" : "play", Artwork.Violet, s.Playback == "Play");
        var label = id switch { "power" => s.Power ? "Power On" : "Standby", "mute" => s.Muted ? "Muted" : "Mute", "previous" => "Previous", "next" => "Next", "stop" => "Stop", "shuffle" => "Shuffle", "repeat" => "Repeat", _ => id };
        var icon = id switch { "power" => "power", "mute" => s.Muted ? "volume-x" : "volume-2", "previous" => "skip-back", "next" => "skip-forward", "stop" => "square", "shuffle" => "shuffle", "repeat" => "repeat", _ => "radio" };
        var color = !s.Available ? Artwork.Gray : id == "power" ? Artwork.Green : id == "mute" ? Artwork.Amber : id is "previous" or "next" ? Artwork.Rose : Artwork.Violet;
        var active = id == "power" && s.Power || id == "mute" && s.Muted;
        if (id.StartsWith("station") && int.TryParse(id[7..], out var n) && n < p.Receiver.Settings.Stations.Length)
        { label = p.Receiver.Settings.Stations[n]; active = s.Power && s.Station == label && s.Playback == "Play"; return Artwork.StationGlyph(active, s.Available); }
        if (id is "previous" or "next" && (!s.Power || s.Input is not ("NET RADIO" or "TUNER" or "SERVER" or "Spotify" or "AirPlay"))) color = Artwork.Gray;
        return Artwork.GlowGlyph(icon, color, active);
    }
    protected override void RunCommand(string id)
    {
        var p = YamahaPlugin.Current; if (p == null) return;
        if (id == "source_apply") { p.ApplySource(); return; }
        p.Execute(() => Execute(p.Receiver, id));
    }
    internal static Task Execute(Receiver r, string id) => id switch
    {
        "power" => r.TogglePowerAsync(), "mute" => r.ToggleMuteAsync(), "previous" => r.StepAsync(-1), "next" => r.StepAsync(1),
        "play" => r.PlaybackAsync(), "stop" => r.PlaybackAsync("stop"), "shuffle" => r.ToggleShuffleAsync(), "repeat" => r.CycleRepeatAsync(),
        _ when id.StartsWith("station") && int.TryParse(id[7..], out var n) && n < r.Settings.Stations.Length => r.PlayStationAsync(r.Settings.Stations[n]),
        _ => Task.CompletedTask
    };
}

// Square buttons are shared across System workspaces. Keep ordinary arrows everywhere else.
public sealed class ContextArrowCommand : PluginDynamicCommand
{
    public ContextArrowCommand() : base()
    {
        AddParameter("left", "Vasak / Yamaha eelmine", "Yamaha");
        AddParameter("right", "Parem / Yamaha järgmine", "Yamaha");
    }
    protected override BitmapImage GetCommandImage(string parameter, PluginImageSize size) => Artwork.Glyph(parameter == "left" ? "skip-back" : "skip-forward");
    protected override void RunCommand(string parameter)
    {
        if (YamahaPlugin.Current is not { } p) return;
        p.Execute(async () =>
        {
            // On a visibility-query failure do not guess and accidentally skip media.
            var visible = false;
            try { visible = await WorkspaceVisibility.IsVisible(CancellationToken.None, p.Receiver.Settings.WorkspaceId); } catch { }
            if (visible) await p.Receiver.StepAsync(parameter == "left" ? -1 : 1);
            else p.ClientApplication.SendKeyboardShortcut(parameter == "left" ? VirtualKeyCode.ArrowLeft : VirtualKeyCode.ArrowRight);
        });
    }
}

public sealed class VolumeAdjustment : PluginDynamicAdjustment
{
    public VolumeAdjustment() : base("Yamaha Volume", "Receiver volume; press to mute", "Yamaha", true) { }
    protected override bool OnLoad() { var p = (YamahaPlugin)Plugin; p.Updated += Refresh; p.VolumeUpdated += ValueChanged; return true; }
    protected override bool OnUnload() { var p = (YamahaPlugin)Plugin; p.Updated -= Refresh; p.VolumeUpdated -= ValueChanged; return true; }
    private void ValueChanged() => AdjustmentValueChanged();
    private void Refresh() { AdjustmentValueChanged(); ActionImageChanged(); }
    protected override void ApplyAdjustment(string parameter, int diff) => YamahaPlugin.Current?.AdjustVolume(diff);
    protected override void RunCommand(string parameter) { if (YamahaPlugin.Current is { } p) p.Execute(() => p.Receiver.ToggleMuteAsync()); }
    protected override string GetAdjustmentValue(string parameter) { var p = YamahaPlugin.Current; p?.Touch(); var s = p?.Receiver?.State; return s == null || !s.Available ? "Offline" : !s.Power ? "Off" : s.Muted ? "Mute" : $"{p?.Volume?.PendingTarget ?? s.Volume}%"; }
    protected override string GetAdjustmentDisplayName(string parameter, PluginImageSize size) => " ";
    protected override BitmapImage GetAdjustmentImage(string p, PluginImageSize s)
    {
        var state = YamahaPlugin.Current?.Receiver?.State;
        return Artwork.Glyph(state?.Muted == true ? "volume-x" : "volume-2", state?.Available != true ? Artwork.Gray : state.Muted ? Artwork.Violet : Artwork.Amber);
    }
}

public sealed class SourceAdjustment : PluginDynamicAdjustment
{
    public SourceAdjustment() : base("Yamaha Input", "Preview input; press to select", "Yamaha", true) { }
    protected override bool OnLoad() { ((YamahaPlugin)Plugin).Updated += Refresh; return true; }
    protected override bool OnUnload() { ((YamahaPlugin)Plugin).Updated -= Refresh; return true; }
    private void Refresh() => AdjustmentValueChanged();
    protected override void ApplyAdjustment(string parameter, int diff) => YamahaPlugin.Current?.BrowseSource(diff);
    protected override void RunCommand(string parameter) => YamahaPlugin.Current?.ApplySource();
    protected override string GetAdjustmentValue(string parameter) { YamahaPlugin.Current?.Touch(); return YamahaPlugin.Current?.SourcePreview ?? "Connecting"; }
    protected override string GetAdjustmentDisplayName(string parameter, PluginImageSize size) => " ";
    protected override BitmapImage GetAdjustmentImage(string p, PluginImageSize s) => Artwork.Glyph("cable", Artwork.Violet);
}

public sealed class TrackAdjustment : PluginDynamicAdjustment
{
    public TrackAdjustment() : base("Yamaha Station / Track", "Previous or next favorite, preset or track", "Yamaha", true) { }
    protected override bool OnLoad() { ((YamahaPlugin)Plugin).Updated += Refresh; return true; }
    protected override bool OnUnload() { ((YamahaPlugin)Plugin).Updated -= Refresh; return true; }
    private void Refresh() => AdjustmentValueChanged();
    protected override void ApplyAdjustment(string parameter, int diff) { if (YamahaPlugin.Current is { } p) p.Execute(() => p.Receiver.StepAsync(diff)); }
    protected override void RunCommand(string parameter)
    {
        if (YamahaPlugin.Current is { } p && p.Receiver.State.Input != "TUNER") p.Execute(() => p.Receiver.PlaybackAsync());
        // TUNER press opens MediaFolder via the profile assignment.
    }
    protected override string GetAdjustmentValue(string parameter) { var p=YamahaPlugin.Current; p?.Touch(); return p?.Receiver == null ? "…" : p.ShortStation; }
    protected override string GetAdjustmentDisplayName(string parameter, PluginImageSize size) => " ";
    protected override BitmapImage GetAdjustmentImage(string p, PluginImageSize s) => Artwork.Glyph("skip-forward", Artwork.Rose);
}

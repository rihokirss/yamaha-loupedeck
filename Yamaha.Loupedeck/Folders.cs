using YamahaPlugin = Loupedeck.YamahaDirectPlugin.YamahaDirectPlugin;
namespace Loupedeck.YamahaDirectPlugin;

public sealed class InputsFolder : PluginDynamicFolder
{
    private string _list = "", _selection = "";
    public InputsFolder() { DisplayName = "Yamaha Inputs"; GroupName = "Yamaha"; }
    public override bool Load() { ((YamahaPlugin)Plugin).Updated += Changed; return true; }
    public override bool Unload() { ((YamahaPlugin)Plugin).Updated -= Changed; return true; }
    private void Changed()
    {
        var r = YamahaPlugin.Current?.Receiver; if (r == null) return;
        var list = string.Join("|", r.Inputs.Select(i => i.Id + ":" + i.Name));
        if (list != _list) { _list = list; ButtonActionNamesChanged(); }
        if (_selection == r.State.Input) return;
        _selection = r.State.Input; foreach (var input in r.Inputs) CommandImageChanged(input.Id);
    }
    public override string GetButtonDisplayName(PluginImageSize size) => "Sisendid";
    public override BitmapImage GetButtonImage(PluginImageSize size) => Artwork.GlowGlyph("cable", Artwork.Violet);
    public override IEnumerable<string> GetButtonPressActionNames(DeviceType device) => (YamahaPlugin.Current?.Receiver.Inputs ?? []).Select(i => CreateCommandName(i.Id));
    public override string GetCommandDisplayName(string id, PluginImageSize size) => YamahaPlugin.Current?.Receiver.Inputs.FirstOrDefault(i => i.Id == id)?.Name ?? id;
    public override BitmapImage GetCommandImage(string id, PluginImageSize size)
    {
        var p = YamahaPlugin.Current; p?.Touch();
        return Artwork.GlowGlyph(id is "NET RADIO" or "TUNER" ? "radio" : id == "SERVER" ? "library" : "cable", Artwork.Violet, p?.Receiver.State.Input == id);
    }
    public override void RunCommand(string id) { if (YamahaPlugin.Current is { } p) p.Execute(() => p.Receiver.SelectInputAsync(id)); }
}

public class MediaFolder : PluginDynamicFolder
{
    private string _list = "", _selection = "";
    public MediaFolder() { DisplayName = "Yamaha Media / FM"; GroupName = "Yamaha"; }
    public override bool Load() { ((YamahaPlugin)Plugin).Updated += Changed; return true; }
    public override bool Unload() { ((YamahaPlugin)Plugin).Updated -= Changed; return true; }
    private void Changed()
    {
        var r = YamahaPlugin.Current?.Receiver; if (r == null) return;
        var ids = Ids().ToArray();
        var list = string.Join("|", ids) + string.Join("|", r.Presets.Select(i => i.Name));
        if (_list != list) { _list = list; ButtonActionNamesChanged(); }
        var s = r.State; var selection = $"{s.Available}|{s.Input}|{s.Preset}|{s.Playback}|{s.Shuffle}|{s.Repeat}";
        if (_selection == selection) return;
        _selection = selection; foreach (var id in ids) CommandImageChanged(id);
    }
    private static IEnumerable<string> Ids()
    {
        var p = YamahaPlugin.Current; if (p?.Receiver == null) yield break;
        if (p.Receiver.State.Input is "SERVER" or "Spotify" or "AirPlay" or "NET RADIO")
        { yield return "previous"; yield return "play"; yield return "next"; yield return "stop"; }
        if (p.Receiver.State.Input == "SERVER") { yield return "shuffle"; yield return "repeat"; }
        foreach (var preset in p.Receiver.Presets) yield return "preset:" + preset.Id;
    }
    public override string GetButtonDisplayName(PluginImageSize size) => "Meedia / FM";
    public override BitmapImage GetButtonImage(PluginImageSize size) => Artwork.GlowGlyph("sliders-horizontal", Artwork.Rose);
    public override IEnumerable<string> GetButtonPressActionNames(DeviceType device) => Ids().Select(CreateCommandName);
    public override string GetCommandDisplayName(string id, PluginImageSize size)
    {
        var r = YamahaPlugin.Current?.Receiver;
        if (id.StartsWith("preset:") && int.TryParse(id[7..], out var n)) return r?.Presets.FirstOrDefault(p => p.Id == n)?.Name ?? "FM " + n;
        return id switch { "previous" => "Eelmine", "next" => "Järgmine", "play" => r?.State.Playback == "Play" ? "Peata / paus" : "Esita", "stop" => "Peata", "shuffle" => "Juhuesitus " + r?.State.Shuffle, "repeat" => "Kordus " + r?.State.Repeat, _ => id };
    }
    public override BitmapImage GetCommandImage(string id, PluginImageSize size)
    {
        var p = YamahaPlugin.Current; p?.Touch(); var s = p?.Receiver.State;
        if (id.StartsWith("preset:") && int.TryParse(id[7..], out var n))
        { return Artwork.StationGlyph(s?.Input == "TUNER" && s.Preset == n, s?.Available == true); }
        var icon = id switch { "previous" => "skip-back", "next" => "skip-forward", "play" => s?.Playback == "Play" ? "pause" : "play", "stop" => "square", "shuffle" => "shuffle", "repeat" => "repeat", _ => "radio" };
        var label = id switch { "play" => s?.Input == "NET RADIO" && s.Playback == "Play" ? "Stop Radio" : s?.Playback == "Play" ? "Pause" : "Play", "shuffle" => "Shuffle " + s?.Shuffle, "repeat" => "Repeat " + s?.Repeat, _ => id };
        // NET RADIO previous/next are our favorite navigator, not unsupported Playback Skip.
        return Artwork.GlowGlyph(icon, s?.Available == true ? Artwork.Violet : Artwork.Gray, id == "play" && s?.Playback == "Play");
    }
    public override void RunCommand(string id)
    {
        if (YamahaPlugin.Current is not { } p) return;
        if (id.StartsWith("preset:") && int.TryParse(id[7..], out var n)) p.Execute(() => p.Receiver.SelectPresetAsync(n));
        else p.Execute(() => ReceiverCommand.Execute(p.Receiver, id));
    }
}

public sealed class TrackPressFolder : MediaFolder
{
    public TrackPressFolder() { DisplayName = "Yamaha Track Press"; }
    public override bool Activate()
    {
        if (YamahaPlugin.Current is { } p && p.Receiver.State.Input != "TUNER")
        { p.Execute(() => p.Receiver.PlaybackAsync()); return false; }
        return true;
    }
}

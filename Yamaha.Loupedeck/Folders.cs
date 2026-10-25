using Yamaha.Core;
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
    public MediaFolder() { DisplayName = "Yamaha Meedia · aktiivne sisend"; GroupName = "Yamaha"; }
    public override bool Load() { ((YamahaPlugin)Plugin).Updated += Changed; return true; }
    public override bool Unload() { ((YamahaPlugin)Plugin).Updated -= Changed; return true; }
    private void Changed()
    {
        var r = YamahaPlugin.Current?.Receiver; if (r == null) return;
        var items = Items();
        var list = string.Join("|", items.Select(a => a.Token + ":" + a.Label));
        if (_list != list) { _list = list; ButtonActionNamesChanged(); }
        var s = r.State; var selection = $"{s.Available}|{s.Input}|{s.Preset}|{s.Playback}|{s.Shuffle}|{s.Repeat}";
        if (_selection == selection) return;
        _selection = selection; foreach (var item in items) CommandImageChanged(item.Token);
    }
    private static SourceAction[] Items()
    {
        var r = YamahaPlugin.Current?.Receiver;
        return r == null ? [] : SourceControls.Menu(r.State, r.Settings, r.Presets);
    }
    public override bool Activate() { YamahaPlugin.Current?.Touch(); return Items().Length > 0; }
    public override string GetButtonDisplayName(PluginImageSize size) => "Meedia";
    public override BitmapImage GetButtonImage(PluginImageSize size) => Artwork.GlowGlyph("sliders-horizontal", Artwork.Rose);
    public override IEnumerable<string> GetButtonPressActionNames(DeviceType device) => Items().Select(a => CreateCommandName(a.Token));
    public override string GetCommandDisplayName(string id, PluginImageSize size)
    {
        return Items().FirstOrDefault(a => a.Token == id)?.Label ?? " ";
    }
    public override BitmapImage GetCommandImage(string id, PluginImageSize size)
    {
        YamahaPlugin.Current?.Touch();
        return ContextPresentation.Image(Items().FirstOrDefault(a => a.Token == id));
    }
    public override void RunCommand(string id)
    {
        if (YamahaPlugin.Current is not { } p) return;
        var action = Items().FirstOrDefault(a => a.Token == id);
        if (action?.Enabled == true) p.Execute(() => p.Receiver.ExecuteContextAsync(action));
    }
}

public sealed class TrackPressFolder : MediaFolder
{
    public TrackPressFolder() { DisplayName = "Yamaha Track Press"; }
    public override bool Activate()
    {
        if (YamahaPlugin.Current is not { } p) return false;
        if (p.Receiver.State.Input == "TUNER") return base.Activate();
        var action = SourceControls.Menu(p.Receiver.State, p.Receiver.Settings, p.Receiver.Presets).FirstOrDefault(a => a.Id == "play");
        if (action?.Enabled == true) p.Execute(() => p.Receiver.ExecuteContextAsync(action));
        return false;
    }
}

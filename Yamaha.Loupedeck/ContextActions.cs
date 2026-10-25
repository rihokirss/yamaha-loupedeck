using Yamaha.Core;
using YamahaPlugin = Loupedeck.YamahaDirectPlugin.YamahaDirectPlugin;

namespace Loupedeck.YamahaDirectPlugin;

internal static class ContextPresentation
{
    internal static BitmapImage Image(SourceAction? action)
    {
        if (action == null || !action.Enabled || action.Id.Length == 0) return Artwork.Blank();
        if (action.Id.StartsWith("station:")) return Artwork.StationGlyph(action.Active);
        if (action.Id.StartsWith("preset:")) return Artwork.PresetGlyph(action.Detail, action.Active);
        return Artwork.GlowGlyph(action.Icon, action.Id is "previous" or "next" ? Artwork.Rose : Artwork.Violet, action.Active);
    }
}

public sealed class ContextSlotCommand : PluginDynamicCommand
{
    private readonly object _sync = new();
    private readonly SourceAction?[] _drawn = new SourceAction?[8];
    private SourceAction[] _last = [];
    public ContextSlotCommand() : base()
    { for (var n = 0; n < 8; n++) AddParameter("slot" + n, "Sisendinupp " + (n + 1), "Yamaha · aktiivne sisend"); }
    protected override bool OnLoad() { ((YamahaPlugin)Plugin).Updated += Refresh; return true; }
    protected override bool OnUnload() { ((YamahaPlugin)Plugin).Updated -= Refresh; return true; }
    private static SourceAction[] Current()
    {
        var r = YamahaPlugin.Current?.Receiver;
        return SourceControls.Slots(r?.State ?? new(), r?.Settings ?? new(), r?.Presets ?? []);
    }
    private void Refresh()
    {
        var next = Current();
        lock (_sync) { if (_last.SequenceEqual(next)) return; _last = next; }
        for (var n = 0; n < 8; n++) ActionImageChanged("slot" + n);
    }
    private static int Index(string parameter) => parameter.StartsWith("slot") && int.TryParse(parameter[4..], out var n) && n is >= 0 and < 8 ? n : -1;
    protected override string GetCommandDisplayName(string parameter, PluginImageSize size)
    {
        var n = Index(parameter); var label = n < 0 ? "" : Current()[n].Label;
        return label.Length > 0 ? label : " ";
    }
    protected override BitmapImage GetCommandImage(string parameter, PluginImageSize size)
    {
        YamahaPlugin.Current?.Touch(); var n = Index(parameter);
        if (n < 0) return Artwork.Blank();
        var action = Current()[n]; lock (_sync) _drawn[n] = action;
        return ContextPresentation.Image(action);
    }
    protected override void RunCommand(string parameter)
    {
        var n = Index(parameter); if (n < 0 || YamahaPlugin.Current is not { } p) return;
        SourceAction? action; lock (_sync) action = _drawn[n];
        if (action?.Enabled == true && action.Id.Length > 0) p.Execute(() => p.Receiver.ExecuteContextAsync(action));
    }
}

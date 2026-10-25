using Loupedeck.Devices.Loupedeck2Devices;

namespace Loupedeck.YamahaDirectPlugin;

// A real 240px wheel canvas, not an action icon shrunk into the stock 1x1 template.
public sealed class WheelToolYamahaNowPlaying : WheelToolGeneric
{
    public WheelToolYamahaNowPlaying() : base("Yamaha · Hi-Fi", "Yamaha", 3) { }
    protected override int GetButtonIndex(int x, int y) => y < 165 ? 1 : x < 90 ? 0 : x > 150 ? 2 : 1;
    protected override void OnButtonDoubleClick(int index) { } // Do not accidentally toggle playback twice.
    protected override void OnStart()
    {
        base.OnStart();
        if (Plugin is YamahaDirectPlugin p) { p.Updated += Refresh; p.VolumeUpdated += Refresh; }
    }
    protected override void OnStop()
    {
        if (Plugin is YamahaDirectPlugin p) { p.Updated -= Refresh; p.VolumeUpdated -= Refresh; }
        base.OnStop();
    }
    private void Refresh() { if (IsStarted && !IsPaused) DrawDelayed(33); }
    protected override BitmapImage CreateImage()
    {
        var p = Plugin as YamahaDirectPlugin ?? YamahaDirectPlugin.Current;
        if (p?.Receiver == null) return Artwork.Wheel(false, false, false, 0, "", "", "", "Stop", "Connecting");
        var s = p.Receiver.State;
        return Artwork.Wheel(s.Available, s.Power, s.Muted, p.Volume.PendingTarget ?? s.Volume, s.Input, s.Station, s.Title, s.Playback, p.UiError.Length > 0 ? p.UiError : s.Error);
    }
    protected override BitmapImage CreateDemoImage() => Artwork.Wheel(true, true, false, 35, "NET RADIO", "Raadio Kuku", "Hetkel eetris", "Play", "");
}

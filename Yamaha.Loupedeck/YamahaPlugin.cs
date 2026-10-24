using System.Text.Json;
using Yamaha.Core;

namespace Loupedeck.YamahaDirectPlugin;

public sealed class YamahaDirectPlugin : Plugin
{
    public override bool UsesApplicationApiOnly => true;
    public override bool HasNoApplication => true;
    public static YamahaDirectPlugin? Current { get; private set; }
    internal Receiver Receiver { get; private set; } = null!;
    internal VolumePump Volume { get; private set; } = null!;
    internal event Action? Updated;
    internal event Action? VolumeUpdated;
    internal string UiError { get; private set; } = "";
    private readonly CancellationTokenSource _stop = new();
    private DateTime _visibleUntil;
    private readonly string _settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "YamahaDirect", "settings.json");
    private Task? _pollTask;
    private int _sourceIndex = -1;
    private DateTime _sourceExpires;
    public override void Load()
    {
        Current = this;
        // Preserve settings from the first local prototype; no Codex app dependency.
        var legacyPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodexController", "Yamaha", "settings.json");
        if (!File.Exists(_settingsPath) && File.Exists(legacyPath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
            File.Copy(legacyPath, _settingsPath);
        }
        var settings = File.Exists(_settingsPath) ? JsonSerializer.Deserialize<ReceiverSettings>(File.ReadAllText(_settingsPath)) ?? new() : new ReceiverSettings();
        Receiver = new Receiver(settings, new HttpYamahaTransport(), _stop.Token);
        Receiver.Changed += () => { if (!Receiver.State.Available) Volume?.Discard(); Updated?.Invoke(); };
        Receiver.AddressChanged += SaveSettings;
        Volume = new VolumePump(Receiver);
        Volume.Changed += () => VolumeUpdated?.Invoke();
        Volume.Failed += ex => Error(ex);
        _pollTask = Task.Run(Poll);
    }
    private void SaveSettings()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
        File.WriteAllText(_settingsPath, JsonSerializer.Serialize(Receiver.Settings, new JsonSerializerOptions { WriteIndented = true }));
    }
    private async Task Poll()
    {
        var nextDiscovery = DateTime.MinValue;
        var nextRefresh = DateTime.MinValue;
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                bool visible;
                try { visible = await WorkspaceVisibility.IsVisible(_stop.Token, Receiver.Settings.WorkspaceId); }
                catch { visible = DateTime.UtcNow < _visibleUntil; }
                if (!visible && DateTime.UtcNow < nextRefresh) { await Task.Delay(2000, _stop.Token); continue; }
                nextRefresh = DateTime.UtcNow.AddSeconds(10);
                if (Receiver.Settings.Host.Length == 0) await Receiver.RediscoverAsync();
                else await Receiver.RefreshAsync();
                if (Receiver.State.Available) { var hadError = UiError.Length > 0; UiError = ""; SaveSettings(); if (hadError) Updated?.Invoke(); }
            }
            catch (Exception ex) when (!_stop.IsCancellationRequested)
            {
                Error(ex);
                if (DateTime.UtcNow > nextDiscovery)
                {
                    nextDiscovery = DateTime.UtcNow.AddSeconds(30);
                    try { await Receiver.RediscoverAsync(); } catch (Exception foundError) { Error(foundError); }
                }
            }
            try { await Task.Delay(2000, _stop.Token); } catch (OperationCanceledException) { }
        }
    }
    internal void Touch() => _visibleUntil = DateTime.UtcNow.AddSeconds(4);
    internal void Execute(Func<Task> action)
    {
        Touch();
        _ = Task.Run(async () =>
        {
            try { UiError = ""; await action(); Updated?.Invoke(); }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
            catch (Exception ex) { Error(ex); }
        });
    }
    private void Error(Exception ex) { UiError = ex.Message; Log.Error("Yamaha: " + ex.Message); Updated?.Invoke(); }
    internal void AdjustVolume(int diff)
    {
        Touch();
        if (!Volume.Adjust(diff)) Error(new ReceiverException(Receiver.State.Available ? "Receiver is in standby" : "Receiver offline"));
    }
    internal void BrowseSource(int diff)
    {
        Touch(); var list = Receiver.Inputs;
        if (list.Length == 0) return;
        if (_sourceIndex < 0 || DateTime.UtcNow > _sourceExpires) _sourceIndex = Math.Max(0, Array.FindIndex(list, i => i.Id == Receiver.State.Input));
        _sourceIndex = ((_sourceIndex + diff) % list.Length + list.Length) % list.Length;
        _sourceExpires = DateTime.UtcNow.AddSeconds(10); Updated?.Invoke();
    }
    internal static string ShortInput(string value) => value switch { "NET RADIO" => "Radio", "TUNER" => "FM", "SERVER" => "Server", "OPTICAL" => "Optical", "COAXIAL" => "Coax", "LINE1" => "Line 1", "LINE2" => "Line 2", "LINE3" => "Line 3", _ => value };
    internal string SourcePreview => _sourceIndex >= 0 && DateTime.UtcNow < _sourceExpires && _sourceIndex < Receiver.Inputs.Length ? "→" + ShortInput(Receiver.Inputs[_sourceIndex].Id) : ShortInput(Receiver.State.Input);
    internal string ShortStation => Receiver.State.Station switch { "Raadio Kuku" => "Kuku", "Raadio 2" => "R2", "Power Hit Radio" => "Power", "Vikerraadio" => "Viker", "Raadio Elmar" => "Elmar", "Sky Plus" => "Sky+", "Star FM" => "Star", "NRJ" => "NRJ", "" => ShortInput(Receiver.State.Input), var name => name.Length <= 7 ? name : name[..7] };
    internal void ApplySource()
    {
        if (_sourceIndex < 0 || DateTime.UtcNow > _sourceExpires) return;
        var id = Receiver.Inputs[_sourceIndex].Id; _sourceIndex = -1; Execute(() => Receiver.SelectInputAsync(id));
    }
    public override void Unload()
    {
        _stop.Cancel(); Volume?.Dispose();
        try { _pollTask?.Wait(2000); } catch (AggregateException) { }
        Receiver?.Dispose(); Current = null;
    }
}

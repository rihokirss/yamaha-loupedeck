using System.Text.Json;
using Yamaha.Core;

if (args.Length == 0 || args[0] == "test") { await Tests.Run(); return; }
var settingsPath = args.Length > 1 ? args[1] : throw new ArgumentException("Pass the local settings JSON path");
var settings = JsonSerializer.Deserialize<ReceiverSettings>(File.ReadAllText(settingsPath))!;
using var receiver = new Receiver(settings, new HttpYamahaTransport());
await receiver.RefreshAsync();
Console.WriteLine(JsonSerializer.Serialize(new { State = receiver.State, receiver.Inputs, receiver.Presets }));
if (args[0] == "inspect") return;
if (args[0] == "play-station") { await receiver.PlayStationAsync(args[2]); Console.WriteLine("PLAYING " + receiver.State.Station); return; }
var original = receiver.State;
if (!original.Power || original.Volume > 25 || original.Input != "NET RADIO" || !settings.Stations.Contains(original.Station))
    throw new Exception("Live smoke requires NET RADIO playing a configured favorite at volume <=25");
try
{
    if (args[0] == "smoke-basic")
    {
        await receiver.SetVolumeAsync(original.Volume + 1); Console.WriteLine("PASS volume +1 = " + receiver.State.Volume);
        await receiver.SetVolumeAsync(original.Volume);
        await receiver.ToggleMuteAsync(); if (receiver.State.Muted == original.Muted) throw new Exception("Mute did not change"); Console.WriteLine("PASS mute");
        await receiver.ToggleMuteAsync();
        await receiver.SelectInputAsync("OPTICAL"); if (receiver.State.Input != "OPTICAL") throw new Exception("Input did not change"); Console.WriteLine("PASS input OPTICAL");
        await receiver.PlayStationAsync(original.Station); Console.WriteLine("PASS return radio");
        await receiver.TogglePowerAsync(); if (receiver.State.Power) throw new Exception("Power off failed"); Console.WriteLine("PASS standby");
        await receiver.PlayStationAsync(original.Station); Console.WriteLine("PASS station wakes receiver");
    }
    else if (args[0] == "smoke-stations")
    {
        foreach (var station in settings.Stations) { await receiver.PlayStationAsync(station); Console.WriteLine("PASS station: " + receiver.State.Station); }
    }
    else if (args[0] == "smoke-presets")
    {
        foreach (var preset in receiver.Presets) { await receiver.SelectPresetAsync(preset.Id); Console.WriteLine("PASS FM preset " + preset.Id); }
    }
    else if (args[0] == "smoke-transport")
    {
        await receiver.StepAsync(1); Console.WriteLine("PASS next favorite " + receiver.State.Station);
        await receiver.StepAsync(-1); Console.WriteLine("PASS previous favorite " + receiver.State.Station);
        await receiver.PlaybackAsync("stop"); if (receiver.State.Playback != "Stop") throw new Exception("Stop not confirmed"); Console.WriteLine("PASS radio stop");
        await receiver.PlaybackAsync(); Console.WriteLine("PASS radio resume " + receiver.State.Station);
    }
    else throw new ArgumentException("Unknown command");
}
finally
{
    await receiver.RefreshAsync();
    if (!receiver.State.Power) await receiver.TogglePowerAsync();
    await receiver.SetVolumeAsync(original.Volume);
    if (receiver.State.Muted != original.Muted) await receiver.ToggleMuteAsync();
    await receiver.PlayStationAsync(original.Station);
    Console.WriteLine("RESTORED " + JsonSerializer.Serialize(receiver.State));
}

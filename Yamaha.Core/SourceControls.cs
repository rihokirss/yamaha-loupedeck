using System.Text.RegularExpressions;

namespace Yamaha.Core;

public record SourceAction(string Input, string Id, string Label, string Icon, bool Enabled, bool Active = false, string Detail = "")
{
    public string Token => Uri.EscapeDataString(Input) + "|" + Id;
}

/// <summary>Only confirmed receiver state is used here, never the input-dial preview.</summary>
public static class SourceControls
{
    public static SourceAction[] Slots(ReceiverState state, ReceiverSettings settings, PresetItem[] presets)
    {
        IEnumerable<SourceAction> actions = [];
        if (state.Available)
        {
            actions = state.Input switch
            {
                "NET RADIO" => settings.Stations.Select(name => new SourceAction(state.Input, "station:" + name, name, "radio", true,
                    state.Power && state.Playback == "Play" && state.Station == name)),
                "TUNER" => PresetActions(state, settings, presets),
                _ => Menu(state, settings, presets),
            };
        }
        return actions.Take(8).Concat(Enumerable.Repeat(new SourceAction(state.Input, "", "", "", false), 8)).Take(8).ToArray();
    }

    public static SourceAction[] Menu(ReceiverState state, ReceiverSettings settings, PresetItem[] presets)
    {
        if (!state.Available) return [];
        if (state.Input == "TUNER") return PresetActions(state, settings, presets).ToArray();
        if (!state.Power || state.Input is not ("NET RADIO" or "SERVER" or "Spotify" or "AirPlay")) return [];
        var playing = state.Playback == "Play";
        var actions = new List<SourceAction>
        {
            new(state.Input, "previous", "Eelmine", "skip-back", true),
            new(state.Input, "play", playing ? state.Input == "NET RADIO" ? "Peata" : "Paus" : "Esita", playing ? state.Input == "NET RADIO" ? "square" : "pause" : "play", true, playing),
            new(state.Input, "next", "Järgmine", "skip-forward", true),
            new(state.Input, "stop", "Peata", "square", true),
        };
        if (state.Input == "SERVER")
        {
            actions.Add(new(state.Input, "shuffle", "Juhuesitus", "shuffle", true, state.Shuffle == "On"));
            actions.Add(new(state.Input, "repeat", state.Repeat switch { "One" => "Korda ühte", "All" => "Korda kõiki", _ => "Kordus" }, "repeat", true, state.Repeat is "One" or "All"));
        }
        return actions.ToArray();
    }

    private static IEnumerable<SourceAction> PresetActions(ReceiverState state, ReceiverSettings settings, PresetItem[] presets) => presets.Select(p =>
    {
        var name = Regex.Replace(p.Name, @"^\s*\d+\s*:\s*", "").Trim();
        var frequency = Rds.PresetFrequency(name);
        var detail = frequency.Length > 0 ? frequency : p.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var learned = frequency.Length > 0 ? Rds.Clean(settings.FmStationNames?.GetValueOrDefault(frequency) ?? "") : "";
        return new SourceAction(state.Input, "preset:" + p.Id, learned.Length > 0 ? learned : name.Length > 0 ? name : "FM " + p.Id, "radio", true,
            state.Power && state.Preset == p.Id && (state.Frequency.Length == 0 || state.Frequency == frequency), detail);
    });

    public static SourceAction? ResolveMenu(string token, ReceiverState state, ReceiverSettings settings, PresetItem[] presets)
        => Menu(state, settings, presets).FirstOrDefault(a => a.Token == token);

    public static bool IsCurrent(SourceAction action, ReceiverState state, ReceiverSettings settings, PresetItem[] presets)
        => action.Enabled && action.Input == state.Input && Slots(state, settings, presets).Concat(Menu(state, settings, presets)).Any(a => a.Enabled && a.Id == action.Id);
}

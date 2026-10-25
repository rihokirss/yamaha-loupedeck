using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Yamaha.Core;

public record RdsInfo(string Frequency, string Station, string Text, bool Tuned);

public static class Rds
{
    public static string Clean(string value)
    {
        var clean = Regex.Replace(new string(XmlValue.Clean(value).Select(c => char.IsControl(c) ? ' ' : c).ToArray()), @"\s+", " ").Trim();
        return clean.Any(char.IsLetterOrDigit) ? clean : "";
    }

    private static string Frequency(decimal value) => value is >= 50 and <= 150 ? value.ToString("0.##", CultureInfo.InvariantCulture) : "";

    public static string PresetFrequency(string title)
    {
        var match = Regex.Match(title, @"\bFM\s+([\d.,]+)\s*MHz", RegexOptions.IgnoreCase);
        return match.Success && decimal.TryParse(match.Groups[1].Value.Replace(',', '.'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number)
            ? Frequency(number) : "";
    }

    public static RdsInfo Read(XElement info)
    {
        var frequency = "";
        var current = info.Element("Tuning")?.Element("Freq")?.Element("Current");
        if (info.Value("Tuning", "Band") == "FM" && current != null && current.Value("Unit") == "MHz"
            && decimal.TryParse(current.Value("Val"), NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
            && int.TryParse(current.Value("Exp"), out var exp) && exp is >= 0 and <= 4)
        {
            for (var n = 0; n < exp; n++) value /= 10;
            frequency = Frequency(value);
        }
        var tuned = frequency.Length > 0 && info.Value("Feature_Availability") == "Ready" && info.Value("Signal_Info", "Tuned") == "Assert";
        var station = tuned ? Clean(info.Value("Meta_Info", "Program_Service")) : "";
        var text = tuned ? Clean(info.Value("Meta_Info", "Radio_Text_A")) : "";
        if (tuned && text.Length == 0) text = Clean(info.Value("Meta_Info", "Radio_Text_B"));
        return new(frequency, station, text, tuned);
    }
}

/// <summary>Learn only repeated, settled PS names from the currently tuned FM frequency. Never retunes.</summary>
public sealed class RdsStationLearner
{
    private string _frequency = "", _station = "";
    private DateTimeOffset _frequencySince, _stationSince;
    private int _observations;
    public void Reset() { _frequency = _station = ""; _observations = 0; }
    public bool Observe(RdsInfo info, ReceiverSettings settings, DateTimeOffset now)
    {
        if (!info.Tuned || info.Frequency.Length == 0 || info.Station.Length is 0 or > 32) { Reset(); return false; }
        if (_frequency != info.Frequency)
        { _frequency = info.Frequency; _frequencySince = now; _station = ""; }
        if (_station != info.Station)
        { _station = info.Station; _stationSince = now; _observations = 0; }
        _observations++;
        // Reject the first frame after tuning and transient/incomplete PS fragments.
        if (_observations < 2 || now - _frequencySince < TimeSpan.FromSeconds(3) || now - _stationSince < TimeSpan.FromSeconds(2)) return false;
        var names = settings.FmStationNames ?? [];
        if (names.TryGetValue(info.Frequency, out var old) && old == info.Station) return false;
        settings.FmStationNames = new(names) { [info.Frequency] = info.Station };
        return true;
    }
}

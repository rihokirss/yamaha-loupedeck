using System.Reflection;
using System.Security;
using System.Xml.Linq;

namespace Loupedeck.YamahaDirectPlugin;

internal static class Artwork
{
    private static readonly Dictionary<string, string> Shapes = new();
    internal const string Cyan = "#22D3EE", Muted = "#F59E0B", White = "#F8FAFC", Gray = "#64748B", Red = "#EF4444";
    internal const string Amber = "#FFAD47", Green = "#7BD89A", Violet = "#AB93FA", Rose = "#FF7597", Cream = "#FFF4E4";
    private static string Escape(string s) => SecurityElement.Escape(s) ?? "";
    private static string Shape(string name)
    {
        lock (Shapes)
        {
            if (Shapes.TryGetValue(name, out var shape)) return shape;
            var asm = typeof(Artwork).Assembly;
            var resource = asm.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith("." + name + ".svg", StringComparison.Ordinal));
            if (resource == null) return "";
            using var stream = asm.GetManifestResourceStream(resource)!;
            var svg = XElement.Load(stream);
            // Strip namespace declarations; preserve the original Lucide geometry.
            shape = string.Join("", svg.Elements().Select(e => e.ToString(SaveOptions.DisableFormatting)));
            Shapes[name] = shape; return shape;
        }
    }
    internal static BitmapImage Glyph(string icon, string color = White) => BitmapImage.FromSvg($"<svg xmlns='http://www.w3.org/2000/svg' width='180' height='180' viewBox='0 0 180 180'><rect width='180' height='180' fill='black'/><g transform='translate(24 24) scale(5.5)' fill='none' stroke='{color}' stroke-width='1.8' stroke-linecap='round' stroke-linejoin='round'>{Shape(icon)}</g></svg>");
    internal static BitmapImage GlowGlyph(string icon, string color, bool active = false) => MakeGlow(icon, color, active, color);
    internal static BitmapImage StationGlyph(bool active, bool available = true) => MakeGlow("radio", available ? White : Gray, active, Amber);
    internal static string PowerLabel(bool available, bool power) => !available ? "ÜHENDUS PUUDUB" : power ? "SEES" : "OOTEL";
    internal static string PowerColor(bool available, bool power) => !available ? Red : power ? Green : "#94A3B8";
    internal static BitmapImage Power(bool available, bool power) => BitmapImage.FromSvg(PowerSvg(available, power));
    internal static string PowerSvg(bool available, bool power)
    {
        var color = PowerColor(available, power);
        var label = available
            ? $"<text x='90' y='151' font-size='32'>{PowerLabel(available, power)}</text>"
            : "<text x='90' y='140' font-size='21'>ÜHENDUS</text><text x='90' y='165' font-size='21'>PUUDUB</text>";
        return $"<svg xmlns='http://www.w3.org/2000/svg' width='180' height='180' viewBox='0 0 180 180'><defs><radialGradient id='power'><stop stop-color='{color}' stop-opacity='{(available && power ? ".24" : ".06")}'/><stop offset='1' stop-color='{color}' stop-opacity='0'/></radialGradient></defs><rect width='180' height='180' fill='black'/><circle cx='90' cy='65' r='72' fill='url(#power)'/><g transform='translate(44 10) scale(3.8)' fill='none' stroke='{color}' stroke-width='2' stroke-linecap='round' stroke-linejoin='round'>{Shape("power")}</g><g text-anchor='middle' font-family='Segoe UI' font-weight='700' fill='{color}'>{label}</g></svg>";
    }
    private static BitmapImage MakeGlow(string icon, string color, bool active, string accent) => BitmapImage.FromSvg($"<svg xmlns='http://www.w3.org/2000/svg' width='180' height='180' viewBox='0 0 180 180'><defs><radialGradient id='halo'><stop stop-color='{(active ? accent : color)}' stop-opacity='{(active ? ".26" : ".04")}'/><stop offset='1' stop-color='{accent}' stop-opacity='0'/></radialGradient></defs><rect width='180' height='180' fill='black'/><circle cx='90' cy='90' r='88' fill='url(#halo)'/><g transform='translate(28 24) scale(5.2)' fill='none' stroke='{color}' stroke-width='{(active ? "2.2" : "1.8")}' stroke-linecap='round' stroke-linejoin='round'>{Shape(icon)}</g>{(active ? $"<circle cx='90' cy='165' r='4' fill='{accent}'/>" : "")}</svg>");

    private static string[] WrapLines(string text, int width, int count)
    {
        var lines = new List<string>(); var rest = text.Trim();
        while (rest.Length > 0 && lines.Count < count)
        {
            if (rest.Length <= width) { lines.Add(rest); break; }
            var cut = rest.LastIndexOf(' ', width, width);
            if (cut < width / 2) cut = width;
            var line = rest[..cut].Trim(); rest = rest[cut..].Trim();
            lines.Add(lines.Count == count - 1 ? line.TrimEnd('.') + "…" : line);
        }
        return lines.ToArray();
    }
    internal static string WheelSvg(bool available, bool power, bool muted, int volume, string input, string station, string title, string playback, string error)
    {
        static string N(double n) => n.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        static (double X, double Y) Point(double angle, double radius)
        { var a = angle * Math.PI / 180; return (120 + radius * Math.Cos(a), 120 + radius * Math.Sin(a)); }
        static string Arc(double degrees, double radius)
        {
            var a = Point(135, radius); var b = Point(135 + Math.Max(.01, degrees), radius);
            return $"M {N(a.X)} {N(a.Y)} A {N(radius)} {N(radius)} 0 {(degrees > 180 ? 1 : 0)} 1 {N(b.X)} {N(b.Y)}";
        }
        var accent = !available || error.Length > 0 ? Red : muted ? Violet : Amber;
        var heading = !available || !power ? "YAMAHA R-N301" : input == "NET RADIO" ? "NET RADIO" : YamahaDirectPlugin.ShortInput(input).ToUpperInvariant();
        var main = !available ? "Ühendus puudub" : error.Length > 0 ? error : !power ? "Ooterežiim" : station.Length > 0 ? station : title.Length > 0 ? title : input;
        var detail = !available || !power || error.Length > 0 || station.Length == 0 || title == station ? "" : title;
        var mainLines = !available ? new[] { "Ühendus", "puudub" } : WrapLines(main, 15, 2); var detailLines = WrapLines(detail, 24, 2);
        var text = string.Join("", mainLines.Select((line, n) => $"<text x='120' y='{(mainLines.Length > 1 ? 91 : 104)+n*28}' font-size='{(mainLines.Length > 1 ? 24 : 27)}' font-weight='650'>{Escape(line)}</text>")) +
            string.Join("", detailLines.Select((line, n) => $"<text x='120' y='{145+n*20}' font-size='15' fill='#BFB6C9'>{Escape(line)}</text>"));
        var amount = Math.Clamp(volume, 0, 100) * 2.7;
        var tip = Point(135 + amount, 108);
        var progress = available && power && volume > 0 ? $"<path d='{Arc(amount, 108)}' fill='none' stroke='url(#volume)' stroke-width='7' stroke-linecap='round'/><circle cx='{N(tip.X)}' cy='{N(tip.Y)}' r='4.5' fill='{Cream}'/>" : "";
        var ticks = string.Join("", Enumerable.Range(0, 19).Select(n => { var a = Point(135+n*15, 98); var b = Point(135+n*15, 100); return $"<path d='M{N(a.X)} {N(a.Y)} L{N(b.X)} {N(b.Y)}' stroke='#45404C' stroke-width='1'/>"; }));
        var transport = muted ? "volume-x" : playback == "Play" ? "pause" : "play";
        string SmallIcon(string name, int x, string color) => $"<g transform='translate({x} 184) scale(.9)' fill='none' stroke='{color}' stroke-width='1.8' stroke-linecap='round' stroke-linejoin='round'>{Shape(name)}</g>";
        var status = PowerLabel(available, power);
        var statusColor = PowerColor(available, power);
        var headingColor = available && power ? accent : statusColor;
        var controlsColor = available && power ? muted ? Violet : accent : Gray;
        return $"<svg xmlns='http://www.w3.org/2000/svg' width='240' height='240' viewBox='0 0 240 240'><defs><radialGradient id='bg'><stop stop-color='#19131E'/><stop offset='1' stop-color='#050507'/></radialGradient><linearGradient id='volume' x1='0' y1='1' x2='1' y2='0'><stop stop-color='{(muted ? Violet : "#FF6E52")}'/><stop offset='1' stop-color='{accent}'/></linearGradient></defs><circle cx='120' cy='120' r='120' fill='black'/><circle cx='120' cy='120' r='104' fill='url(#bg)'/><path d='{Arc(270,108)}' fill='none' stroke='#29232E' stroke-width='7' stroke-linecap='round'/>{ticks}{progress}<g text-anchor='middle' font-family='Segoe UI' fill='{Cream}'><text x='120' y='36' font-size='{(available ? 13 : 11)}' font-weight='700' letter-spacing='1' fill='{statusColor}'>{Escape(status)}</text><text x='120' y='60' font-size='11' letter-spacing='2.4' fill='{headingColor}'>{Escape(heading)}</text>{text}</g>{SmallIcon("skip-back", 64, "#81768D")}{SmallIcon(transport,109,controlsColor)}{SmallIcon("skip-forward",154,"#81768D")}</svg>";
    }
    internal static BitmapImage Wheel(bool available, bool power, bool muted, int volume, string input, string station, string title, string playback, string error)
    {
        using var vector = BitmapImage.FromSvg(WheelSvg(available, power, muted, volume, input, station, title, playback, error));
        return BitmapImage.FromArray(Convert.FromBase64String(vector.ToBitmapBase64String(240)));
    }
    internal static BitmapImage Icon(string icon, string label, string color = White, string detail = "")
    {
        var words = label.Split(' '); var line1 = ""; var line2 = "";
        foreach (var word in words) { if ((line1 + " " + word).Trim().Length <= 15 && line2.Length == 0) line1 = (line1 + " " + word).Trim(); else line2 = (line2 + " " + word).Trim(); }
        var svg = $"<svg xmlns='http://www.w3.org/2000/svg' width='180' height='180' viewBox='0 0 180 180'><rect width='180' height='180' fill='#000000'/><g transform='translate(46 12) scale(3.65)' fill='none' stroke='{color}' stroke-width='1.9' stroke-linecap='round' stroke-linejoin='round'>{Shape(icon)}</g>" +
            $"<g fill='{color}' font-family='Segoe UI' font-weight='600' text-anchor='middle'><text x='90' y='128' font-size='22'>{Escape(line1)}</text><text x='90' y='152' font-size='22'>{Escape(line2)}</text><text x='90' y='174' font-size='16'>{Escape(detail.Length > 22 ? detail[..22] : detail)}</text></g></svg>";
        return BitmapImage.FromSvg(svg);
    }
    internal static BitmapImage Status(bool available, bool power, string input, string station, string title, string playback, string error)
    {
        var color = !available || error.Length > 0 ? Red : Cyan;
        var top = !available ? "OFFLINE" : !power ? "OOTEREŽIIM" : input;
        var main = error.Length > 0 ? error : station.Length > 0 ? station : title;
        var detail = error.Length > 0 || station.Length == 0 || title == station ? "" : title;
        static string[] Wrap(string text, int width, int count)
        {
            var lines = new List<string>(); var rest = text.Trim();
            while (rest.Length > 0 && lines.Count < count)
            {
                if (rest.Length <= width) { lines.Add(rest); break; }
                var cut = rest.LastIndexOf(' ', width, width);
                if (cut < width / 2) cut = width;
                var line = rest[..cut].Trim(); rest = rest[cut..].Trim();
                lines.Add(lines.Count == count - 1 ? line.TrimEnd('.') + "…" : line);
            }
            return lines.ToArray();
        }
        var mainLines = Wrap(main.Length == 0 ? "Meedia puudub" : main, 17, 2);
        var detailLines = Wrap(detail, 22, 2);
        var content = string.Join("", mainLines.Select((line, n) => $"<text x='90' y='{72+n*26}' font-size='22' font-weight='600'>{Escape(line)}</text>")) +
            string.Join("", detailLines.Select((line, n) => $"<text x='90' y='{126+n*20}' font-size='16' fill='{White}'>{Escape(line)}</text>"));
        using var vector = BitmapImage.FromSvg($"<svg xmlns='http://www.w3.org/2000/svg' width='180' height='180' viewBox='0 0 180 180'><rect width='180' height='180' fill='black'/><g text-anchor='middle' font-family='Segoe UI' fill='{color}'><text x='90' y='32' font-size='16'>{Escape(top)}</text>{content}<text x='90' y='166' font-size='13' fill='{Gray}'>{Escape(playback == "Play" ? "ESITAB" : "PEATATUD")}</text></g></svg>");
        // WheelToolGeneric1x1 composites a bitmap: return actual PNG pixels, not SVG bytes.
        return BitmapImage.FromArray(Convert.FromBase64String(vector.ToBitmapBase64String(180)));
    }
}

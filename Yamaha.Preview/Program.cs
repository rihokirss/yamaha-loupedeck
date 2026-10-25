using System.Reflection;
using System.Runtime.Loader;

var root = Path.GetFullPath(args.Length > 0 ? args[0] : ".");
var service = @"C:\Program Files\Logi\LogiPluginService";
AssemblyLoadContext.Default.Resolving += (_, name) =>
{
    foreach (var dir in new[] { service, Path.Combine(root, "Yamaha.Loupedeck/bin/Release") })
    {
        var path = Path.Combine(dir, name.Name + ".dll");
        if (File.Exists(path)) return AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
    }
    return null;
};
AssemblyLoadContext.Default.ResolvingUnmanagedDll += (_, name) =>
{
    foreach (var path in new[] { Path.Combine(service, name), Path.Combine(service, name + ".dll"), Path.Combine(service, "runtimes/win-x64/native", name + ".dll") })
        if (File.Exists(path)) return System.Runtime.InteropServices.NativeLibrary.Load(path);
    return IntPtr.Zero;
};
var plugin = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(root, "Yamaha.Loupedeck/bin/Release/YamahaDirectPlugin.dll"));
var art = plugin.GetType("Loupedeck.YamahaDirectPlugin.Artwork")!;
var api = Assembly.Load("PluginApi");
void SaveRaster(object image, string path, int width = 180)
{
    var encoded = (string)image.GetType().GetMethod("ToBitmapBase64String", [typeof(int)])!.Invoke(image, [width])!;
    File.WriteAllBytes(path, Convert.FromBase64String(encoded));
}
var flags = BindingFlags.NonPublic | BindingFlags.Static;
var output = Path.Combine(root, "docs/icons"); Directory.CreateDirectory(output);
var names = new[] { "Toide", "Vaigista", "Sisendid", "Meedia / FM", "Sky Plus", "Raadio 2", "Raadio Kuku", "Vikerraadio", "Power Hit Radio", "Raadio Elmar", "NRJ", "Star FM" };
for (var n = 0; n < names.Length; n++)
{
    var icon = n == 0 ? "power" : n == 1 ? "volume-2" : n == 2 ? "cable" : n == 3 ? "sliders-horizontal" : "radio";
    var colors = new[] { "#7BD89A", "#FFAD47", "#AB93FA", "#FF7597", "#FFAD47", "#AB93FA", "#FF7597", "#7BD89A", "#FF815E", "#D7C171", "#F47182", "#A9A2F5" };
    var image = n == 0 ? art.GetMethod("Power", flags)!.Invoke(null, [true, true])! : n >= 4 ? art.GetMethod("StationGlyph", flags)!.Invoke(null, [n == 6, true])! : art.GetMethod("GlowGlyph", flags)!.Invoke(null, [icon, colors[n], false])!;
    var target = Path.Combine(output, $"button-{n+1:00}.png");
    SaveRaster(image, target);
    Console.WriteLine(target);
}
var status = art.GetMethod("Wheel", flags)!.Invoke(null, [true, true, false, 35, "NET RADIO", "Raadio Kuku", "Hetkel eetris: EMALAEV", "Play", ""])!;
SaveRaster(status, Path.Combine(output, "wheel-status.png"), 240);
foreach (var (available, power, label, name, color) in new[] {
    (true, true, "SEES", "on", "#7BD89A"),
    (true, false, "OOTEL", "standby", "#94A3B8"),
    (false, false, "ÜHENDUS PUUDUB", "offline", "#EF4444"),
    (false, true, "ÜHENDUS PUUDUB", "offline-last-on", "#EF4444") })
{
    var actualLabel = (string)art.GetMethod("PowerLabel", flags)!.Invoke(null, [available, power])!;
    var actualColor = (string)art.GetMethod("PowerColor", flags)!.Invoke(null, [available, power])!;
    if (actualLabel != label || actualColor != color) throw new Exception("Incorrect power visual state: " + name);
    var powerSvg = (string)art.GetMethod("PowerSvg", flags)!.Invoke(null, [available, power])!;
    if (label.Split(' ').Any(word => !powerSvg.Contains(">" + word + "<")) || !powerSvg.Contains(color)) throw new Exception("Missing readable power caption: " + name);
    object[] wheelArgs = [available, power, false, 35, "NET RADIO", "Raadio Kuku", "Hetkel eetris", "Play", ""];
    var wheelSvg = (string)art.GetMethod("WheelSvg", flags)!.Invoke(null, wheelArgs)!;
    if (!wheelSvg.Contains(">" + label + "<")) throw new Exception("Wheel status differs from power button: " + name);
    if (!available && wheelSvg.Contains("Hetkel eetris")) throw new Exception("Offline view shows stale media");
    if (!power && wheelSvg.Contains("#7BD89A")) throw new Exception("Standby view still shows green on-state");
    if (name != "offline-last-on")
    {
        SaveRaster(art.GetMethod("Power", flags)!.Invoke(null, [available, power])!, Path.Combine(output, "power-" + name + ".png"));
        if (name != "on") SaveRaster(art.GetMethod("Wheel", flags)!.Invoke(null, wheelArgs)!, Path.Combine(output, "wheel-" + name + ".png"), 240);
    }
}
Console.WriteLine("PASS power visuals: SEES / OOTEL / ÜHENDUS PUUDUB, matching button and wheel, no stale online state after disconnect");
var combined = (string)api.GetType("Loupedeck.CombinedString")!.GetMethod("GetString", [typeof(string), typeof(string)])!.Invoke(null, ["YamahaDirect", "WheelToolYamahaNowPlaying"])!;
Console.WriteLine("Wheel template identifier: " + combined);

// Exercise real SDK callbacks with an owner set before Plugin.Load, as in the SDK lifecycle.
var ownerType = plugin.GetType("Loupedeck.YamahaDirectPlugin.YamahaDirectPlugin")!;
var owner = Activator.CreateInstance(ownerType)!;
var sdkActions = new List<object>();
foreach (var name in new[] { "ReceiverCommand", "VolumeAdjustment", "SourceAdjustment", "TrackAdjustment", "InputsFolder", "MediaFolder", "TrackPressFolder" })
{
    var type = plugin.GetType("Loupedeck.YamahaDirectPlugin." + name)!;
    var action = Activator.CreateInstance(type)!;
    var property = type.GetProperty("Plugin", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
    property.DeclaringType!.GetProperty("Plugin", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.SetValue(action, owner);
    var method = type.GetMethod("Load", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
    method.Invoke(action, null); sdkActions.Add(action);
    if (!name.EndsWith("Folder"))
    {
        var bridge = api.GetType("Loupedeck.PluginDynamicAction")!.GetField("k", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var delegates = bridge.FieldType.GetConstructors().Single().GetParameters().Select((p, index) =>
        {
            var parameters = p.ParameterType.GetMethod("Invoke")!.GetParameters().Select(v => System.Linq.Expressions.Expression.Parameter(v.ParameterType)).ToArray();
            System.Linq.Expressions.Expression body = index == 2 ? System.Linq.Expressions.Expression.Call(typeof(Probe).GetMethod("OnBridge")!) : System.Linq.Expressions.Expression.Empty();
            return (object)System.Linq.Expressions.Expression.Lambda(p.ParameterType, body, parameters).Compile();
        }).ToArray();
        bridge.SetValue(action, Activator.CreateInstance(bridge.FieldType, delegates));
    }
}
var listeners = (Delegate?)ownerType.GetField("Updated", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner);
if (listeners?.GetInvocationList().Length != 7) throw new Exception("SDK feedback subscriptions missing before Plugin.Load");
listeners.DynamicInvoke();
if (Probe.Values != 3) throw new Exception($"Expected all three dial callbacks; got {Probe.Values}");
var volumeListener = (Delegate?)ownerType.GetField("VolumeUpdated", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner);
if (volumeListener?.GetInvocationList().Length != 1) throw new Exception("Immediate volume subscription missing");
volumeListener.DynamicInvoke();
if (Probe.Values != 4) throw new Exception("Immediate SDK volume notification missing");
var labels = sdkActions[0].GetType().GetMethod("GetCommandDisplayName", BindingFlags.NonPublic | BindingFlags.Instance)!;
var size = Enum.Parse(api.GetType("Loupedeck.PluginImageSize")!, "Width90Pixels");
if ((string?)labels.Invoke(sdkActions[0], ["station0", size]) != "Sky Plus") throw new Exception("Friendly station caption missing");
Console.WriteLine("PASS SDK: 7 feedback subscriptions before plugin load, 3 dial notifications, isolated immediate volume notification, friendly station caption");

public static class Probe
{
    public static int Values;
    public static void OnBridge() => Values++;
}

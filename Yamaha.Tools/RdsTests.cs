using System.Text.Json;
using System.Xml.Linq;
using Yamaha.Core;

internal static class RdsTests
{
    private static XElement Info() => XElement.Parse("<Play_Info><Feature_Availability>Ready</Feature_Availability><Preset><Preset_Sel>1</Preset_Sel></Preset><Tuning><Band>FM</Band><Freq><Current><Val>9540</Val><Exp>2</Exp><Unit>MHz</Unit></Current></Freq></Tuning><Signal_Info><Tuned>Assert</Tuned></Signal_Info><Meta_Info><Program_Service>SKYPLUS </Program_Service><Radio_Text_A></Radio_Text_A><Radio_Text_B>TEDDY SWIMS - Mr. Know It All</Radio_Text_B></Meta_Info></Play_Info>");
    private static void Set(XElement info, string element, string value) => info.Descendants(element).First().Value = value;
    public static async Task Run()
    {
        var info = Info(); var rds = Rds.Read(info);
        Tests.Assert(rds is { Tuned: true, Frequency: "95.4", Station: "SKYPLUS", Text: "TEDDY SWIMS - Mr. Know It All" }, "RDS reads live-shaped PS, trims padding and falls back from empty A to Radio Text B");
        Set(info, "Radio_Text_A", "  Päeva\r\nsaade  ");
        Tests.Assert(Rds.Read(info).Text == "Päeva saade", "RDS A takes priority when present; whitespace is normalized");
        Set(info, "Radio_Text_A", "---");
        Tests.Assert(Rds.Read(info).Text.StartsWith("TEDDY"), "placeholder Radio Text A does not suppress valid B");
        Set(info, "Tuned", "Negate");
        Tests.Assert(Rds.Read(info) is { Tuned: false, Station: "", Text: "" }, "untuned receiver does not expose stale RDS metadata");
        Set(info, "Tuned", "Assert"); Set(info, "Band", "AM");
        Tests.Assert(Rds.Read(info) is { Tuned: false, Frequency: "" }, "AM reception cannot populate FM name cache");
        Tests.Assert(Rds.PresetFrequency("1 : FM 95.40 MHz") == "95.4" && Rds.PresetFrequency("FM 101,60 MHz") == "101.6" && Rds.PresetFrequency("AM 1089 kHz") == "", "preset and live frequency keys use the same canonical FM scale");
        Tests.Assert(Rds.Clean(" \0 Päev\t&ouml;&ouml; ") == "Päev öö" && Rds.Clean("PÃ¤ev") == "Päev" && Rds.Clean("____") == "", "RDS normalization removes padding/control characters without losing accents");

        var now = DateTimeOffset.Parse("2026-10-25T10:00:00Z");
        var settings = new ReceiverSettings(); var learner = new RdsStationLearner();
        Tests.Assert(!learner.Observe(rds, settings, now) && !learner.Observe(rds, settings, now.AddSeconds(2)) && settings.FmStationNames.Count == 0, "RDS first frames and tune-settling interval do not learn names");
        var oldSnapshot = settings.FmStationNames;
        Tests.Assert(learner.Observe(rds, settings, now.AddSeconds(4)) && settings.FmStationNames["95.4"] == "SKYPLUS" && oldSnapshot.Count == 0, "settled repeated PS is learned with a stable copy-on-write settings snapshot");
        Tests.Assert(!learner.Observe(rds, settings, now.AddSeconds(6)), "unchanged RDS name does not repeatedly trigger persistence");
        var changedFrequency = rds with { Frequency = "97.8" };
        Tests.Assert(!learner.Observe(changedFrequency, settings, now.AddSeconds(7)) && !learner.Observe(changedFrequency, settings, now.AddSeconds(9)) && !settings.FmStationNames.ContainsKey("97.8"), "stale PS immediately after tuning is not assigned to a new frequency");
        changedFrequency = changedFrequency with { Station = "RAADIO 2" };
        Tests.Assert(!learner.Observe(changedFrequency, settings, now.AddSeconds(10)) && learner.Observe(changedFrequency, settings, now.AddSeconds(12)) && settings.FmStationNames["97.8"] == "RAADIO 2", "new station must independently settle before its name is learned");
        learner.Observe(rds with { Tuned = false, Station = "" }, settings, now.AddSeconds(13));
        Tests.Assert(settings.FmStationNames["95.4"] == "SKYPLUS" && !learner.Observe(rds, settings, now.AddSeconds(20)), "signal loss resets learning but preserves previously learned names");
        var restored = JsonSerializer.Deserialize<ReceiverSettings>(JsonSerializer.Serialize(settings))!;
        var fm = new ReceiverState(Available: true, Power: true, Input: "TUNER", Preset: 1, Frequency: "95.4");
        PresetItem[] presets = [new(1, "1 : FM 95.40 MHz"), new(4, "4 : FM 97.80 MHz"), new(8, "8 : FM 101.60 MHz")];
        var slots = SourceControls.Slots(fm, restored, presets);
        Tests.Assert(slots[0].Label == "SKYPLUS" && slots[0].Detail == "95.4" && slots[1].Label == "RAADIO 2" && slots[2].Label == "FM 101.60 MHz", "restored learned names label preset buttons; unlearned frequencies remain visible");
        Tests.Assert(SourceControls.Menu(fm, restored, presets)[0].Label == "SKYPLUS", "FM menu uses the same learned station names as main buttons");
        Tests.Assert(SourceControls.Slots(fm, restored, [new(1, "1 : FM 101.60 MHz")])[0].Label == "FM 101.60 MHz" && !SourceControls.Slots(fm with { Frequency = "97.8" }, restored, presets)[0].Active, "reprogrammed preset cannot inherit another frequency's name or active highlight");

        var clock = new TestClock(now); var fake = new FakeTransport { Input = "TUNER", TunerInfo = Info() };
        using var receiver = new Receiver(new ReceiverSettings { Host = "127.0.0.1", SystemId = "TEST" }, fake, timeProvider: clock);
        var saved = 0; receiver.SettingsChanged += () => saved++;
        await receiver.RefreshAsync(); clock.Now = now.AddSeconds(2); await receiver.RefreshAsync(); clock.Now = now.AddSeconds(4); await receiver.RefreshAsync();
        Tests.Assert(receiver.State is { Station: "SKYPLUS", Frequency: "95.4" } && receiver.State.Title.StartsWith("TEDDY") && saved == 1 && receiver.Settings.FmStationNames["95.4"] == "SKYPLUS", "receiver polling exposes RDS B and emits persistence once after learning");
        Set(fake.TunerInfo, "Program_Service", ""); Set(fake.TunerInfo, "Radio_Text_B", ""); await receiver.RefreshAsync();
        Tests.Assert(receiver.State.Station == "SKYPLUS" && receiver.State.Title == "", "temporary RDS loss uses cached station name but clears old program text");
        Set(fake.TunerInfo, "Tuned", "Negate"); Set(fake.TunerInfo, "Program_Service", "OLD NAME"); Set(fake.TunerInfo, "Radio_Text_B", "OLD SHOW"); await receiver.RefreshAsync();
        Tests.Assert(receiver.State.Station == "FM 95.4 MHz" && receiver.State.Title == "", "untuned wheel falls back to frequency instead of stale station/program");
        fake.Input = "OPTICAL"; await receiver.RefreshAsync();
        Tests.Assert(receiver.State.Frequency == "" && receiver.State.Title == "" && receiver.State.Station == "" && fake.Writes.Count == 0, "RDS learning and source changes issue no PUT and never retune the receiver");
    }
}

internal sealed class TestClock(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now = now;
    public override DateTimeOffset GetUtcNow() => Now;
}

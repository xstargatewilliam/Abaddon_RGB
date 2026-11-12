using AbaddonRGB.Core;
using AbaddonRGB.Core.Automation;
using AbaddonRGB.Core.Engines;

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception("Test failed: " + message);
}
var zones = new[] { new LightZone("Addressable Header", 3) };
var a = DeviceIdentity.Create("ASRock B450M", vendor: "ASRock", serial: "123", ledCount: 3, zones: zones);
var b = DeviceIdentity.Create("  asrock   b450m ", vendor: "asrock", serial: "123", ledCount: 4);
Check(a == b, "Same serial remains stable regardless of topology and whitespace");
Check(a != DeviceIdentity.Create("ASRock B450M", vendor: "ASRock", serial: "124"), "Different serial must differ");
Check(DeviceIdentity.Create("RAM", ledCount: 8) != DeviceIdentity.Create("RAM", ledCount: 12), "Fallback topology distinguishes models");

var config = new AppConfig
{
    Overrides = new List<TargetColor> { new() { Device = "ASRock B450M", Color = "#AABBCC" } },
    DisabledDevices = new List<string> { "ASRock B450M" }
};
config.Profiles[0].Overrides.Add(new TargetColor { Device = "ASRock B450M", Color = "#FF0000" });
Check(config.BindDeviceIdentity("ASRock B450M", a), "Legacy data migrated");
Check(config.Overrides[0].DeviceId == a, "Override receives permanent ID");
Check(config.Profiles[0].Overrides[0].DeviceId == a, "Profile receives permanent ID");
Check(config.IsDisabled("renamed", a), "Disabled device remains disabled when renamed");
Check(!config.BindDeviceIdentity("ASRock B450M", a), "Migration is idempotent");

var device = new LightDevice("native", 0, "renamed", zones, 3, a);
var colors = ColorComposer.Build(device, new RgbColor(0, 0, 0), 100, config.Overrides, Array.Empty<ZoneCorrection>());
Check(colors.Length == 3 && colors.All(x => x.ToHex() == "#AABBCC"),
    "Override survives device rename and engine change");
Check(Scheduler.ActiveAt(new[]
{
    new ScheduleRule { Time = "07:00", Profile = "Dia" },
    new ScheduleRule { Time = "22:00", Profile = "Noite" }
}, TimeSpan.FromHours(1))?.Profile == "Noite", "Schedule wraps midnight");

var release = """
{
  "tag_name": "v0.5.0",
  "assets": [
    { "name": "AbaddonRGB.exe", "browser_download_url": "https://example.com/app.exe" },
    { "name": "AbaddonRGB.exe.sha256", "browser_download_url": "https://example.com/app.sha256" }
  ]
}
""";
Check(Updater.ParseLatestRelease(release)?.ChecksumUrl == "https://example.com/app.sha256",
    "Updater requires accompanying checksum");
Check(Updater.ParseLatestRelease(release.Replace("AbaddonRGB.exe.sha256", "missing")) is null,
    "Unverified release must not be selected");
Console.WriteLine("AbaddonRGB.Core smoke tests passed.");

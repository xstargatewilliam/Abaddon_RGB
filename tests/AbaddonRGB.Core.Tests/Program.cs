using AbaddonRGB.Core;
using AbaddonRGB.Core.Automation;
using AbaddonRGB.Core.Engines;
using AbaddonRGB.Core.Engines.Native;

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
// ---- Fase 1: teclado Logitech com cor por tecla (0x8080), com um teclado simulado
var keyboard = new FakeG512();
var backend = new FakeBackend(keyboard);
var driver = new LogitechKeyboardDriver(backend);
var detected = driver.Detect("native", 0);
Check(detected.Count == 1, "Native driver finds the simulated G512");
var kb = detected[0];
Check(kb.Zones.Count == 2 && kb.Zones[0].Name == "Teclas" && kb.Zones[0].LedCount == FakeG512.MainKeys.Length,
    "Main key zone lists every key the keyboard reported");
Check(kb.Zones[1].Name == "Indicadores" && kb.Zones[1].LedCount == 2, "Only the two real G512 indicators are kept");
Check(kb.HardwareKey == "usb:046D:C33C", "Hardware key comes from the HID path");
Check(keyboard.Writes.All(w => w[0] == 0x11), "Discovery only sends requests, never color writes");

var paint = Enumerable.Repeat(new RgbColor(0x12, 0x34, 0x56), kb.LedCount).ToArray();
driver.SetColors(kb, paint);
var frames = keyboard.Writes.Where(w => w[0] == 0x12).ToList();
Check(frames.Count == (FakeG512.MainKeys.Length + 13) / 14 + 1, "Keys go out 14 per very-long frame, per key type");
Check(frames.All(f => f[1] == 0xFF && f[2] == FakeG512.Index && f[3] == (0x30 | HidPpChannel.SoftwareId)),
    "Frames use fn3 SetKeyColors on the discovered feature index");
var firstFrame = frames[0];
Check(firstFrame[4] == 0x00 && firstFrame[5] == 0x01 && firstFrame[7] == 14 &&
      firstFrame[8] == FakeG512.MainKeys[0] && firstFrame[9] == 0x12 && firstFrame[10] == 0x34 && firstFrame[11] == 0x56,
    "Frame layout: keyType, count, then keyId R G B");
Check(keyboard.Painted.Count == kb.LedCount && keyboard.Painted.Values.All(c => c == (0x12, 0x34, 0x56)),
    "Every key received the color");
Check(keyboard.Flushes == 2, "Busy flush is retried until the keyboard applies it");

keyboard.Unplugged = true;
var threw = false;
try { driver.SetColors(kb, paint); } catch (IOException) { threw = true; }
Check(threw, "Unplugged keyboard surfaces as an error");

Check(new LightDevice("openrgb", 0, "x", zones, 3, "id",
    @"HID: \\?\hid#vid_046d&pid_c33c&mi_01&col03#8&1&0&0002#{4d1e55b2}").HardwareKey == "usb:046D:C33C",
    "OpenRGB HID location maps to the same hardware key");
Check(new HidCollectionInfo(@"\\?\hid#vid_046d&pid_c33c&mi_01&col03#8&1e76927f&0&0002#{x}", 0x46D, 0xC33C, 20, 20, "", "").InterfaceKey ==
      new HidCollectionInfo(@"\\?\hid#vid_046d&pid_c33c&mi_01&col04#8&1e76927f&0&0003#{x}", 0x46D, 0xC33C, 64, 64, "", "").InterfaceKey,
    "Collections of the same interface pair up");

// ---- Motor nativo assume a peça que o OpenRGB também via, herdando o ID dela
var takeoverConfig = new AppConfig { UseNativeDrivers = false };
var openRgbKb = new LightDevice("openrgb", 3, "G512 RGB MECHANICAL GAMING KEYBOARD", new[] { new LightZone("Keyboard", 105) }, 105,
    "openrgb-id-g512", @"HID: \\?\hid#vid_046d&pid_c33c&mi_01&col03#8&1&0&0002#{x}");
var ram = new LightDevice("openrgb", 1, "ENE DRAM", new[] { new LightZone("DRAM", 5) }, 5, "ram-id", "I2C: PIIX4 #0, Address 0x71");
var fakeOpenRgb = new StaticEngine("openrgb", openRgbKb, ram);
var nativeEngine = new NativeEngine(new INativeDriver[] { new LogitechKeyboardDriver(new FakeBackend(new FakeG512())) });
takeoverConfig.Overrides.Add(new TargetColor { Device = openRgbKb.Name, DeviceId = "openrgb-id-g512", Color = "#00FF00" });
using (var controller = new LightingController(takeoverConfig, new ILightingEngine[] { fakeOpenRgb, nativeEngine }, new AppLog(null)))
{
    Check(controller.Connect(out _), "Controller connects");
    Check(!nativeEngine.IsConnected && controller.Devices.Count == 2, "Native engine stays off until enabled");
    Check(takeoverConfig.HardwareIds["usb:046D:C33C"] == "openrgb-id-g512", "OpenRGB ID remembered per USB device");

    controller.SetNativeEnabled(true);
    var keyboards = controller.Devices.Where(d => d.HardwareKey == "usb:046D:C33C").ToList();
    Check(keyboards.Count == 1 && keyboards[0].EngineId == "native", "Only the native copy of the keyboard remains");
    Check(keyboards[0].StableId == "openrgb-id-g512", "Native keyboard inherits the OpenRGB ID");
    Check(controller.Devices.Any(d => d.Name == "ENE DRAM" && d.EngineId == "openrgb"), "Other parts stay on OpenRGB");
    var results = controller.Apply();
    Check(results.All(r => r.Success), "Apply works across both engines");
    Check(fakeOpenRgb.Painted.ContainsKey(1) && !fakeOpenRgb.Painted.ContainsKey(3), "OpenRGB no longer writes to the keyboard");

    controller.SetNativeEnabled(false);
    Check(controller.Devices.Single(d => d.HardwareKey == "usb:046D:C33C").EngineId == "openrgb", "Turning native off hands the keyboard back");
}

// ---- Efeitos
var purple = new RgbColor(160, 32, 240);
var breatheLow = Effects.ColorAt(LightEffect.Breathing, 5, 0, purple, 0, 0);
var breatheHigh = Effects.ColorAt(LightEffect.Breathing, 5, (11.0 / 5 + 0.9) / 2, purple, 0, 0);
Check(breatheLow.R < 20 && breatheHigh == purple, "Breathing goes from dim to full base color");
Check(Effects.ColorAt(LightEffect.Spectrum, 5, 0, purple, 0.3, 1) == Effects.ColorAt(LightEffect.Spectrum, 5, 0, purple, 0.9, 7),
    "Spectrum paints every LED the same at a given instant");
Check(Effects.ColorAt(LightEffect.Wave, 5, 0, purple, 0.0, 0) != Effects.ColorAt(LightEffect.Wave, 5, 0, purple, 0.5, 0),
    "Wave varies along the PC");
var starColors = Enumerable.Range(0, 200).Select(k => Effects.ColorAt(LightEffect.Stars, 5, 1.0, purple, 0, k)).ToList();
Check(starColors.Any(c => c.G > 150) && starColors.Count(c => c == starColors[0]) > 100, "Stars: mostly dim background, a few bright LEDs");
Check(Effects.FromHue(0) == new RgbColor(255, 0, 0) && Effects.FromHue(1.0 / 3) == new RgbColor(0, 255, 0), "Hue wheel");

var waveDevice = new LightDevice("x", 0, "Teclado", new[] { new LightZone("Teclas", 10), new LightZone("Logo", 1) }, 11, "kb");
var ownLogo = new[] { new TargetColor { Device = "Teclado", DeviceId = "kb", Zone = "Logo", Color = "#FFFFFF" } };
var waveColors = ColorComposer.Build(waveDevice, purple, 100, ownLogo, Array.Empty<ZoneCorrection>(), null, false,
    led => Effects.ColorAt(LightEffect.Wave, 5, 0, purple, led / 11.0, led));
Check(waveColors.Take(10).Distinct().Count() > 3 && waveColors[10] == new RgbColor(255, 255, 255),
    "Effect animates the general color, own colors stay fixed");

var effectConfig = new AppConfig { Effect = LightEffect.Spectrum, EffectSpeed = 8 };
effectConfig.Profiles[0].Effect = LightEffect.Breathing;
var effectEngine = new StaticEngine("openrgb",
    new LightDevice("openrgb", 0, "ENE DRAM", new[] { new LightZone("DRAM", 5) }, 5, "ram-a"),
    new LightDevice("openrgb", 1, "Fixa", new[] { new LightZone("Z", 1) }, 1, "fixa"));
effectConfig.Overrides.Add(new TargetColor { Device = "Fixa", DeviceId = "fixa", Color = "#123456" });
using (var controller = new LightingController(effectConfig, new ILightingEngine[] { effectEngine }, new AppLog(null)))
{
    controller.Connect(out _);
    controller.Apply(effectFrame: true);
    Check(effectEngine.Painted.Count == 2, "First effect frame paints everything");
    effectEngine.Painted.Clear();
    Thread.Sleep(80);
    controller.Apply(effectFrame: true);
    Check(effectEngine.Painted.ContainsKey(0) && !effectEngine.Painted.ContainsKey(1),
        "Later frames skip parts whose color did not change");
    controller.LoadProfile(effectConfig.Profiles[0].Name);
    Check(effectConfig.Effect == LightEffect.Breathing, "Profiles carry the effect");
}

Console.WriteLine("AbaddonRGB.Core smoke tests passed.");

sealed class StaticEngine : ILightingEngine
{
    private readonly LightDevice[] _devices;
    public readonly Dictionary<int, RgbColor[]> Painted = new();
    public StaticEngine(string id, params LightDevice[] devices) { Id = id; _devices = devices; }
    public string Id { get; }
    public string DisplayName => Id;
    public bool IsConnected { get; private set; }
    public bool TryConnect(out string? error) { error = null; IsConnected = true; return true; }
    public IReadOnlyList<LightDevice> GetDevices() => _devices;
    public void SetColors(LightDevice device, IReadOnlyList<RgbColor> colors) => Painted[device.Index] = colors.ToArray();
    public void Dispose() { }
}

/// <summary>G512 simulado: responde ao HID++ 2.0 como o teclado real (recursos 0x0000 e 0x8080).</summary>
sealed class FakeG512 : IHidLink
{
    public const byte Index = 0x0C;
    public static readonly byte[] MainKeys = Enumerable.Range(0x04, 100).Select(i => (byte)i).ToArray();
    private static readonly byte[] Indicators = { 0x01, 0x02, 0x03, 0x04, 0x05 };

    private readonly Queue<byte[]> _inbox = new();
    public readonly List<byte[]> Writes = new();
    public readonly Dictionary<(ushort, byte), (byte, byte, byte)> Painted = new();
    public int Flushes;
    public bool Unplugged;
    private bool _busyOnce = true;

    public bool IsOpen => !Unplugged;
    public void Drain() => _inbox.Clear();
    public byte[]? Read(int timeoutMs) => _inbox.Count > 0 ? _inbox.Dequeue() : null;
    public void Dispose() { }

    public void WriteLong(byte[] r) => Handle(r);
    public void WriteVeryLong(byte[] r) => Handle(r);

    private void Reply(byte report, byte feature, byte fn, params byte[] data)
    {
        var buf = new byte[report == 0x12 ? 64 : 20];
        buf[0] = report; buf[1] = 0xFF; buf[2] = feature; buf[3] = fn;
        Array.Copy(data, 0, buf, 4, Math.Min(data.Length, buf.Length - 4));
        _inbox.Enqueue(buf);
    }

    private void Handle(byte[] r)
    {
        if (Unplugged) throw new IOException("device removed");
        Writes.Add(r);
        byte feature = r[2], fn = r[3];
        if (feature == 0x00 && (fn & 0xF0) == 0x00)
        {
            Reply(0x11, 0x00, fn, r[4] == 0x80 && r[5] == 0x80 ? Index : (byte)0, 0, 1);
            return;
        }

        if (feature != Index) { Reply(0x11, 0xFF, feature, fn, 0x02); return; }
        ushort kt = (ushort)((r[4] << 8) | r[5]);
        byte[] ids = kt == 0x0001 ? MainKeys : kt == 0x0040 ? Indicators : Array.Empty<byte>();
        switch (fn & 0xF0)
        {
            case 0x00: Reply(0x11, feature, fn, 0x00, 0x41, 0, 0, 2, 0, 120); break;
            case 0x10: Reply(0x11, feature, fn, (byte)(ids.Length >> 8), (byte)ids.Length, 0, 0, 0); break;
            case 0x20:
            {
                int start = (r[6] << 8) | r[7];
                var data = new List<byte> { r[4], r[5], r[6], r[7] };
                foreach (var id in ids.Skip(start).Take(14)) data.AddRange(new byte[] { id, 0, 0, 0 });
                Reply(0x12, feature, fn, data.ToArray());
                break;
            }
            case 0x30:
            {
                int count = (r[6] << 8) | r[7];
                for (int k = 0; k < count; k++)
                    Painted[(kt, r[8 + k * 4])] = (r[9 + k * 4], r[10 + k * 4], r[11 + k * 4]);
                Reply(0x11, feature, fn);
                break;
            }
            case 0x50:
                Flushes++;
                if (_busyOnce) { _busyOnce = false; Reply(0x11, 0xFF, feature, fn, 0x08); }
                else Reply(0x11, feature, fn);
                break;
        }
    }
}

sealed class FakeBackend : IHidBackend
{
    private readonly FakeG512 _keyboard;
    public FakeBackend(FakeG512 keyboard) => _keyboard = keyboard;

    public IReadOnlyList<HidCollectionInfo> Find(int vendorId, IReadOnlyCollection<int> productIds) => new[]
    {
        new HidCollectionInfo(@"\\?\hid#vid_046d&pid_c33c&mi_01&col02#8&1&0&0001#{x}", 0x046D, 0xC33C, 0, 3, "G512", "SN1"),
        new HidCollectionInfo(@"\\?\hid#vid_046d&pid_c33c&mi_01&col03#8&1&0&0002#{x}", 0x046D, 0xC33C, 20, 20, "G512 RGB MECHANICAL GAMING KEYBOARD", "SN1"),
        new HidCollectionInfo(@"\\?\hid#vid_046d&pid_c33c&mi_01&col04#8&1&0&0003#{x}", 0x046D, 0xC33C, 64, 64, "G512 RGB MECHANICAL GAMING KEYBOARD", "SN1"),
    };

    public IHidLink Open(HidCollectionInfo a, HidCollectionInfo b) => _keyboard;
}

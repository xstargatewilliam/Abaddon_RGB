using System.Diagnostics;
using AbaddonRGB.Core.Automation;
using LibreHardwareMonitor.Hardware;

namespace AbaddonRGB;

/// <summary>
/// Lê as temperaturas do PC. Usa a biblioteca LibreHardwareMonitor (CPU e placa de vídeo)
/// e, como reserva, as "zonas térmicas" que o próprio Windows informa.
/// Só liga a leitura de CPU e GPU: nada de memória nem placa-mãe, para não tocar no SMBus.
/// </summary>
internal sealed class WindowsTemperatureProvider : ITemperatureProvider
{
    private const string ThermalCategory = "Thermal Zone Information";

    private readonly object _lock = new();
    private Computer? _computer;
    private List<PerformanceCounter>? _thermalZones;

    public WindowsTemperatureProvider()
    {
        try
        {
            _computer = new Computer { IsCpuEnabled = true, IsGpuEnabled = true };
            _computer.Open();
        }
        catch
        {
            _computer = null;
        }
    }

    public IReadOnlyList<TemperatureSensor> Read()
    {
        lock (_lock)
        {
            var sensors = new List<TemperatureSensor>();

            if (_computer is not null)
            {
                try
                {
                    foreach (var hardware in _computer.Hardware)
                        Collect(hardware, sensors);
                }
                catch
                {
                    // Uma leitura ruim não pode derrubar a automação.
                }
            }

            ReadThermalZones(sensors);

            // CPU primeiro, depois placa de vídeo, depois o resto.
            return sensors
                .OrderBy(s => s.Name.StartsWith("CPU", StringComparison.Ordinal) ? 0 : s.Name.StartsWith("GPU", StringComparison.Ordinal) ? 1 : 2)
                .ToArray();
        }
    }

    private static void Collect(IHardware hardware, List<TemperatureSensor> sensors)
    {
        hardware.Update();

        var kind = hardware.HardwareType switch
        {
            HardwareType.Cpu => "CPU",
            HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel => "GPU",
            _ => hardware.HardwareType.ToString(),
        };

        foreach (var sensor in hardware.Sensors)
        {
            if (sensor.SensorType != SensorType.Temperature) continue;

            var value = sensor.Value;
            if (value is not null && (float.IsNaN(value.Value) || value.Value <= 0 || value.Value > 150))
                value = null;

            sensors.Add(new TemperatureSensor(
                sensor.Identifier.ToString(),
                $"{kind} {hardware.Name} · {sensor.Name}",
                value));
        }

        foreach (var sub in hardware.SubHardware)
            Collect(sub, sensors);
    }

    private void ReadThermalZones(List<TemperatureSensor> sensors)
    {
        try
        {
            if (_thermalZones is null)
            {
                _thermalZones = new List<PerformanceCounter>();
                if (PerformanceCounterCategory.Exists(ThermalCategory))
                {
                    var category = new PerformanceCounterCategory(ThermalCategory);
                    foreach (var instance in category.GetInstanceNames())
                        _thermalZones.Add(new PerformanceCounter(ThermalCategory, "Temperature", instance, readOnly: true));
                }
            }

            foreach (var counter in _thermalZones)
            {
                // O Windows informa em Kelvin.
                var celsius = counter.NextValue() - 273.15f;
                if (celsius <= 0 || celsius > 150) continue;
                sensors.Add(new TemperatureSensor("tz:" + counter.InstanceName,
                    "Zona térmica do Windows · " + counter.InstanceName.Trim('\\'), celsius));
            }
        }
        catch
        {
            _thermalZones ??= new List<PerformanceCounter>();
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            try { _computer?.Close(); } catch { }
            _computer = null;

            if (_thermalZones is not null)
                foreach (var counter in _thermalZones)
                    try { counter.Dispose(); } catch { }
            _thermalZones = null;
        }
    }
}

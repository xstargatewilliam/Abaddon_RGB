namespace AbaddonRGB.Core.Automation;

/// <summary>Um sensor de temperatura e o valor lido agora (em °C).</summary>
public sealed record TemperatureSensor(string Id, string Name, float? Value);

/// <summary>Quem sabe ler as temperaturas do PC.</summary>
public interface ITemperatureProvider : IDisposable
{
    IReadOnlyList<TemperatureSensor> Read();
}

/// <summary>Transforma temperatura em cor: fria = uma cor, quente = outra, e mistura no meio.</summary>
public sealed class TemperatureAutomation
{
    private const double Smoothing = 0.3;
    private const int MinColorChange = 6;

    private double? _smoothed;
    private RgbColor? _lastSent;

    /// <summary>Temperatura suavizada (evita a cor "tremer" com picos rápidos).</summary>
    public double? Smoothed => _smoothed;

    public void Reset()
    {
        _smoothed = null;
        _lastSent = null;
    }

    /// <summary>Recebe uma leitura e devolve a cor a enviar, ou null se a cor quase não mudou.</summary>
    public RgbColor? Update(float temperature, TemperatureSettings settings)
    {
        _smoothed = _smoothed is null ? temperature : _smoothed + (temperature - _smoothed) * Smoothing;

        var color = ColorFor(_smoothed.Value, settings);
        if (_lastSent is not null && _lastSent.Value.Distance(color) < MinColorChange)
            return null;

        _lastSent = color;
        return color;
    }

    public static RgbColor ColorFor(double temperature, TemperatureSettings settings)
    {
        var cold = RgbColor.ParseOr(settings.ColdColor, new RgbColor(0, 64, 255));
        var hot = RgbColor.ParseOr(settings.HotColor, new RgbColor(255, 0, 0));

        double range = settings.HotTemp - settings.ColdTemp;
        if (range <= 0) return temperature >= settings.HotTemp ? hot : cold;

        return RgbColor.Lerp(cold, hot, (temperature - settings.ColdTemp) / range);
    }
}

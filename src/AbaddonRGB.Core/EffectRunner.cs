/*
 * Abaddon RGB
 * SPDX-License-Identifier: GPL-3.0-or-later
 */
namespace AbaddonRGB.Core;

/// <summary>
/// Roda os efeitos animados: cerca de 15 quadros por segundo, num processo próprio.
/// Fica parado (sem custo) quando o efeito é "Estático".
/// </summary>
public sealed class EffectRunner : IDisposable
{
    private const int FrameMs = 66;

    private readonly LightingController _controller;
    private readonly Func<RgbColor?> _temperature;
    private readonly Action<RgbColor>? _preview;
    private readonly Thread _thread;
    private readonly CancellationTokenSource _stop = new();

    public EffectRunner(LightingController controller, Func<RgbColor?> temperature, Action<RgbColor>? preview = null)
    {
        _controller = controller;
        _temperature = temperature;
        _preview = preview;
        _thread = new Thread(Loop) { IsBackground = true, Name = "AbaddonRGB efeitos" };
        _thread.Start();
    }

    private void Loop()
    {
        var token = _stop.Token;
        while (!token.IsCancellationRequested)
        {
            var config = _controller.Config;
            if (!Effects.IsAnimated(config.Effect) || config.ReadOnly || !_controller.IsConnected)
            {
                token.WaitHandle.WaitOne(250);
                continue;
            }

            var started = Environment.TickCount64;
            try
            {
                _controller.Apply(_temperature(), effectFrame: true);
                _preview?.Invoke(_controller.EffectPreviewColor());
            }
            catch
            {
                // Motor caiu: a reconexão automática cuida disso.
                token.WaitHandle.WaitOne(1000);
                continue;
            }

            var elapsed = (int)(Environment.TickCount64 - started);
            token.WaitHandle.WaitOne(Math.Max(FrameMs - elapsed, 10));
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        _thread.Join(1000);
        _stop.Dispose();
    }
}

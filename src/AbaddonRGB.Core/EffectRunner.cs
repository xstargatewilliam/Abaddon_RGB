/*
 * Abaddon RGB
 * SPDX-License-Identifier: GPL-3.0-or-later
 */
using AbaddonRGB.Core.Engines;

namespace AbaddonRGB.Core;

/// <summary>
/// Envia as cores às peças, de forma suave.
/// - Cada motor (OpenRGB, nativo) tem o seu processo: uma peça lenta não atrasa as outras.
/// - Toda mudança de cor passa por uma transição curta (fade), nos efeitos e quando você troca a cor.
/// - Cada peça tem um limite de envios por segundo (veja <see cref="DeviceSafety"/>):
///   peças desconhecidas e a placa ASRock ficam no limite seguro de 4 por segundo.
/// Fica quase parado quando a cor está fixa e já chegou ao destino.
/// </summary>
public sealed class EffectRunner : IDisposable
{
    private const int FrameMs = 33;
    private const int IdleMs = 300;
    private const double FadeMs = 140;
    private readonly LightingController _controller;
    private readonly Func<RgbColor?> _temperature;
    private readonly Action<RgbColor>? _preview;
    private readonly CancellationTokenSource _stop = new();
    private readonly List<Worker> _workers = new();
    private long _lastPreview;

    public EffectRunner(LightingController controller, Func<RgbColor?> temperature, Action<RgbColor>? preview = null)
    {
        _controller = controller;
        _temperature = temperature;
        _preview = preview;
        foreach (var id in controller.EngineIds)
            _workers.Add(new Worker(this, id));
    }

    /// <summary>Avisa que algo mudou (cor, perfil, peça): os motores enviam já, com transição.</summary>
    public void Kick()
    {
        foreach (var worker in _workers) worker.Kick();
    }

    /// <summary>
    /// Pede o envio e espera uma rodada completa de cada motor conectado.
    /// Retorna o resultado de cada peça (para a mensagem na tela).
    /// </summary>
    public async Task<IReadOnlyList<DeviceResult>> ApplyNowAsync(int timeoutMs = 2000)
    {
        // Reenvia mesmo o que "já estava certo": a peça pode ter perdido a cor (suspensão, reinício).
        foreach (var worker in _workers) worker.ForceResend();
        var waits = _workers.Select(w => w.RequestPassAsync()).ToList();
        Kick();
        await Task.WhenAny(Task.WhenAll(waits), Task.Delay(timeoutMs));

        var results = new List<DeviceResult>();
        for (var i = 0; i < _workers.Count; i++)
        {
            if (waits[i].IsCompleted)
            {
                results.AddRange(_workers[i].LastResults);
                continue;
            }

            // A rodada ainda não terminou (motor lento): não reaproveita o resultado antigo.
            foreach (var item in _controller.Render(_workers[i].EngineId, _temperature()))
                results.Add(item.Skip ?? new DeviceResult(item.Device.Name, item.EngineName, true,
                    "ainda enviando", State: SendState.Pending));
        }

        return results;
    }

    private void MaybePreview()
    {
        if (_preview is null || !Effects.IsAnimated(_controller.Config.Effect)) return;
        var now = Environment.TickCount64;
        if (now - Interlocked.Read(ref _lastPreview) < 80) return;
        Interlocked.Exchange(ref _lastPreview, now);
        try { _preview(_controller.EffectPreviewColor()); } catch { }
    }

    /// <summary>O que já está aceso numa peça e o que foi enviado por último.</summary>
    private sealed class DeviceState
    {
        public double[] Shown = Array.Empty<double>();
        public RgbColor[]? Sent;
        public long LastStep;
        public long LastSend;
    }

    private sealed class Worker
    {
        private readonly EffectRunner _owner;
        private readonly string _engineId;
        private readonly AutoResetEvent _kick = new(false);
        private readonly Dictionary<string, DeviceState> _state = new();
        private readonly object _passLock = new();
        private readonly List<(int Serial, TaskCompletionSource<bool> Done)> _waiting = new();
        private int _requested;
        private int _completed;
        private int _seenVersion = -1;

        public IReadOnlyList<DeviceResult> LastResults { get; private set; } = Array.Empty<DeviceResult>();

        public string EngineId => _engineId;

        public Worker(EffectRunner owner, string engineId)
        {
            _owner = owner;
            _engineId = engineId;
            new Thread(Loop) { IsBackground = true, Name = "AbaddonRGB envio " + engineId }.Start();
        }

        public void Kick() => _kick.Set();

        private volatile bool _force;

        public void ForceResend() => _force = true;

        /// <summary>Completa quando terminar uma rodada que começou depois deste pedido.</summary>
        public Task RequestPassAsync()
        {
            lock (_passLock)
            {
                var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                _waiting.Add((++_requested, done));
                return done.Task;
            }
        }

        private int StartPass()
        {
            lock (_passLock) return _requested;
        }

        private void FinishPass(int serial, IReadOnlyList<DeviceResult> results)
        {
            LastResults = results;
            List<TaskCompletionSource<bool>> ready;
            lock (_passLock)
            {
                _completed = Math.Max(_completed, serial);
                ready = _waiting.Where(w => w.Serial <= _completed).Select(w => w.Done).ToList();
                _waiting.RemoveAll(w => w.Serial <= _completed);
            }

            foreach (var done in ready) done.TrySetResult(true);
        }

        private void Loop()
        {
            var token = _owner._stop.Token;
            var controller = _owner._controller;

            while (!token.IsCancellationRequested)
            {
                var started = Environment.TickCount64;
                var serial = StartPass();
                var busy = false;
                var results = new List<DeviceResult>();

                try
                {
                    if (controller.IsEngineConnected(_engineId))
                    {
                        if (controller.DevicesVersion != _seenVersion)
                        {
                            // Peças mudaram ou o motor reconectou: reenvia tudo, sem transição.
                            _seenVersion = controller.DevicesVersion;
                            _state.Clear();
                        }

                        busy = Step(controller, results);
                        if (Effects.IsAnimated(controller.Config.Effect)) busy = true;
                        _owner.MaybePreview();
                    }
                    else
                    {
                        _state.Clear();
                    }
                }
                catch
                {
                    // Erro inesperado: tenta de novo na próxima rodada.
                }

                FinishPass(serial, results);

                var wait = busy
                    ? Math.Max(FrameMs - (int)(Environment.TickCount64 - started), 5)
                    : IdleMs;
                WaitHandle.WaitAny(new[] { _kick, token.WaitHandle }, wait);
            }
        }

        /// <summary>Uma rodada: aproxima o que está aceso do destino e envia. True = ainda em transição.</summary>
        private bool Step(LightingController controller, List<DeviceResult> results)
        {
            var moving = false;
            var now = Environment.TickCount64;
            if (_force)
            {
                _force = false;
                foreach (var known in _state.Values) known.Sent = null;
            }

            var rendered = controller.Render(_engineId, _owner._temperature());

            foreach (var item in rendered)
            {
                if (item.Skip is not null)
                {
                    results.Add(item.Skip);
                    continue;
                }

                var device = item.Device;
                var target = item.Colors!;
                var key = device.StableId + "|" + device.Index;
                if (!_state.TryGetValue(key, out var state) || state.Shown.Length != target.Length * 3)
                {
                    // Primeira vez: vai direto para a cor, sem transição (não sabemos o que estava aceso).
                    state = new DeviceState { Shown = new double[target.Length * 3], LastStep = now };
                    for (var i = 0; i < target.Length; i++)
                    {
                        state.Shown[i * 3] = target[i].R;
                        state.Shown[i * 3 + 1] = target[i].G;
                        state.Shown[i * 3 + 2] = target[i].B;
                    }

                    _state[key] = state;
                }

                // Transição exponencial: a cada rodada percorre uma parte do caminho até o destino.
                var dt = Math.Clamp(now - state.LastStep, 1, 250);
                state.LastStep = now;
                var alpha = 1 - Math.Exp(-dt / FadeMs);
                var output = new RgbColor[target.Length];
                var arrived = true;
                for (var i = 0; i < target.Length; i++)
                {
                    output[i] = new RgbColor(
                        Approach(state.Shown, i * 3, target[i].R, alpha, ref arrived),
                        Approach(state.Shown, i * 3 + 1, target[i].G, alpha, ref arrived),
                        Approach(state.Shown, i * 3 + 2, target[i].B, alpha, ref arrived));
                }

                if (!arrived) moving = true;

                if (state.Sent is not null && state.Sent.AsSpan().SequenceEqual(output))
                {
                    results.Add(new DeviceResult(device.Name, item.EngineName, true, State: SendState.Unchanged));
                    continue;
                }

                if (now - state.LastSend < DeviceSafety.MinIntervalMs(device))
                {
                    // Ainda não pode enviar (limite por segundo): fica para a próxima rodada.
                    moving = true;
                    results.Add(new DeviceResult(device.Name, item.EngineName, true, "aguardando", State: SendState.Pending));
                    continue;
                }

                var result = controller.Send(device, output);
                state.LastSend = now;
                state.Sent = result.Success ? output : null;
                results.Add(result);
            }

            return moving;
        }

        private static byte Approach(double[] shown, int index, byte target, double alpha, ref bool arrived)
        {
            var value = shown[index] + (target - shown[index]) * alpha;
            if (Math.Abs(target - value) < 0.6) value = target;
            else arrived = false;
            shown[index] = value;
            return (byte)Math.Round(value);
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
    }
}

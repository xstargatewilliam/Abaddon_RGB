/*
 * Abaddon RGB
 * SPDX-License-Identifier: GPL-3.0-or-later
 */
using System.Collections.Concurrent;
using AbaddonRGB.Core.Engines.Native;
using HidSharp;

namespace AbaddonRGB;

/// <summary>Acesso aos aparelhos HID do Windows pela biblioteca HidSharp.</summary>
internal sealed class HidSharpBackend : IHidBackend
{
    public IReadOnlyList<HidCollectionInfo> Find(int vendorId, IReadOnlyCollection<int> productIds)
    {
        var result = new List<HidCollectionInfo>();
        foreach (var device in DeviceList.Local.GetHidDevices(vendorId))
        {
            if (!productIds.Contains(device.ProductID)) continue;

            int output, input;
            try
            {
                output = device.GetMaxOutputReportLength();
                input = device.GetMaxInputReportLength();
            }
            catch
            {
                continue;
            }

            result.Add(new HidCollectionInfo(device.DevicePath, device.VendorID, device.ProductID,
                output, input, Safe(device.GetProductName), Safe(device.GetSerialNumber)));
        }

        return result;
    }

    private static string Safe(Func<string> read)
    {
        try { return read() ?? ""; }
        catch { return ""; }
    }

    public IHidLink Open(HidCollectionInfo longCollection, HidCollectionInfo veryLongCollection)
    {
        var all = DeviceList.Local.GetHidDevices(longCollection.VendorId, longCollection.ProductId).ToList();
        var longDevice = all.FirstOrDefault(d => d.DevicePath == longCollection.Path)
                         ?? throw new IOException("coleção 0x11 não encontrada");
        var veryLongDevice = all.FirstOrDefault(d => d.DevicePath == veryLongCollection.Path)
                             ?? throw new IOException("coleção 0x12 não encontrada");
        return new Link(longDevice, veryLongDevice);
    }

    /// <summary>Duas coleções abertas e uma fila única com tudo o que chega delas.</summary>
    private sealed class Link : IHidLink
    {
        private readonly HidStream _long;
        private readonly HidStream _veryLong;
        private readonly int _longOut;
        private readonly int _veryLongOut;
        private readonly BlockingCollection<byte[]> _queue = new(new ConcurrentQueue<byte[]>(), 256);
        private readonly CancellationTokenSource _stop = new();
        private readonly object _writeLock = new();
        private volatile bool _open = true;

        public Link(HidDevice longDevice, HidDevice veryLongDevice)
        {
            if (!longDevice.TryOpen(out _long)) throw new IOException("não consegui abrir a coleção 0x11");
            if (!veryLongDevice.TryOpen(out _veryLong))
            {
                _long.Dispose();
                throw new IOException("não consegui abrir a coleção 0x12");
            }

            _longOut = longDevice.GetMaxOutputReportLength();
            _veryLongOut = veryLongDevice.GetMaxOutputReportLength();
            StartReader(_long, longDevice.GetMaxInputReportLength());
            StartReader(_veryLong, veryLongDevice.GetMaxInputReportLength());
        }

        public bool IsOpen => _open;

        private void StartReader(HidStream stream, int inputLength)
        {
            stream.ReadTimeout = 500;
            var thread = new Thread(() =>
            {
                var buffer = new byte[Math.Max(inputLength, 1)];
                while (!_stop.IsCancellationRequested)
                {
                    try
                    {
                        var count = stream.Read(buffer, 0, buffer.Length);
                        if (count <= 0) continue;
                        var report = buffer.AsSpan(0, count).ToArray();
                        // Fila cheia: descarta o mais antigo.
                        while (!_queue.TryAdd(report)) _queue.TryTake(out _);
                    }
                    catch (TimeoutException)
                    {
                    }
                    catch
                    {
                        // Aparelho desconectado ou fechado.
                        _open = false;
                        break;
                    }
                }
            })
            {
                IsBackground = true,
                Name = "AbaddonRGB HID reader",
            };
            thread.Start();
        }

        public void WriteLong(byte[] report) => Write(_long, report, _longOut);

        public void WriteVeryLong(byte[] report) => Write(_veryLong, report, _veryLongOut);

        private void Write(HidStream stream, byte[] report, int length)
        {
            var buffer = new byte[Math.Max(length, report.Length)];
            Array.Copy(report, buffer, report.Length);
            try
            {
                lock (_writeLock) stream.Write(buffer, 0, buffer.Length);
            }
            catch
            {
                _open = false;
                throw;
            }
        }

        public byte[]? Read(int timeoutMs)
        {
            try
            {
                return _queue.TryTake(out var report, Math.Max(timeoutMs, 0), _stop.Token) ? report : null;
            }
            catch (OperationCanceledException)
            {
                return null;
            }
        }

        public void Drain()
        {
            while (_queue.TryTake(out _)) { }
        }

        public void Dispose()
        {
            _open = false;
            _stop.Cancel();
            try { _long.Dispose(); } catch { }
            try { _veryLong.Dispose(); } catch { }
        }
    }
}

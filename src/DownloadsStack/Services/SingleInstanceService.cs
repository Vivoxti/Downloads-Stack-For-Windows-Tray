using System.IO;
using System.IO.Pipes;
using System.Security.Principal;
using DownloadsStack.Interop;

namespace DownloadsStack.Services;

internal sealed class SingleInstanceService : IDisposable
{
    private readonly Mutex _mutex;
    private readonly string _pipeName;
    private readonly CancellationTokenSource _stop = new();
    private Task? _listener;
    public bool IsPrimary { get; }

    public SingleInstanceService()
    {
        var sid = WindowsIdentity.GetCurrent().User!.Value;
        _pipeName = "DownloadsStack." + sid;
        _mutex = new Mutex(true, @"Local\" + _pipeName, out var created);
        IsPrimary = created;
    }

    public void Listen(Action<bool> show)
    {
        _listener = Task.Run(async () =>
        {
            while (!_stop.IsCancellationRequested)
            {
                try
                {
                    using var pipe = new NamedPipeServerStream(_pipeName, PipeDirection.InOut, 1,
                        PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    await pipe.WaitForConnectionAsync(_stop.Token);
                    using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                    requestTimeout.CancelAfter(TimeSpan.FromSeconds(5));
                    await pipe.WriteAsync(BitConverter.GetBytes(Environment.ProcessId), requestTimeout.Token);
                    var command = new byte[1];
                    await pipe.ReadExactlyAsync(command, requestTimeout.Token);
                    show(command[0] == 1);
                    await pipe.WriteAsync(new byte[] { 1 }, requestTimeout.Token);
                }
                catch (OperationCanceledException) { if (_stop.IsCancellationRequested) break; }
                catch (IOException ex) { LocalLog.Write("Pipe", ex); }
            }
        });
    }

    public async Task SignalAsync(bool cursor)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var pipe = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await pipe.ConnectAsync(timeout.Token);
        var pid = new byte[4];
        await pipe.ReadExactlyAsync(pid, timeout.Token);
        NativeMethods.AllowSetForegroundWindow(BitConverter.ToUInt32(pid));
        await pipe.WriteAsync(new byte[] { cursor ? (byte)1 : (byte)0 }, timeout.Token);
        var ack = new byte[1];
        await pipe.ReadExactlyAsync(ack, timeout.Token);
    }

    public void Dispose()
    {
        _stop.Cancel();
        if (_listener is not null) _ = _listener.ContinueWith(_ => _stop.Dispose(), TaskScheduler.Default);
        else _stop.Dispose();
        if (IsPrimary) _mutex.ReleaseMutex();
        _mutex.Dispose();
        // The listener owns its pipe; cancellation unblocks WaitForConnectionAsync.
    }
}

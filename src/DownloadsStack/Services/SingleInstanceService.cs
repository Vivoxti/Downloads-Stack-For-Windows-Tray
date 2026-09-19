using System.IO;
using System.IO.Pipes;
using System.Security.Principal;
using DownloadsStack.Interop;

namespace DownloadsStack.Services;

/// <summary>What a second launch asks the instance that is already running to do.</summary>
internal enum InstanceCommand : byte
{
    /// <summary>Show the list, opened from the keyboard.</summary>
    ShowKeyboard = 0,
    /// <summary>Show the list, opened by a click, so it follows the pointer.</summary>
    ShowCursor = 1,
    /// <summary>Exit. Sent by the installer, which cannot replace an executable the tray still holds.</summary>
    Exit = 2,
}

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

    public void Listen(Action<InstanceCommand> handle)
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
                    handle((InstanceCommand)command[0]);
                    await pipe.WriteAsync(new byte[] { 1 }, requestTimeout.Token);
                }
                catch (OperationCanceledException) { if (_stop.IsCancellationRequested) break; }
                catch (IOException ex) { LocalLog.Write("Pipe", ex); }
            }
        });
    }

    public async Task SignalAsync(InstanceCommand command)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var pipe = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await pipe.ConnectAsync(timeout.Token);
        var pid = new byte[4];
        await pipe.ReadExactlyAsync(pid, timeout.Token);
        NativeMethods.AllowSetForegroundWindow(BitConverter.ToUInt32(pid));
        await pipe.WriteAsync(new byte[] { (byte)command }, timeout.Token);
        var ack = new byte[1];
        await pipe.ReadExactlyAsync(ack, timeout.Token);
    }

    /// <summary>
    /// Asks the instance running for this user to exit, and waits until it really has. The mutex is held
    /// for the life of that process, so it disappearing is the only report that the executable is free
    /// again — which is what an installer about to replace or delete it has to wait for.
    /// </summary>
    /// <returns>Whether nothing of ours is running any more.</returns>
    public static async Task<bool> RequestExitAsync(TimeSpan timeout)
    {
        string name;
        using (var instance = new SingleInstanceService())
        {
            name = instance._pipeName;
            if (instance.IsPrimary) return true; // Nothing was running: the mutex was ours to create.
            await instance.SignalAsync(InstanceCommand.Exit);
        }
        for (var deadline = DateTime.UtcNow + timeout; DateTime.UtcNow < deadline; await Task.Delay(100))
        {
            if (!Mutex.TryOpenExisting(@"Local\" + name, out var running)) return true;
            running.Dispose();
        }
        return false;
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

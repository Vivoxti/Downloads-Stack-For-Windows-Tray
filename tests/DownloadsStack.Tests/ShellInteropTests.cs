using System.Runtime.InteropServices;
using DownloadsStack.Interop;
using DownloadsStack.Services;

namespace DownloadsStack.Tests;

public class ShellInteropTests
{
    [Fact]
    public async Task RealFileBindsToNativeShellDataObjectOnSta()
    {
        using var files = new TestDirectory(); var path = files.File("кириллица & пробелы.txt");
        var result = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            IShellItem? item = null; System.Runtime.InteropServices.ComTypes.IDataObject? data = null;
            try
            {
                NativeMethods.SHCreateItemFromParsingName(path, 0, NativeMethods.ShellItemId, out item);
                item.BindToHandler(0, NativeMethods.DataObjectHandler, NativeMethods.DataObjectId, out data);
                // CF_HDROP (15) must expose the actual file, not a text payload.
                var format = new System.Runtime.InteropServices.ComTypes.FORMATETC { cfFormat = 15, dwAspect = System.Runtime.InteropServices.ComTypes.DVASPECT.DVASPECT_CONTENT, lindex = -1, tymed = System.Runtime.InteropServices.ComTypes.TYMED.TYMED_HGLOBAL };
                Assert.Equal(0, data.QueryGetData(ref format)); result.SetResult();
            }
            catch (Exception ex) { result.SetException(ex); }
            finally { if (data is not null) Marshal.ReleaseComObject(data); if (item is not null) Marshal.ReleaseComObject(item); }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); await result.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }
    [Fact]
    public void KnownFolderReturnsAbsoluteDownloadsPath() => Assert.True(System.IO.Path.IsPathFullyQualified(ShellService.GetDownloadsPath()));

    [Fact]
    public async Task RealFileProvidesExplorerCommandsAndNativeMenuIsReleased()
    {
        using var files = new TestDirectory();
        var path = files.File("кириллица & пробелы.txt");
        var result = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var initialized = NativeMethods.OleInitialize(0) >= 0;
            try
            {
                var menu = new ShellContextMenu(path);
                var handle = menu.Handle;
                try
                {
                    Assert.True(ContextMenuNative.IsMenu(handle));
                    var commands = new List<string>();
                    for (var i = 0; i < ContextMenuNative.GetMenuItemCount(handle); i++)
                    {
                        var id = ContextMenuNative.GetMenuItemID(handle, i);
                        if (id is 0 or uint.MaxValue) continue;
                        var buffer = Marshal.AllocCoTaskMem(1024);
                        try
                        {
                            if (menu.Context.GetCommandString(id - 1, 4, 0, buffer, 512) == 0)
                                commands.Add(Marshal.PtrToStringUni(buffer) ?? "");
                        }
                        finally { Marshal.FreeCoTaskMem(buffer); }
                    }
                    Assert.Contains(commands, c => c.Equals("copy", StringComparison.OrdinalIgnoreCase));
                    Assert.Contains(commands, c => c.Equals("properties", StringComparison.OrdinalIgnoreCase));
                }
                finally { menu.Dispose(); }
                Assert.False(ContextMenuNative.IsMenu(handle));
                result.SetResult();
            }
            catch (Exception ex) { result.SetException(ex); }
            finally { if (initialized) NativeMethods.OleUninitialize(); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        await result.Task.WaitAsync(TimeSpan.FromSeconds(15));
    }
}

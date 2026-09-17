using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using static FolderBox.App.Shell.NativeMethods;

namespace FolderBox.App.Shell;

/// <summary>
/// A <see cref="SystemBackdrop"/> that paints nothing: the window's backdrop surface becomes a fully
/// transparent composition brush, so the desktop shows through anywhere XAML does not draw.
/// This is what lets tile/panel windows look like free-floating desktop icons.
/// </summary>
internal sealed class TransparentBackdrop : SystemBackdrop
{
    private static Windows.UI.Composition.Compositor? s_compositor;

    private static Windows.UI.Composition.Compositor Compositor
    {
        get
        {
            if (s_compositor is null)
            {
                DispatcherQueueHelper.EnsureSystemDispatcherQueue();
                s_compositor = new Windows.UI.Composition.Compositor();
            }
            return s_compositor;
        }
    }

    protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop connectedTarget, XamlRoot xamlRoot)
    {
        base.OnTargetConnected(connectedTarget, xamlRoot);
        connectedTarget.SystemBackdrop = Compositor.CreateColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0));
    }

    protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop disconnectedTarget)
    {
        disconnectedTarget.SystemBackdrop = null;
        base.OnTargetDisconnected(disconnectedTarget);
    }
}

/// <summary>
/// Windows.UI.Composition needs a Windows.System.DispatcherQueue on the calling thread; WinUI 3 threads
/// only have a Microsoft.UI.Dispatching one, so we create the system-level queue once.
/// </summary>
internal static class DispatcherQueueHelper
{
    private static IntPtr s_controller;

    public static void EnsureSystemDispatcherQueue()
    {
        if (Windows.System.DispatcherQueue.GetForCurrentThread() is not null) return;
        if (s_controller != IntPtr.Zero) return;

        var options = new DispatcherQueueOptions
        {
            dwSize = System.Runtime.InteropServices.Marshal.SizeOf<DispatcherQueueOptions>(),
            threadType = 2,     // DQTYPE_THREAD_CURRENT
            apartmentType = 2,  // DQTAT_COM_STA
        };
        var hr = CreateDispatcherQueueController(options, out s_controller);
        if (hr < 0) throw new System.Runtime.InteropServices.COMException("CreateDispatcherQueueController failed", hr);
    }
}

using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using FolderBox.Core.Logging;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using static FolderBox.App.Shell.NativeMethods;

namespace FolderBox.App.Shell;

/// <summary>Raw BGRA (premultiplied) pixels of a shell icon.</summary>
internal sealed record IconPixels(int Width, int Height, byte[] Bgra);

/// <summary>
/// Resolves the real Windows Shell icon for files and folders (IShellItemImageFactory, with an
/// SHGetFileInfo fallback) and converts it into a WinUI <see cref="ImageSource"/>.
/// Extraction runs on the thread pool; bitmaps are created on the UI thread and cached per size.
/// Generic file types are cached by extension so a folder with 10,000 .png files costs one lookup.
/// </summary>
internal sealed class IconService
{
    private static readonly HashSet<string> PerFileExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".lnk", ".ico", ".url", ".cur", ".appref-ms", ".scr", ".msc",
    };

    private readonly ConcurrentDictionary<string, Task<IconPixels?>> _pixelCache = new();
    private readonly Dictionary<string, ImageSource> _imageCache = new();
    private readonly ConcurrentDictionary<string, string> _typeNameCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly DispatcherQueue _dispatcher;

    public IconService(DispatcherQueue dispatcher)
    {
        _dispatcher = dispatcher;
    }

    /// <summary>Returns a cached or freshly extracted icon. Must be awaited on the UI thread.</summary>
    public async Task<ImageSource?> GetIconAsync(string path, bool isDirectory, int physicalSize)
    {
        var key = CacheKey(path, isDirectory, physicalSize);
        lock (_imageCache)
        {
            if (_imageCache.TryGetValue(key, out var cached)) return cached;
        }

        var pixels = await _pixelCache.GetOrAdd(key, _ => ExtractAsync(path, isDirectory, physicalSize)).ConfigureAwait(true);
        if (pixels is null) return null;

        // WriteableBitmap must be created and filled on the UI thread.
        if (!_dispatcher.HasThreadAccess)
        {
            var tcs = new TaskCompletionSource<ImageSource?>();
            _dispatcher.TryEnqueue(() => tcs.SetResult(CreateBitmap(key, pixels)));
            return await tcs.Task.ConfigureAwait(false);
        }
        return CreateBitmap(key, pixels);
    }

    /// <summary>Raw premultiplied BGRA pixels of the icon (for native drag images).</summary>
    public Task<IconPixels?> GetPixelsAsync(string path, bool isDirectory, int physicalSize)
    {
        var key = CacheKey(path, isDirectory, physicalSize);
        return _pixelCache.GetOrAdd(key, _ => ExtractAsync(path, isDirectory, physicalSize));
    }

    private ImageSource? CreateBitmap(string key, IconPixels pixels)
    {
        lock (_imageCache)
        {
            if (_imageCache.TryGetValue(key, out var cached)) return cached;
            try
            {
                var bmp = new WriteableBitmap(pixels.Width, pixels.Height);
                using (var stream = bmp.PixelBuffer.AsStream())
                {
                    stream.Write(pixels.Bgra, 0, pixels.Bgra.Length);
                }
                bmp.Invalidate();
                _imageCache[key] = bmp;
                return bmp;
            }
            catch (Exception ex)
            {
                Log.Warn($"Icon bitmap creation failed for {key}: {ex.Message}");
                return null;
            }
        }
    }

    private static string CacheKey(string path, bool isDirectory, int size)
    {
        if (isDirectory) return $"d|{path}|{size}";
        var ext = Path.GetExtension(path);
        if (string.IsNullOrEmpty(ext) || PerFileExtensions.Contains(ext)) return $"f|{path}|{size}";
        return $"e|{ext.ToLowerInvariant()}|{size}";
    }

    private Task<IconPixels?> ExtractAsync(string path, bool isDirectory, int size)
    {
        // Shell icon handlers for shortcuts (.lnk/.url) only produce the real icon on an STA thread —
        // on MTA threads they silently return the generic document icon — so extraction runs on a
        // dedicated STA worker instead of the thread pool.
        return StaWorker.RunAsync(() => Extract(path, isDirectory, size));
    }

    /// <summary>Single background STA thread that executes queued shell calls in order.</summary>
    private static class StaWorker
    {
        private static readonly System.Collections.Concurrent.BlockingCollection<Action> Queue = new();
        private static readonly Thread Thread = CreateThread();

        private static Thread CreateThread()
        {
            var t = new Thread(() =>
            {
                foreach (var work in Queue.GetConsumingEnumerable())
                {
                    try { work(); } catch (Exception ex) { Log.Debug("STA icon work failed: " + ex.Message); }
                }
            })
            { IsBackground = true, Name = "FolderBox.ShellIcons" };
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            return t;
        }

        public static Task<T> RunAsync<T>(Func<T> func)
        {
            _ = Thread; // ensure started
            var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            Queue.Add(() =>
            {
                try { tcs.SetResult(func()); }
                catch (Exception ex) { tcs.SetException(ex); }
            });
            return tcs.Task;
        }
    }

    private static IconPixels? Extract(string path, bool isDirectory, int size)
    {
        // 1) Modern API: honours custom folder icons, file type handlers, DPI-specific sizes.
        try
        {
            var item = ShellItem.FromPath(path);
            var factory = (IShellItemImageFactory)item;
            var hr = factory.GetImage(new SIZE { cx = size, cy = size }, SIIGBF_ICONONLY | SIIGBF_BIGGERSIZEOK, out var hbm);
            if (hr >= 0 && hbm != IntPtr.Zero)
            {
                try { return FromHBitmap(hbm); }
                finally { DeleteObject(hbm); }
            }
        }
        catch (Exception ex)
        {
            Log.Debug($"IShellItemImageFactory failed for {path}: {ex.Message}");
        }

        // 2) Fallback: classic SHGetFileInfo by attributes (works even when the file is gone).
        try
        {
            var info = new SHFILEINFOW();
            var flags = SHGFI_ICON | SHGFI_USEFILEATTRIBUTES | (size > 24 ? SHGFI_LARGEICON : SHGFI_SMALLICON);
            var attrs = isDirectory ? FILE_ATTRIBUTE_DIRECTORY : FILE_ATTRIBUTE_NORMAL;
            var r = SHGetFileInfoW(path, attrs, ref info, (uint)Marshal.SizeOf<SHFILEINFOW>(), flags);
            if (r != IntPtr.Zero && info.hIcon != IntPtr.Zero)
            {
                try
                {
                    if (GetIconInfo(info.hIcon, out var ii))
                    {
                        try
                        {
                            return ii.hbmColor != IntPtr.Zero ? FromHBitmap(ii.hbmColor) : null;
                        }
                        finally
                        {
                            if (ii.hbmColor != IntPtr.Zero) DeleteObject(ii.hbmColor);
                            if (ii.hbmMask != IntPtr.Zero) DeleteObject(ii.hbmMask);
                        }
                    }
                }
                finally { DestroyIcon(info.hIcon); }
            }
        }
        catch (Exception ex)
        {
            Log.Debug($"SHGetFileInfo failed for {path}: {ex.Message}");
        }
        return null;
    }

    /// <summary>Copies a 32bpp HBITMAP into a premultiplied BGRA buffer.</summary>
    private static IconPixels? FromHBitmap(IntPtr hbm)
    {
        if (GetObject(hbm, Marshal.SizeOf<BITMAP>(), out var bm) == 0) return null;
        int w = bm.bmWidth, h = Math.Abs(bm.bmHeight);
        if (w <= 0 || h <= 0) return null;

        var header = new BITMAPINFOHEADER
        {
            biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
            biWidth = w,
            biHeight = -h, // top-down
            biPlanes = 1,
            biBitCount = 32,
            biCompression = BI_RGB,
        };
        var buffer = new byte[w * h * 4];
        var hdc = CreateCompatibleDC(IntPtr.Zero);
        try
        {
            if (GetDIBits(hdc, hbm, 0, (uint)h, buffer, ref header, DIB_RGB_COLORS) == 0) return null;
        }
        finally
        {
            DeleteDC(hdc);
        }

        // Decide whether the alpha channel is meaningful and whether it is premultiplied.
        bool anyAlpha = false, straight = false;
        for (int i = 0; i < buffer.Length; i += 4)
        {
            var a = buffer[i + 3];
            if (a != 0) anyAlpha = true;
            if (a != 255 && (buffer[i] > a || buffer[i + 1] > a || buffer[i + 2] > a)) straight = true;
        }
        if (!anyAlpha)
        {
            // 24-bit source: treat as opaque.
            for (int i = 3; i < buffer.Length; i += 4) buffer[i] = 255;
        }
        else if (straight)
        {
            for (int i = 0; i < buffer.Length; i += 4)
            {
                int a = buffer[i + 3];
                if (a == 255) continue;
                buffer[i] = (byte)(buffer[i] * a / 255);
                buffer[i + 1] = (byte)(buffer[i + 1] * a / 255);
                buffer[i + 2] = (byte)(buffer[i + 2] * a / 255);
            }
        }
        return new IconPixels(w, h, buffer);
    }

    /// <summary>Localized type description such as "PNG File" or "File folder".</summary>
    public string GetTypeName(string path, bool isDirectory)
    {
        var key = isDirectory ? "<dir>" : (Path.GetExtension(path) is { Length: > 0 } e ? e : "<none>");
        if (_typeNameCache.TryGetValue(key, out var cached)) return cached;
        string name;
        try
        {
            var info = new SHFILEINFOW();
            var attrs = isDirectory ? FILE_ATTRIBUTE_DIRECTORY : FILE_ATTRIBUTE_NORMAL;
            var r = SHGetFileInfoW(isDirectory ? "folder" : ("x" + key), attrs, ref info, (uint)Marshal.SizeOf<SHFILEINFOW>(), SHGFI_TYPENAME | SHGFI_USEFILEATTRIBUTES);
            name = r != IntPtr.Zero && !string.IsNullOrWhiteSpace(info.szTypeName) ? info.szTypeName : (isDirectory ? "File folder" : "File");
        }
        catch
        {
            name = isDirectory ? "File folder" : "File";
        }
        _typeNameCache[key] = name;
        return name;
    }

    /// <summary>Loads the stock folder icon as an HICON for the tray. Caller owns the handle.</summary>
    public static IntPtr LoadStockFolderIcon(bool small)
    {
        var info = new SHSTOCKICONINFO { cbSize = (uint)Marshal.SizeOf<SHSTOCKICONINFO>() };
        var hr = SHGetStockIconInfo(SIID_FOLDER, SHGSI_ICON | (small ? SHGSI_SMALLICON : SHGSI_LARGEICON), ref info);
        return hr == 0 ? info.hIcon : IntPtr.Zero;
    }
}

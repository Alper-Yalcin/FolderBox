using System.Runtime.InteropServices;
using FolderBox.Core.Logging;
using static FolderBox.App.Shell.NativeMethods;

namespace FolderBox.App.Shell;

/// <summary>
/// Shows the real Windows Shell context menu (IContextMenu) for one or more items in the same folder.
/// Owner-drawn entries (e.g. "Open with", "Send to") need menu messages forwarded to the handler,
/// which is done through the owner window's subclass while the menu is open.
/// </summary>
internal static class ShellContextMenu
{
    private const uint IdFirst = 1;
    private const uint IdLast = 0x7FFF;

    /// <summary>
    /// Shows the menu at screen coordinates. Returns true if a command was invoked.
    /// <paramref name="onRename"/> is called instead of invoking the shell's "rename" verb, because
    /// renaming is a view operation that the host (our panel) implements inline.
    /// </summary>
    public static bool Show(IntPtr ownerHwnd, WindowSubclass ownerSubclass, IReadOnlyList<string> paths, int screenX, int screenY, Action? onRename)
    {
        if (paths.Count == 0) return false;

        var fullPidls = new List<IntPtr>();
        object? folderObj = null;
        object? menuObj = null;
        IntPtr hmenu = IntPtr.Zero;
        WindowMessageHandler? forwarder = null;

        try
        {
            var childPidls = new IntPtr[paths.Count];
            for (int i = 0; i < paths.Count; i++)
            {
                SHParseDisplayName(paths[i], IntPtr.Zero, out var pidl, 0, out _);
                fullPidls.Add(pidl);
                var iidFolder = ComGuids.IID_IShellFolder;
                SHBindToParent(pidl, ref iidFolder, out var parent, out var child);
                childPidls[i] = child;
                if (folderObj is null) folderObj = parent;
                else Marshal.ReleaseComObject(parent);
            }
            if (folderObj is not IShellFolder folder) return false;

            var iidMenu = ComGuids.IID_IContextMenu;
            folder.GetUIObjectOf(ownerHwnd, (uint)childPidls.Length, childPidls, ref iidMenu, IntPtr.Zero, out menuObj);
            var contextMenu = (IContextMenu)menuObj;

            hmenu = CreatePopupMenu();
            var flags = CMF_NORMAL | CMF_EXPLORE | CMF_CANRENAME;
            if (IsKeyDown(VK_SHIFT)) flags |= CMF_EXTENDEDVERBS;
            var hr = contextMenu.QueryContextMenu(hmenu, 0, IdFirst, IdLast, flags);
            if (hr < 0)
            {
                Log.Warn($"QueryContextMenu failed: 0x{hr:X8}");
                return false;
            }

            // Forward owner-draw / init messages so submenus like "Open with" populate correctly.
            var cm2 = menuObj as IContextMenu2;
            var cm3 = menuObj as IContextMenu3;
            forwarder = (h, msg, wParam, lParam) =>
            {
                if (msg is WM_INITMENUPOPUP or WM_DRAWITEM or WM_MEASUREITEM or WM_MENUCHAR)
                {
                    if (cm3 is not null)
                    {
                        if (cm3.HandleMenuMsg2(msg, wParam, lParam, out var result) == 0)
                            return MessageResult.HandledWith(result);
                    }
                    else if (cm2 is not null && msg != WM_MENUCHAR)
                    {
                        if (cm2.HandleMenuMsg(msg, wParam, lParam) == 0)
                            return MessageResult.HandledZero;
                    }
                }
                return MessageResult.Unhandled;
            };
            ownerSubclass.AddHandler(forwarder);

            SetForegroundWindow(ownerHwnd);
            var cmd = TrackPopupMenuEx(hmenu, TPM_RETURNCMD | TPM_RIGHTBUTTON | TPM_LEFTALIGN, screenX, screenY, ownerHwnd, IntPtr.Zero);
            if (cmd <= 0) return false;

            var verb = GetVerb(contextMenu, (uint)cmd - IdFirst);
            if (string.Equals(verb, "rename", StringComparison.OrdinalIgnoreCase))
            {
                onRename?.Invoke();
                return true;
            }

            var invoke = new CMINVOKECOMMANDINFOEX
            {
                cbSize = Marshal.SizeOf<CMINVOKECOMMANDINFOEX>(),
                fMask = CMIC_MASK_UNICODE | CMIC_MASK_PTINVOKE,
                hwnd = ownerHwnd,
                lpVerb = new IntPtr(cmd - (int)IdFirst),
                lpVerbW = new IntPtr(cmd - (int)IdFirst),
                nShow = SW_SHOWNORMAL,
                ptInvoke = new POINT { X = screenX, Y = screenY },
            };
            var ihr = contextMenu.InvokeCommand(ref invoke);
            if (ihr < 0) Log.Warn($"InvokeCommand '{verb}' failed: 0x{ihr:X8}");
            else Log.Debug($"Shell verb '{verb}' invoked on {paths.Count} item(s)");
            return ihr >= 0;
        }
        catch (Exception ex)
        {
            Log.Error("Shell context menu failed", ex);
            return false;
        }
        finally
        {
            if (forwarder is not null) ownerSubclass.RemoveHandler(forwarder);
            if (hmenu != IntPtr.Zero) DestroyMenu(hmenu);
            if (menuObj is not null) Marshal.FinalReleaseComObject(menuObj);
            if (folderObj is not null) Marshal.FinalReleaseComObject(folderObj);
            foreach (var p in fullPidls) ILFree(p);
        }
    }

    private static string? GetVerb(IContextMenu menu, uint id)
    {
        const int cch = 256;
        var buffer = Marshal.AllocCoTaskMem(cch * 2);
        try
        {
            var hr = menu.GetCommandString(new UIntPtr(id), GCS_VERBW, IntPtr.Zero, buffer, cch);
            return hr >= 0 ? Marshal.PtrToStringUni(buffer) : null;
        }
        catch
        {
            return null;
        }
        finally
        {
            Marshal.FreeCoTaskMem(buffer);
        }
    }
}

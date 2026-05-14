using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace LieblingstypenRemote;

public sealed class GlobalHotkeyManager : IDisposable
{
    private const int WM_HOTKEY = 0x0312;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private readonly IntPtr _hwnd;
    private readonly HwndSource _source;
    private readonly Action<string> _onTrigger;
    private readonly Dictionary<int, string> _idMap = new();
    private int _nextId = 1;

    public GlobalHotkeyManager(IntPtr hwnd, Action<string> onTrigger)
    {
        _hwnd = hwnd;
        _onTrigger = onTrigger;
        _source = HwndSource.FromHwnd(hwnd)
            ?? throw new InvalidOperationException("HwndSource not available — window handle not initialized yet");
        _source.AddHook(WndProc);
    }

    public void SetHotkeys(IEnumerable<HotkeyDef> hotkeys)
    {
        foreach (var id in _idMap.Keys)
        {
            UnregisterHotKey(_hwnd, id);
        }
        _idMap.Clear();

        foreach (var hk in hotkeys)
        {
            var winId = _nextId++;
            if (RegisterHotKey(_hwnd, winId, hk.Modifiers, hk.VirtualKey))
            {
                _idMap[winId] = hk.Id;
            }
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY)
        {
            var winId = wParam.ToInt32();
            if (_idMap.TryGetValue(winId, out var jsId))
            {
                _onTrigger(jsId);
                handled = true;
            }
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        foreach (var id in _idMap.Keys)
        {
            UnregisterHotKey(_hwnd, id);
        }
        _idMap.Clear();
        _source.RemoveHook(WndProc);
    }
}

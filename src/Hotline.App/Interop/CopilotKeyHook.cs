using System.Runtime.InteropServices;
using Hotline.Core.Activation;
using Hotline.Core.Diagnostics;

namespace Hotline.App.Interop;

/// <summary>
/// activation.copilotKey = "rightCtrl": a low-level keyboard hook that applies <see cref="CopilotKeyRemap"/> (the
/// Copilot key's Win+Shift+F23 becomes Right Ctrl). It runs on its own thread with its own message loop, so the hook
/// is never held up by Hotline's UI; it's only installed while the option is on.
/// </summary>
internal sealed class CopilotKeyHook(FileLog log) : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const uint WM_KEYUP = 0x0101, WM_SYSKEYUP = 0x0105, WM_QUIT = 0x0012;
    private const uint INPUT_KEYBOARD = 1, KEYEVENTF_EXTENDEDKEY = 0x0001, KEYEVENTF_KEYUP = 0x0002;
    /// <summary>Marks the keys this hook sends, so it doesn't act on them again.</summary>
    private static readonly nint Marker = 0x484F544C; // "HOTL"

    private readonly Lock _gate = new();
    private Thread? _thread;
    private uint _threadId;
    private HookProc? _proc; // kept alive while installed
    private CopilotKeyRemap _remap = new();

    public bool Enabled
    {
        get { lock (_gate) return _thread is not null; }
        set { if (value) Start(); else Stop(); }
    }

    private void Start()
    {
        lock (_gate)
        {
            if (_thread is not null) return;
            _remap = new CopilotKeyRemap();
            var ready = new ManualResetEventSlim();
            _thread = new Thread(() =>
            {
                _threadId = GetCurrentThreadId();
                _proc = Callback;
                var hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(null), 0);
                ready.Set();
                if (hook == 0) { log.Error($"Copilot key as Right Ctrl: hook failed ({Marshal.GetLastWin32Error()})"); return; }
                log.Info("Copilot key acts as Right Ctrl");
                while (GetMessage(out var msg, 0, 0, 0) > 0) { }
                UnhookWindowsHookEx(hook);
                if (_remap.Held) Send([new KeyStroke(CopilotKeyRemap.RCtrl, Up: true)]); // never leave Ctrl stuck down
                log.Info("Copilot key opens Hotline again");
            }) { IsBackground = true, Name = "Hotline Copilot key hook" };
            _thread.Start();
            ready.Wait(TimeSpan.FromSeconds(2));
        }
    }

    private void Stop()
    {
        Thread? thread;
        lock (_gate)
        {
            thread = _thread;
            if (thread is null) return;
            _thread = null;
            PostThreadMessage(_threadId, WM_QUIT, 0, 0);
        }
        thread.Join(TimeSpan.FromSeconds(2));
    }

    public void Dispose() => Stop();

    private nint Callback(int code, nint wParam, nint lParam)
    {
        if (code >= 0)
        {
            var info = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            if (info.ExtraInfo != Marker)
            {
                var up = (uint)wParam is WM_KEYUP or WM_SYSKEYUP;
                var (suppress, inject) = _remap.OnKey((ushort)info.VkCode, up);
                if (inject.Count > 0) Send(inject);
                if (suppress) return 1;
            }
        }
        return CallNextHookEx(0, code, wParam, lParam);
    }

    private static void Send(IReadOnlyList<KeyStroke> strokes)
    {
        var inputs = strokes.Select(s => new INPUT
        {
            Type = INPUT_KEYBOARD,
            U = new InputUnion
            {
                Keyboard = new KEYBDINPUT
                {
                    Vk = s.Vk, ExtraInfo = Marker,
                    Flags = (s.Up ? KEYEVENTF_KEYUP : 0) | (s.Vk == CopilotKeyRemap.RCtrl ? KEYEVENTF_EXTENDEDKEY : 0), // Right Ctrl is an extended key
                },
            },
        }).ToArray();
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }

    private delegate nint HookProc(int code, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint VkCode, ScanCode, Flags, Time;
        public nint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint Type;
        public InputUnion U;
    }

    // As large as MOUSEINPUT, the biggest member, or SendInput rejects the size.
    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public KEYBDINPUT Keyboard;
        [FieldOffset(0)] public MOUSEINPUT Mouse;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort Vk, Scan;
        public uint Flags, Time;
        public nint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int Dx, Dy;
        public uint MouseData, Flags, Time;
        public nint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public nint Hwnd;
        public uint Message;
        public nint WParam, LParam;
        public uint Time;
        public int PtX, PtY;
        public uint Private;
    }

    [DllImport("user32.dll", SetLastError = true)] private static extern nint SetWindowsHookEx(int idHook, HookProc proc, nint module, uint threadId);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")] private static extern nint CallNextHookEx(nint hook, int code, nint wParam, nint lParam);
    [DllImport("user32.dll")] private static extern int GetMessage(out MSG msg, nint hWnd, uint min, uint max);
    [DllImport("user32.dll")] private static extern bool PostThreadMessage(uint threadId, uint msg, nint wParam, nint lParam);
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, INPUT[] inputs, int size);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern nint GetModuleHandle(string? name);
}

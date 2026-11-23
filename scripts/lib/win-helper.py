#!/usr/bin/env python3
"""Window helper for the Store capture scripts (scripts/lib/store-capture.sh). Windows only, ctypes only: no PowerShell, no
compiler, so it starts in a few milliseconds and never triggers a C# build or a Defender scan.

    win-helper.py front  TITLE_PART PIDFILE      bring the Edge kiosk backdrop (title contains TITLE_PART) to the foreground,
                                                  check it IS the foreground window, write its process id to PIDFILE
    win-helper.py rect   TITLE RECTFILE           write the visible rectangle "x y w h" (physical pixels) of Hotline's window
                                                  with exactly this title
    win-helper.py place  TITLE X Y RECTFILE       move that window to X,Y (no resize, no activation), then write its rectangle
    win-helper.py close  TITLE                    WM_CLOSE that window until it is gone (nothing to do when there is none)

Exit codes: 0 done, 1 window not found, 2 could not bring it to the front.
"""
import csv
import ctypes
import subprocess
import sys
import time
from ctypes import wintypes as wt

user32 = ctypes.WinDLL("user32", use_last_error=True)
kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
dwm = ctypes.WinDLL("dwmapi")
user32.SetProcessDPIAware()  # physical pixels, like the screen grab

EnumProc = ctypes.WINFUNCTYPE(wt.BOOL, wt.HWND, wt.LPARAM)
user32.EnumWindows.argtypes = [EnumProc, wt.LPARAM]
user32.IsWindowVisible.argtypes = [wt.HWND]
user32.IsIconic.argtypes = [wt.HWND]
user32.GetWindowTextW.argtypes = [wt.HWND, wt.LPWSTR, ctypes.c_int]
user32.GetClassNameW.argtypes = [wt.HWND, wt.LPWSTR, ctypes.c_int]
user32.GetWindowThreadProcessId.argtypes = [wt.HWND, ctypes.POINTER(wt.DWORD)]
user32.GetWindowThreadProcessId.restype = wt.DWORD
user32.GetForegroundWindow.restype = wt.HWND
user32.SetForegroundWindow.argtypes = [wt.HWND]
user32.BringWindowToTop.argtypes = [wt.HWND]
user32.ShowWindow.argtypes = [wt.HWND, ctypes.c_int]
user32.AttachThreadInput.argtypes = [wt.DWORD, wt.DWORD, wt.BOOL]
user32.SetWindowPos.argtypes = [wt.HWND, wt.HWND, ctypes.c_int, ctypes.c_int, ctypes.c_int, ctypes.c_int, wt.UINT]
user32.PostMessageW.argtypes = [wt.HWND, wt.UINT, wt.WPARAM, wt.LPARAM]
kernel32.GetCurrentThreadId.restype = wt.DWORD
kernel32.OpenProcess.argtypes = [wt.DWORD, wt.BOOL, wt.DWORD]
kernel32.OpenProcess.restype = wt.HANDLE
kernel32.QueryFullProcessImageNameW.argtypes = [wt.HANDLE, wt.DWORD, wt.LPWSTR, ctypes.POINTER(wt.DWORD)]
kernel32.CloseHandle.argtypes = [wt.HANDLE]
dwm.DwmGetWindowAttribute.argtypes = [wt.HWND, wt.DWORD, ctypes.POINTER(wt.RECT), wt.DWORD]


def windows():
    """Every top-level window: (hwnd, title, class name, process id, visible)."""
    found = []

    def each(hwnd, _):
        title = ctypes.create_unicode_buffer(512)
        cls = ctypes.create_unicode_buffer(256)
        pid = wt.DWORD()
        user32.GetWindowTextW(hwnd, title, 512)
        user32.GetClassNameW(hwnd, cls, 256)
        user32.GetWindowThreadProcessId(hwnd, ctypes.byref(pid))
        found.append((hwnd, title.value, cls.value, pid.value, bool(user32.IsWindowVisible(hwnd))))
        return True

    user32.EnumWindows(EnumProc(each), 0)
    return found


def image_name(pid):
    handle = kernel32.OpenProcess(0x1000, False, pid)  # PROCESS_QUERY_LIMITED_INFORMATION
    if not handle:
        return ""
    try:
        size = wt.DWORD(1024)
        buf = ctypes.create_unicode_buffer(1024)
        return buf.value if kernel32.QueryFullProcessImageNameW(handle, 0, buf, ctypes.byref(size)) else ""
    finally:
        kernel32.CloseHandle(handle)


def hotline_pids():
    out = subprocess.run(["tasklist", "/FI", "IMAGENAME eq Hotline.exe", "/FO", "CSV", "/NH"], capture_output=True, text=True).stdout
    return {int(row[1]) for row in csv.reader(out.splitlines()) if len(row) > 1 and row[0].lower() == "hotline.exe"}


def hotline_window(title):
    pids = hotline_pids()
    for hwnd, text, _cls, pid, visible in windows():
        if visible and pid in pids and text == title:
            return hwnd
    return None


def frame(hwnd):
    r = wt.RECT()
    if dwm.DwmGetWindowAttribute(hwnd, 9, ctypes.byref(r), ctypes.sizeof(r)) != 0:  # DWMWA_EXTENDED_FRAME_BOUNDS
        return None
    return r


def wait_for(find, seconds=8):
    deadline = time.time() + seconds
    while True:
        hwnd = find()
        if hwnd:
            return hwnd
        if time.time() > deadline:
            return None
        time.sleep(0.3)


def write_rect(hwnd, path):
    r = frame(hwnd)
    if r is None or r.left < -10000 or r.right - r.left < 50 or r.bottom - r.top < 50:
        return False
    with open(path, "w", encoding="ascii") as f:
        f.write(f"{r.left} {r.top} {r.right - r.left} {r.bottom - r.top}\n")
    return True


def to_front(hwnd):
    """Brings a window to the foreground. Windows only lets the foreground thread hand over focus, so attach to its input queue
    for the moment (never to our own, and always detach again)."""
    me = kernel32.GetCurrentThreadId()
    for _ in range(10):
        if user32.IsIconic(hwnd):
            user32.ShowWindow(hwnd, 9)  # SW_RESTORE, only for a minimized window
        fg = user32.GetForegroundWindow()
        fg_thread = user32.GetWindowThreadProcessId(fg, None) if fg else 0
        attached = bool(fg_thread and fg_thread != me and user32.AttachThreadInput(me, fg_thread, True))
        try:
            user32.SetForegroundWindow(hwnd)
            user32.BringWindowToTop(hwnd)
        finally:
            if attached:
                user32.AttachThreadInput(me, fg_thread, False)
        time.sleep(0.5)
        if user32.GetForegroundWindow() == hwnd:
            return True
    return False


def main(argv):
    cmd = argv[1]
    if cmd == "front":
        part, pidfile = argv[2], argv[3]

        def find():
            for hwnd, title, cls, pid, visible in windows():
                if visible and cls == "Chrome_WidgetWin_1" and part in title and image_name(pid).lower().endswith("msedge.exe"):
                    return hwnd, pid
            return None

        hit = wait_for(find, 15)
        if not hit:
            return 1
        hwnd, pid = hit
        with open(pidfile, "w", encoding="ascii") as f:
            f.write(str(pid))
        return 0 if to_front(hwnd) else 2
    if cmd == "rect":
        hwnd = wait_for(lambda: hotline_window(argv[2]))
        return 0 if hwnd and write_rect(hwnd, argv[3]) else 1
    if cmd == "place":
        title, x, y, path = argv[2], int(argv[3]), int(argv[4]), argv[5]
        hwnd = wait_for(lambda: hotline_window(title), 10)
        if not hwnd:
            return 1
        user32.SetWindowPos(hwnd, None, x, y, 0, 0, 0x15)  # SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE
        time.sleep(0.8)
        return 0 if write_rect(hwnd, path) else 1
    if cmd == "close":
        title = argv[2]
        for _ in range(10):
            hwnd = hotline_window(title)
            if not hwnd:
                return 0
            user32.PostMessageW(hwnd, 0x10, 0, 0)  # WM_CLOSE: no keystrokes
            time.sleep(0.5)
        return 1 if hotline_window(title) else 0
    print(__doc__)
    return 64


if __name__ == "__main__":
    sys.exit(main(sys.argv))

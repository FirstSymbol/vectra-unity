"""Copy of StudioTools/win/hidden_run.py with NORMAL priority (the original starts Idle; timing players run at normal priority, see agent_rules_v080.md).
Run a program on a desktop of its own, so its window never shows on the owner's screen or takes focus.

usage: python -I hidden_run.py <timeout seconds> <exe> [args...]
Prints "exit <code>" or "killed after <s> s". Normal priority. Unlike a minimized window, a player keeps rendering.
"""
import ctypes
import ctypes.wintypes as wt
import subprocess
import sys

kernel = ctypes.WinDLL("kernel32", use_last_error=True)
kernel.CreateProcessW.argtypes = [wt.LPCWSTR, wt.LPWSTR, ctypes.c_void_p, ctypes.c_void_p, wt.BOOL, wt.DWORD, ctypes.c_void_p, wt.LPCWSTR, ctypes.c_void_p, ctypes.c_void_p]
kernel.GetExitCodeProcess.argtypes = [wt.HANDLE, ctypes.POINTER(wt.DWORD)]
kernel.WaitForSingleObject.argtypes = [wt.HANDLE, wt.DWORD]
kernel.TerminateProcess.argtypes = [wt.HANDLE, wt.UINT]
DESKTOP = "VectraPerfHidden"


class Startup(ctypes.Structure):
    _fields_ = [("cb", wt.DWORD), ("r", wt.LPWSTR), ("d", wt.LPWSTR), ("t", wt.LPWSTR), ("x", wt.DWORD), ("y", wt.DWORD), ("xs", wt.DWORD), ("ys", wt.DWORD), ("xc", wt.DWORD), ("yc", wt.DWORD), ("fill", wt.DWORD), ("flags", wt.DWORD), ("show", wt.WORD), ("r2", wt.WORD), ("r3", ctypes.c_void_p), ("i", wt.HANDLE), ("o", wt.HANDLE), ("e", wt.HANDLE)]


class Info(ctypes.Structure):
    _fields_ = [("process", wt.HANDLE), ("thread", wt.HANDLE), ("pid", wt.DWORD), ("tid", wt.DWORD)]


def main():
    timeout = float(sys.argv[1])
    command = sys.argv[2:]
    if not ctypes.windll.user32.CreateDesktopW(DESKTOP, None, None, 0, 0x10000000, None):  # GENERIC_ALL
        raise OSError(ctypes.get_last_error())
    startup = Startup()
    startup.cb = ctypes.sizeof(Startup)
    startup.d = DESKTOP
    startup.flags = 0x1  # STARTF_USESHOWWINDOW
    startup.show = 4  # SW_SHOWNOACTIVATE
    info = Info()
    line = subprocess.list2cmdline(command)
    if not kernel.CreateProcessW(None, ctypes.create_unicode_buffer(line), None, None, False, 0x00000020, None, None, ctypes.byref(startup), ctypes.byref(info)):  # NORMAL_PRIORITY_CLASS
        raise OSError(ctypes.get_last_error())
    kernel.CloseHandle(info.thread)
    if kernel.WaitForSingleObject(info.process, int(timeout * 1000)) != 0:
        kernel.TerminateProcess(info.process, 1)
        print(f"killed after {timeout:g} s")
        return 1
    code = wt.DWORD()
    kernel.GetExitCodeProcess(info.process, ctypes.byref(code))
    print("exit", code.value)
    return 0


if __name__ == "__main__":
    sys.exit(main())

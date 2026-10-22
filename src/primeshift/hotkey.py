"""Global hotkeys via RegisterHotKey and a Win32 message loop."""

from __future__ import annotations

import ctypes
import logging
import sys
from ctypes import byref, c_uint32
from typing import Callable

log = logging.getLogger(__name__)

MOD_ALT = 0x0001
MOD_CONTROL = 0x0002
MOD_SHIFT = 0x0004
MOD_WIN = 0x0008
MOD_NOREPEAT = 0x4000

WM_HOTKEY = 0x0312
WM_QUIT = 0x0012
ERROR_HOTKEY_ALREADY_REGISTERED = 1409

_MODIFIERS = {
    "ctrl": MOD_CONTROL,
    "control": MOD_CONTROL,
    "alt": MOD_ALT,
    "shift": MOD_SHIFT,
    "win": MOD_WIN,
}

_NAMED_KEYS = {
    "space": 0x20,
    "pageup": 0x21,
    "pagedown": 0x22,
    "end": 0x23,
    "home": 0x24,
    "left": 0x25,
    "up": 0x26,
    "right": 0x27,
    "down": 0x28,
    "insert": 0x2D,
    "delete": 0x2E,
    "pause": 0x13,
}


def _virtual_key(key: str) -> int:
    if key in _NAMED_KEYS:
        return _NAMED_KEYS[key]
    if len(key) == 1 and key.isascii() and key.isalnum():
        return ord(key.upper())
    if key.startswith("f") and key[1:].isdigit() and 1 <= int(key[1:]) <= 24:
        return 0x70 + int(key[1:]) - 1
    if key.startswith("numpad") and key[6:].isdigit() and 0 <= int(key[6:]) <= 9:
        return 0x60 + int(key[6:])
    raise ValueError(f"无法识别的按键: {key!r}")


def parse_hotkey(text: str) -> tuple[int, int]:
    """Parse e.g. "Ctrl+Alt+F11" into (modifiers, virtual key code)."""
    parts = [p.strip().lower() for p in text.split("+")]
    if any(not p for p in parts):
        raise ValueError(f"快捷键格式错误: {text!r}")
    modifiers = 0
    for part in parts[:-1]:
        if part not in _MODIFIERS:
            raise ValueError(f"无法识别的修饰键 {part!r}（可用: ctrl, alt, shift, win）")
        modifiers |= _MODIFIERS[part]
    if not modifiers:
        raise ValueError(f"全局快捷键至少需要一个修饰键: {text!r}")
    return modifiers, _virtual_key(parts[-1])


class _MSG(ctypes.Structure):
    _fields_ = [
        ("hwnd", ctypes.c_void_p),
        ("message", c_uint32),
        ("wParam", ctypes.c_size_t),
        ("lParam", ctypes.c_ssize_t),
        ("time", c_uint32),
        ("pt_x", ctypes.c_int32),
        ("pt_y", ctypes.c_int32),
    ]


def run(bindings: dict[str, Callable[[], None]]) -> None:
    """Register every hotkey in bindings and dispatch them until Ctrl+C."""
    if sys.platform != "win32":
        raise OSError("PrimeShift 只能在 Windows 上运行")
    user32 = ctypes.WinDLL("user32", use_last_error=True)
    kernel32 = ctypes.WinDLL("kernel32")

    actions = {}
    try:
        for hotkey_id, (text, action) in enumerate(bindings.items(), start=1):
            modifiers, vk = parse_hotkey(text)
            if not user32.RegisterHotKey(None, hotkey_id, modifiers | MOD_NOREPEAT, vk):
                err = ctypes.get_last_error()
                if err == ERROR_HOTKEY_ALREADY_REGISTERED:
                    raise OSError(f"快捷键 {text} 已被其他程序（或另一个 PrimeShift）占用")
                raise ctypes.WinError(err)
            actions[hotkey_id] = (text, action)
            log.info("已注册快捷键 %s", text)

        # GetMessageW blocks Python's own Ctrl+C handling, so post WM_QUIT from a
        # console control handler instead.
        thread_id = kernel32.GetCurrentThreadId()
        handler_type = ctypes.WINFUNCTYPE(ctypes.c_int, c_uint32)

        @handler_type
        def on_console_ctrl(_event):
            user32.PostThreadMessageW(thread_id, WM_QUIT, 0, 0)
            return 1

        kernel32.SetConsoleCtrlHandler(on_console_ctrl, 1)

        msg = _MSG()
        while user32.GetMessageW(byref(msg), None, 0, 0) > 0:
            if msg.message != WM_HOTKEY or msg.wParam not in actions:
                continue
            text, action = actions[msg.wParam]
            log.info("触发快捷键 %s", text)
            try:
                action()
            except Exception:
                log.exception("执行快捷键 %s 失败", text)
    finally:
        for hotkey_id in actions:
            user32.UnregisterHotKey(None, hotkey_id)

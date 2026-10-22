"""ctypes bindings for the Windows CCD (Connecting and Configuring Displays) API.

Structure layouts follow wingdi.h. The structures themselves are plain ctypes
definitions and can be imported on any platform; only the functions that call
into user32.dll require Windows.
"""

from __future__ import annotations

import ctypes
import sys
from ctypes import byref, c_int32, c_uint16, c_uint32, c_uint64, c_wchar

# QueryDisplayConfig flags
QDC_ALL_PATHS = 0x00000001
QDC_ONLY_ACTIVE_PATHS = 0x00000002

# SetDisplayConfig flags
SDC_USE_SUPPLIED_DISPLAY_CONFIG = 0x00000020
SDC_VALIDATE = 0x00000040
SDC_APPLY = 0x00000080
SDC_SAVE_TO_DATABASE = 0x00000200
SDC_ALLOW_CHANGES = 0x00000400

# DISPLAYCONFIG_MODE_INFO_TYPE
MODE_INFO_TYPE_SOURCE = 1
MODE_INFO_TYPE_TARGET = 2
MODE_INFO_TYPE_DESKTOP_IMAGE = 3

# DISPLAYCONFIG_DEVICE_INFO_TYPE
DEVICE_INFO_GET_SOURCE_NAME = 1
DEVICE_INFO_GET_TARGET_NAME = 2
DEVICE_INFO_GET_ADAPTER_NAME = 4

DISPLAYCONFIG_PATH_ACTIVE = 0x00000001
DISPLAYCONFIG_PATH_MODE_IDX_INVALID = 0xFFFFFFFF

ERROR_SUCCESS = 0
ERROR_INSUFFICIENT_BUFFER = 122


class LUID(ctypes.Structure):
    _fields_ = [("LowPart", c_uint32), ("HighPart", c_int32)]


class DISPLAYCONFIG_RATIONAL(ctypes.Structure):
    _fields_ = [("Numerator", c_uint32), ("Denominator", c_uint32)]


class DISPLAYCONFIG_PATH_SOURCE_INFO(ctypes.Structure):
    _fields_ = [
        ("adapterId", LUID),
        ("id", c_uint32),
        ("modeInfoIdx", c_uint32),
        ("statusFlags", c_uint32),
    ]


class DISPLAYCONFIG_PATH_TARGET_INFO(ctypes.Structure):
    _fields_ = [
        ("adapterId", LUID),
        ("id", c_uint32),
        ("modeInfoIdx", c_uint32),
        ("outputTechnology", c_uint32),
        ("rotation", c_uint32),
        ("scaling", c_uint32),
        ("refreshRate", DISPLAYCONFIG_RATIONAL),
        ("scanLineOrdering", c_uint32),
        ("targetAvailable", c_int32),
        ("statusFlags", c_uint32),
    ]


class DISPLAYCONFIG_PATH_INFO(ctypes.Structure):
    _fields_ = [
        ("sourceInfo", DISPLAYCONFIG_PATH_SOURCE_INFO),
        ("targetInfo", DISPLAYCONFIG_PATH_TARGET_INFO),
        ("flags", c_uint32),
    ]


class DISPLAYCONFIG_2DREGION(ctypes.Structure):
    _fields_ = [("cx", c_uint32), ("cy", c_uint32)]


class DISPLAYCONFIG_VIDEO_SIGNAL_INFO(ctypes.Structure):
    _fields_ = [
        ("pixelRate", c_uint64),
        ("hSyncFreq", DISPLAYCONFIG_RATIONAL),
        ("vSyncFreq", DISPLAYCONFIG_RATIONAL),
        ("activeSize", DISPLAYCONFIG_2DREGION),
        ("totalSize", DISPLAYCONFIG_2DREGION),
        ("videoStandard", c_uint32),
        ("scanLineOrdering", c_uint32),
    ]


class DISPLAYCONFIG_TARGET_MODE(ctypes.Structure):
    _fields_ = [("targetVideoSignalInfo", DISPLAYCONFIG_VIDEO_SIGNAL_INFO)]


class POINTL(ctypes.Structure):
    _fields_ = [("x", c_int32), ("y", c_int32)]


class RECTL(ctypes.Structure):
    _fields_ = [
        ("left", c_int32),
        ("top", c_int32),
        ("right", c_int32),
        ("bottom", c_int32),
    ]


class DISPLAYCONFIG_SOURCE_MODE(ctypes.Structure):
    _fields_ = [
        ("width", c_uint32),
        ("height", c_uint32),
        ("pixelFormat", c_uint32),
        ("position", POINTL),
    ]


class DISPLAYCONFIG_DESKTOP_IMAGE_INFO(ctypes.Structure):
    _fields_ = [
        ("PathSourceSize", POINTL),
        ("DesktopImageRegion", RECTL),
        ("DesktopImageClip", RECTL),
    ]


class _ModeInfoUnion(ctypes.Union):
    _fields_ = [
        ("targetMode", DISPLAYCONFIG_TARGET_MODE),
        ("sourceMode", DISPLAYCONFIG_SOURCE_MODE),
        ("desktopImageInfo", DISPLAYCONFIG_DESKTOP_IMAGE_INFO),
    ]


class DISPLAYCONFIG_MODE_INFO(ctypes.Structure):
    _anonymous_ = ("_u",)
    _fields_ = [
        ("infoType", c_uint32),
        ("id", c_uint32),
        ("adapterId", LUID),
        ("_u", _ModeInfoUnion),
    ]


class DISPLAYCONFIG_DEVICE_INFO_HEADER(ctypes.Structure):
    _fields_ = [
        ("type", c_uint32),
        ("size", c_uint32),
        ("adapterId", LUID),
        ("id", c_uint32),
    ]


class DISPLAYCONFIG_SOURCE_DEVICE_NAME(ctypes.Structure):
    _fields_ = [
        ("header", DISPLAYCONFIG_DEVICE_INFO_HEADER),
        ("viewGdiDeviceName", c_wchar * 32),
    ]


class DISPLAYCONFIG_TARGET_DEVICE_NAME(ctypes.Structure):
    _fields_ = [
        ("header", DISPLAYCONFIG_DEVICE_INFO_HEADER),
        ("flags", c_uint32),
        ("outputTechnology", c_uint32),
        ("edidManufactureId", c_uint16),
        ("edidProductCodeId", c_uint16),
        ("connectorInstance", c_uint32),
        ("monitorFriendlyDeviceName", c_wchar * 64),
        ("monitorDevicePath", c_wchar * 128),
    ]


class DISPLAYCONFIG_ADAPTER_NAME(ctypes.Structure):
    _fields_ = [
        ("header", DISPLAYCONFIG_DEVICE_INFO_HEADER),
        ("adapterDevicePath", c_wchar * 128),
    ]


class DisplayConfigError(OSError):
    """A CCD API call failed."""

    def __init__(self, func: str, code: int):
        super().__init__(code, f"{func} 失败，错误码 {code}: {_format_error(code)}")
        self.func = func


def _format_error(code: int) -> str:
    if sys.platform == "win32":
        return ctypes.FormatError(code).strip()
    return "unknown error"


def _user32():
    if sys.platform != "win32":
        raise OSError("PrimeShift 只能在 Windows 上运行")
    return ctypes.WinDLL("user32")


def _check(code: int, func: str) -> None:
    if code != ERROR_SUCCESS:
        raise DisplayConfigError(func, code)


def query_config(flags: int = QDC_ONLY_ACTIVE_PATHS):
    """Return (paths, modes) describing the current display topology."""
    user32 = _user32()
    while True:
        n_paths = c_uint32()
        n_modes = c_uint32()
        _check(
            user32.GetDisplayConfigBufferSizes(flags, byref(n_paths), byref(n_modes)),
            "GetDisplayConfigBufferSizes",
        )
        paths = (DISPLAYCONFIG_PATH_INFO * n_paths.value)()
        modes = (DISPLAYCONFIG_MODE_INFO * n_modes.value)()
        rc = user32.QueryDisplayConfig(
            flags, byref(n_paths), paths, byref(n_modes), modes, None
        )
        # The topology can change between the two calls; retry with fresh sizes.
        if rc == ERROR_INSUFFICIENT_BUFFER:
            continue
        _check(rc, "QueryDisplayConfig")
        return list(paths[: n_paths.value]), list(modes[: n_modes.value])


def set_config(paths, modes, flags: int) -> None:
    user32 = _user32()
    path_arr = (DISPLAYCONFIG_PATH_INFO * len(paths))(*paths)
    mode_arr = (DISPLAYCONFIG_MODE_INFO * len(modes))(*modes)
    _check(
        user32.SetDisplayConfig(len(paths), path_arr, len(modes), mode_arr, flags),
        "SetDisplayConfig",
    )


def _device_info(struct_type, info_type: int, adapter_id: LUID, item_id: int):
    info = struct_type()
    info.header.type = info_type
    info.header.size = ctypes.sizeof(struct_type)
    info.header.adapterId = adapter_id
    info.header.id = item_id
    _check(_user32().DisplayConfigGetDeviceInfo(byref(info)), "DisplayConfigGetDeviceInfo")
    return info


def get_target_name(adapter_id: LUID, target_id: int) -> DISPLAYCONFIG_TARGET_DEVICE_NAME:
    return _device_info(
        DISPLAYCONFIG_TARGET_DEVICE_NAME, DEVICE_INFO_GET_TARGET_NAME, adapter_id, target_id
    )


def get_source_gdi_name(adapter_id: LUID, source_id: int) -> str:
    info = _device_info(
        DISPLAYCONFIG_SOURCE_DEVICE_NAME, DEVICE_INFO_GET_SOURCE_NAME, adapter_id, source_id
    )
    return info.viewGdiDeviceName


def get_adapter_path(adapter_id: LUID) -> str:
    info = _device_info(DISPLAYCONFIG_ADAPTER_NAME, DEVICE_INFO_GET_ADAPTER_NAME, adapter_id, 0)
    return info.adapterDevicePath

"""Capture, store and re-apply complete display layouts ("profiles").

A profile stores the raw DISPLAYCONFIG path and mode records of every active
display, so applying it restores resolution, refresh rate, position, rotation
and which monitor is primary, and turns off every display that is not in it.

Adapter LUIDs are only valid until the next reboot or driver reload, so each
profile also records the stable device path of every adapter it references and
the LUIDs are remapped to the current ones before the layout is applied.
"""

from __future__ import annotations

import json
import os
import re
from dataclasses import asdict, dataclass
from pathlib import Path
from typing import Callable, Iterable

from . import ccd

PROFILE_VERSION = 1
_NAME_RE = re.compile(r"^[A-Za-z0-9_-]{1,64}$")


class ProfileError(Exception):
    pass


@dataclass
class Monitor:
    name: str
    device_path: str
    adapter: str
    target_id: int
    active: bool
    gdi_name: str = ""
    width: int = 0
    height: int = 0
    x: int = 0
    y: int = 0
    refresh_hz: float = 0.0
    primary: bool = False

    def describe(self) -> str:
        if not self.active:
            return f"{self.name}  [已连接，未启用]"
        flag = "  [主屏]" if self.primary else ""
        return (
            f"{self.gdi_name or '?'}  {self.name}  {self.width}x{self.height}"
            f"@{self.refresh_hz:g}Hz  位置({self.x},{self.y}){flag}"
        )


def luid_key(luid: ccd.LUID) -> str:
    return f"{luid.HighPart:08x}:{luid.LowPart:08x}"


def _mode_at(modes, idx: int, info_type: int):
    if idx == ccd.DISPLAYCONFIG_PATH_MODE_IDX_INVALID or idx >= len(modes):
        return None
    mode = modes[idx]
    return mode if mode.infoType == info_type else None


def _refresh_hz(rate: ccd.DISPLAYCONFIG_RATIONAL) -> float:
    if not rate.Denominator:
        return 0.0
    return round(rate.Numerator / rate.Denominator, 2)


def describe_paths(
    paths,
    modes,
    target_name: Callable[[ccd.LUID, int], tuple[str, str]],
    gdi_name: Callable[[ccd.LUID, int], str] = lambda luid, sid: "",
) -> list[Monitor]:
    """Summarise active paths as Monitor records.

    target_name returns (friendly name, device path) for a target; it is a
    parameter so this function can be tested without Windows.
    """
    monitors = []
    for path in paths:
        src, tgt = path.sourceInfo, path.targetInfo
        name, device_path = target_name(tgt.adapterId, tgt.id)
        mon = Monitor(
            name=name,
            device_path=device_path,
            adapter=luid_key(tgt.adapterId),
            target_id=tgt.id,
            active=bool(path.flags & ccd.DISPLAYCONFIG_PATH_ACTIVE),
            gdi_name=gdi_name(src.adapterId, src.id),
            refresh_hz=_refresh_hz(tgt.refreshRate),
        )
        source_mode = _mode_at(modes, src.modeInfoIdx, ccd.MODE_INFO_TYPE_SOURCE)
        if source_mode is not None:
            sm = source_mode.sourceMode
            mon.width, mon.height = sm.width, sm.height
            mon.x, mon.y = sm.position.x, sm.position.y
            # The primary display is by definition the one whose desktop origin is (0, 0).
            mon.primary = mon.active and mon.x == 0 and mon.y == 0
        monitors.append(mon)
    return monitors


def _win_target_name(luid: ccd.LUID, target_id: int) -> tuple[str, str]:
    info = ccd.get_target_name(luid, target_id)
    return info.monitorFriendlyDeviceName or "未知显示器", info.monitorDevicePath


def _win_gdi_name(luid: ccd.LUID, source_id: int) -> str:
    try:
        return ccd.get_source_gdi_name(luid, source_id)
    except ccd.DisplayConfigError:
        return ""


def list_monitors() -> list[Monitor]:
    """All connected monitors: active ones first, then connected but disabled ones."""
    paths, modes = ccd.query_config(ccd.QDC_ONLY_ACTIVE_PATHS)
    monitors = describe_paths(paths, modes, _win_target_name, _win_gdi_name)
    seen = {(m.adapter, m.target_id) for m in monitors}
    all_paths, _ = ccd.query_config(ccd.QDC_ALL_PATHS)
    for path in all_paths:
        tgt = path.targetInfo
        key = (luid_key(tgt.adapterId), tgt.id)
        if not tgt.targetAvailable or key in seen:
            continue
        seen.add(key)
        name, device_path = _win_target_name(tgt.adapterId, tgt.id)
        monitors.append(Monitor(name, device_path, key[0], tgt.id, active=False))
    return monitors


def _adapter_luids(paths, modes) -> Iterable[ccd.LUID]:
    for path in paths:
        yield path.sourceInfo.adapterId
        yield path.targetInfo.adapterId
    for mode in modes:
        yield mode.adapterId


def build_profile(paths, modes, adapter_path: Callable[[ccd.LUID], str], monitors) -> dict:
    adapters = {}
    for luid in _adapter_luids(paths, modes):
        key = luid_key(luid)
        if key not in adapters:
            adapters[key] = adapter_path(luid)
    return {
        "version": PROFILE_VERSION,
        "adapters": adapters,
        "monitors": [asdict(m) for m in monitors],
        "paths": [bytes(p).hex() for p in paths],
        "modes": [bytes(m).hex() for m in modes],
    }


def capture() -> dict:
    """Snapshot the current display layout."""
    paths, modes = ccd.query_config(ccd.QDC_ONLY_ACTIVE_PATHS)
    monitors = describe_paths(paths, modes, _win_target_name, _win_gdi_name)
    return build_profile(paths, modes, ccd.get_adapter_path, monitors)


def decode_profile(profile: dict):
    if profile.get("version") != PROFILE_VERSION:
        raise ProfileError(f"不支持的配置版本: {profile.get('version')}")
    try:
        paths = [
            ccd.DISPLAYCONFIG_PATH_INFO.from_buffer_copy(bytes.fromhex(h))
            for h in profile["paths"]
        ]
        modes = [
            ccd.DISPLAYCONFIG_MODE_INFO.from_buffer_copy(bytes.fromhex(h))
            for h in profile["modes"]
        ]
    except (KeyError, ValueError) as exc:
        raise ProfileError(f"配置文件已损坏: {exc}") from exc
    if not paths:
        raise ProfileError("配置中没有任何显示器")
    return paths, modes


def remap_luids(paths, modes, mapping: dict[str, ccd.LUID]) -> None:
    """Rewrite every adapter LUID in place using mapping[old_key] -> new LUID."""

    def new(luid: ccd.LUID) -> ccd.LUID:
        target = mapping[luid_key(luid)]
        return ccd.LUID(target.LowPart, target.HighPart)

    for path in paths:
        path.sourceInfo.adapterId = new(path.sourceInfo.adapterId)
        path.targetInfo.adapterId = new(path.targetInfo.adapterId)
    for mode in modes:
        mode.adapterId = new(mode.adapterId)


def resolve_adapters(saved: dict[str, str], current: dict[str, ccd.LUID]) -> dict[str, ccd.LUID]:
    """Map saved LUID keys to current LUIDs by matching adapter device paths."""
    mapping = {}
    for key, device_path in saved.items():
        if device_path not in current:
            raise ProfileError(f"找不到配置中记录的显卡: {device_path}")
        mapping[key] = current[device_path]
    return mapping


def missing_targets(paths, available: set[tuple[str, int]]) -> list[int]:
    """Indexes of paths whose target monitor is not currently connected."""
    return [
        i
        for i, path in enumerate(paths)
        if (luid_key(path.targetInfo.adapterId), path.targetInfo.id) not in available
    ]


def apply(profile: dict) -> None:
    """Apply a captured profile, turning off any display that is not part of it."""
    paths, modes = decode_profile(profile)

    all_paths, _ = ccd.query_config(ccd.QDC_ALL_PATHS)
    luids = {luid_key(luid): luid for luid in _adapter_luids(all_paths, [])}
    current_adapters = {ccd.get_adapter_path(luid): luid for luid in luids.values()}
    available = {
        (luid_key(p.targetInfo.adapterId), p.targetInfo.id)
        for p in all_paths
        if p.targetInfo.targetAvailable
    }

    remap_luids(paths, modes, resolve_adapters(profile["adapters"], current_adapters))

    missing = missing_targets(paths, available)
    if missing:
        saved = profile.get("monitors", [])
        names = [saved[i]["name"] if i < len(saved) else f"#{i}" for i in missing]
        raise ProfileError("以下显示器当前未连接: " + ", ".join(names))

    base = ccd.SDC_USE_SUPPLIED_DISPLAY_CONFIG | ccd.SDC_ALLOW_CHANGES
    # Validate first so a bad profile never leaves the desktop half-applied.
    ccd.set_config(paths, modes, base | ccd.SDC_VALIDATE)
    ccd.set_config(paths, modes, base | ccd.SDC_APPLY | ccd.SDC_SAVE_TO_DATABASE)


def home_dir() -> Path:
    override = os.environ.get("PRIMESHIFT_HOME")
    if override:
        return Path(override)
    return Path(os.environ.get("APPDATA", Path.home())) / "PrimeShift"


def profile_path(name: str) -> Path:
    if not _NAME_RE.match(name):
        raise ProfileError(f"配置名只能包含字母、数字、下划线和短横线: {name!r}")
    return home_dir() / "profiles" / f"{name}.json"


def save(name: str, profile: dict, overwrite: bool = False) -> Path:
    path = profile_path(name)
    if path.exists() and not overwrite:
        raise ProfileError(f"配置 {name} 已存在，覆盖请加 --force")
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(profile, ensure_ascii=False, indent=2), encoding="utf-8")
    return path


def load(name: str) -> dict:
    path = profile_path(name)
    if not path.exists():
        raise ProfileError(f"配置 {name} 不存在，请先运行: primeshift save {name}")
    try:
        return json.loads(path.read_text(encoding="utf-8"))
    except json.JSONDecodeError as exc:
        raise ProfileError(f"配置文件 {path} 不是合法的 JSON: {exc}") from exc


def saved_names() -> list[str]:
    folder = home_dir() / "profiles"
    if not folder.is_dir():
        return []
    return sorted(p.stem for p in folder.glob("*.json"))

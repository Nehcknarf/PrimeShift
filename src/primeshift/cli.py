"""Command line entry point."""

from __future__ import annotations

import argparse
import json
import logging
import sys
from pathlib import Path

from . import __version__, hotkey, profile

DEFAULT_CONFIG = {
    "hotkeys": {
        "ctrl+alt+f11": "remote",
        "ctrl+alt+f12": "home",
    }
}

RUN_KEY = r"Software\Microsoft\Windows\CurrentVersion\Run"
RUN_VALUE = "PrimeShift"


def config_path() -> Path:
    return profile.home_dir() / "config.json"


def load_config() -> dict:
    path = config_path()
    if not path.exists():
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(json.dumps(DEFAULT_CONFIG, indent=2), encoding="utf-8")
        return DEFAULT_CONFIG
    try:
        config = json.loads(path.read_text(encoding="utf-8"))
    except json.JSONDecodeError as exc:
        raise profile.ProfileError(f"{path} 不是合法的 JSON: {exc}") from exc
    if not isinstance(config.get("hotkeys"), dict) or not config["hotkeys"]:
        raise profile.ProfileError(f"{path} 中缺少 hotkeys 配置")
    return config


def cmd_list(_args) -> None:
    for mon in profile.list_monitors():
        print(mon.describe())


def cmd_save(args) -> None:
    snapshot = profile.capture()
    path = profile.save(args.name, snapshot, overwrite=args.force)
    print(f"已保存当前布局到 {path}")
    for mon in snapshot["monitors"]:
        print("  " + profile.Monitor(**mon).describe())


def cmd_load(args) -> None:
    profile.apply(profile.load(args.name))
    print(f"已切换到布局 {args.name}")


def cmd_profiles(_args) -> None:
    names = profile.saved_names()
    if not names:
        print("还没有保存任何布局，先运行: primeshift save <名称>")
    for name in names:
        print(name)
        for mon in profile.load(name).get("monitors", []):
            print("  " + profile.Monitor(**mon).describe())


def cmd_run(_args) -> None:
    config = load_config()
    bindings = {}
    for keys, name in config["hotkeys"].items():
        hotkey.parse_hotkey(keys)  # fail fast on typos before registering anything
        profile.load(name)  # fail fast on a missing profile
        bindings[keys] = lambda name=name: profile.apply(profile.load(name))
    print(f"PrimeShift 正在运行（配置: {config_path()}），按 Ctrl+C 退出")
    for keys, name in config["hotkeys"].items():
        print(f"  {keys}  ->  {name}")
    hotkey.run(bindings)


def _startup_command() -> str:
    if getattr(sys, "frozen", False):
        return f'"{sys.executable}" run'
    # Prefer pythonw.exe so autostart does not leave a console window open.
    exe = Path(sys.executable)
    pythonw = exe.with_name("pythonw.exe")
    return f'"{pythonw if pythonw.exists() else exe}" -m primeshift run'


def cmd_startup(args) -> None:
    import winreg

    with winreg.OpenKey(winreg.HKEY_CURRENT_USER, RUN_KEY, 0, winreg.KEY_ALL_ACCESS) as key:
        if args.action == "on":
            command = _startup_command()
            winreg.SetValueEx(key, RUN_VALUE, 0, winreg.REG_SZ, command)
            print(f"已设置开机自启: {command}")
        elif args.action == "off":
            try:
                winreg.DeleteValue(key, RUN_VALUE)
            except FileNotFoundError:
                pass
            print("已取消开机自启")
        else:
            try:
                value, _ = winreg.QueryValueEx(key, RUN_VALUE)
                print(f"开机自启已开启: {value}")
            except FileNotFoundError:
                print("开机自启未开启")


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        prog="primeshift", description="一键切换 Windows 显示布局（KVM 远程主屏切换）"
    )
    parser.add_argument("--version", action="version", version=f"%(prog)s {__version__}")
    sub = parser.add_subparsers(dest="command", required=True)

    sub.add_parser("list", help="列出当前连接的显示器").set_defaults(func=cmd_list)

    p = sub.add_parser("save", help="把当前显示布局保存为配置")
    p.add_argument("name", help="配置名，例如 home 或 remote")
    p.add_argument("--force", action="store_true", help="覆盖同名配置")
    p.set_defaults(func=cmd_save)

    p = sub.add_parser("load", help="切换到已保存的布局")
    p.add_argument("name")
    p.set_defaults(func=cmd_load)

    sub.add_parser("profiles", help="列出已保存的布局").set_defaults(func=cmd_profiles)

    sub.add_parser("run", help="后台运行并监听全局快捷键").set_defaults(func=cmd_run)

    p = sub.add_parser("startup", help="设置开机自启（当前用户）")
    p.add_argument("action", choices=["on", "off", "status"])
    p.set_defaults(func=cmd_startup)
    return parser


def _setup_logging() -> None:
    handlers: list[logging.Handler] = []
    if sys.stderr is not None:  # None under pythonw.exe
        console = logging.StreamHandler()
        console.setFormatter(logging.Formatter("%(message)s"))
        handlers.append(console)
    log_dir = profile.home_dir()
    try:
        log_dir.mkdir(parents=True, exist_ok=True)
        file_handler = logging.FileHandler(log_dir / "primeshift.log", encoding="utf-8")
        file_handler.setFormatter(logging.Formatter("%(asctime)s %(levelname)s %(message)s"))
        handlers.append(file_handler)
    except OSError:
        pass
    logging.basicConfig(level=logging.INFO, handlers=handlers)


def main(argv: list[str] | None = None) -> int:
    args = build_parser().parse_args(argv)
    _setup_logging()
    try:
        args.func(args)
    except KeyboardInterrupt:
        return 130
    except (profile.ProfileError, ValueError, OSError) as exc:
        logging.getLogger("primeshift").error("%s", exc)
        return 1
    return 0

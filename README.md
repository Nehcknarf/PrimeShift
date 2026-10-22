# PrimeShift
🖥️ One-hotkey primary display switcher for Windows multi-monitor + KVM setups. 一键将 KVM 屏切为主屏，远程访问不再黑屏。

## 解决什么问题

主机接了多块显示器，其中一路接到远程 KVM（如 GL-RM10）作为扩展屏。远程连上时，任务栏、开始菜单和窗口都在本地主屏上，KVM 画面里只有一块空桌面，看起来像黑屏。

PrimeShift 把显示布局保存成配置，再用全局快捷键一键切换：

- **remote**：只保留 KVM 屏，并把它设为主屏。所有内容都会出现在 KVM 画面里。
- **home**：恢复本地的多屏布局。

平时本地使用完全不受影响，也不需要显示器分配器。

## 特点

- 纯 Python 标准库 + ctypes，直接调用 Windows CCD API（`QueryDisplayConfig` / `SetDisplayConfig`），不依赖 MultiMonitorTool、AutoHotkey 等第三方程序
- 完整保存并恢复分辨率、刷新率、位置、旋转和主屏，并关闭不在配置里的显示器
- 重启后显卡 LUID 会变化，PrimeShift 按显卡设备路径自动重新映射
- 应用前先用 `SDC_VALIDATE` 校验，配置不合法时不会只切换一半
- 配置里的显示器没连接时，直接报出是哪一块，不会盲目切换

## 安装

需要 Windows 10/11 和 Python 3.9+。

```powershell
pip install .
```

## 使用

### 1. 保存两套布局

```powershell
# 在正常的本地多屏状态下
primeshift save home

# 在「设置 > 系统 > 屏幕」里把 KVM 屏设为主屏，并断开其他显示器，然后
primeshift save remote

# 恢复本地布局
primeshift load home
```

建议把 KVM 屏的分辨率设成和 KVM 采集能力一致（例如 1080p 或 4K30），远程画面会更清晰流畅。

### 2. 运行快捷键监听

```powershell
primeshift run
```

默认快捷键：

| 快捷键 | 布局 |
|---|---|
| `Ctrl+Alt+F11` | remote（仅 KVM 屏，且为主屏） |
| `Ctrl+Alt+F12` | home（恢复本地多屏） |

首次运行会生成 `%APPDATA%\PrimeShift\config.json`，可以在里面改快捷键或添加更多布局：

```json
{
  "hotkeys": {
    "ctrl+alt+f11": "remote",
    "ctrl+alt+f12": "home"
  }
}
```

支持的修饰键：`ctrl`、`alt`、`shift`、`win`。按键可以是字母、数字、`f1`–`f24`、`numpad0`–`numpad9`、`home`、`end`、`pageup`、`pagedown`、`insert`、`delete`、`pause`、`space` 和方向键。

### 3. 开机自启

```powershell
primeshift startup on      # 登录后自动在后台运行（使用 pythonw，无控制台窗口）
primeshift startup status
primeshift startup off
```

### 其他命令

```powershell
primeshift list        # 列出当前连接的显示器
primeshift profiles    # 列出已保存的布局
```

日志写在 `%APPDATA%\PrimeShift\primeshift.log`。

## 注意事项

- **锁屏时快捷键不生效。** 开机或锁屏后，需要先通过 KVM 键盘盲输 PIN 登录（按任意键唤出登录框，输入 PIN 后回车），再按 `Ctrl+Alt+F11`。
- **离开前最好先切回 home。** 如果停在 remote 布局，回家时本地的三块屏都是黑的，按 `Ctrl+Alt+F12` 即可恢复。
- 日常远程如果需要高画质和高帧率，可以配合 Sunshine + Moonlight 使用，KVM 留给开机、BIOS 和救援场景。

## 开发

```bash
PYTHONPATH=src python -m unittest discover -s tests
```

## License

MIT

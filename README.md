# PrimeShift
🖥️ One-hotkey primary display switcher for Windows multi-monitor + KVM setups. 一键将 KVM 屏切为主屏，远程访问不再黑屏。

## 解决什么问题

主机接了多块显示器，其中一路接到远程 KVM（如 GL-RM10）作为扩展屏。远程连上时，任务栏、开始菜单和窗口都在本地主屏上，KVM 画面里只有一块空桌面，看起来像黑屏。

PrimeShift 把显示布局保存下来，再用全局快捷键或托盘菜单一键切换：

- **remote**：只保留 KVM 屏，并把它设为主屏，所有内容都出现在 KVM 画面里
- **home**：恢复本地的多屏布局

平时本地使用完全不受影响，也不需要显示器分配器。

## 特点

- **免安装单个 exe**，常驻托盘，不依赖 MultiMonitorTool、AutoHotkey 等第三方程序
- **低占用**：常驻的托盘程序只用 Win32 API，不加载界面框架；设置窗口是单独的进程，打开时才启动，关闭即退出
- **现代界面**：设置窗口使用 WPF Fluent 主题，跟随系统明暗模式；托盘图标和右键菜单也跟随任务栏明暗
- **自定义快捷键**：点击后直接按组合键录入，自动检查是否和其他程序、系统快捷键或其他布局冲突
- **开机自启**：可在设置窗口或托盘菜单里开关，写在当前用户的注册表启动项里，不需要管理员权限
- **完整还原布局**：通过 Windows CCD API（`QueryDisplayConfig` / `SetDisplayConfig`）保存并还原分辨率、刷新率、位置、旋转和主屏，并关闭不在布局里的显示器
- **可靠**：
  - 重启后显卡内部编号（LUID）会变，PrimeShift 按显卡设备路径自动对应
  - 应用前先让系统校验，不合法的布局不会只切换一半
  - 布局里的显示器没连接时，会提示是哪一块，不会盲目切换

## 下载

在 [Actions](../../actions/workflows/build.yml) 最新一次成功构建的 Artifacts 里下载；打 `v*` 标签后，Releases 页面也会自动附上 exe。

| 文件 | 说明 |
|---|---|
| `PrimeShift-win-x64.exe` | 独立版，约 130 MB，什么都不用装 |
| `PrimeShift-win-x64-lite.exe` | 精简版，不到 1 MB，需要先安装 [.NET 10 桌面运行时](https://dotnet.microsoft.com/download/dotnet/10.0) |

系统要求：Windows 10 1903 及以上，或 Windows 11。

## 使用

1. 双击 `PrimeShift.exe`。托盘区会出现图标；第一次运行会自动打开设置窗口。
2. 在正常的本地多屏状态下，输入名称 `home`，点「保存当前布局」。
3. 点「打开显示设置」，把 KVM 屏设为主屏并断开其他显示器，回来输入 `remote` 再保存。
4. 在 `home` 卡片上点「应用」恢复本地布局。
5. 分别点两张卡片的快捷键框，按下想要的组合键，例如 `Ctrl + Alt + F11` 和 `Ctrl + Alt + F12`。
6. 勾选「登录 Windows 后自动启动 PrimeShift」。

之后远程登录时按 `Ctrl + Alt + F11`，所有内容就会出现在 KVM 画面里；回家后按 `Ctrl + Alt + F12` 恢复。也可以左键或右键点托盘图标，直接在菜单里切换。

建议把 KVM 屏的分辨率设成和 KVM 采集能力一致（例如 1080p 或 4K30），远程画面会更清晰流畅。

### 快捷键冲突检测

录入快捷键时，PrimeShift 会检查：

- 是否被其他程序注册为全局快捷键，或被 Windows 保留（例如大部分 `Win +` 组合）
- 是否和其他布局重复
- 是否缺少修饰键（只用 `Shift` 时只能搭配 F1–F24，避免影响正常打字）

Windows 的限制：查不出是哪个程序占用了快捷键；用低层键盘钩子拦截按键的程序（例如部分 AutoHotkey 脚本）也检测不到。

设置窗口打开期间全局快捷键会暂停，方便重新录入；关闭窗口后自动恢复。如果某个快捷键在恢复时注册失败，托盘会弹出通知。

### 文件位置

| 内容 | 位置 |
|---|---|
| 配置（布局、快捷键） | `%APPDATA%\PrimeShift\config.json` |
| 日志 | `%APPDATA%\PrimeShift\primeshift.log` |
| 开机自启 | `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\PrimeShift` |

## 注意事项

- **锁屏时快捷键不生效。** 开机或锁屏后，需要先通过 KVM 键盘盲输 PIN 登录（按任意键唤出登录框，输入 PIN 后回车），再按快捷键。
- **离开前最好先切回 home。** 如果停在 remote 布局，回家时本地的屏幕都是黑的，按一下 home 的快捷键即可恢复。
- 日常远程如果需要高画质和高帧率，可以配合 Sunshine + Moonlight 使用，KVM 留给开机、BIOS 和救援场景。

## 开发

需要 .NET 10 SDK。

```powershell
dotnet test                                        # 单元测试（也能在 Linux/macOS 上运行）
dotnet run --project src/PrimeShift                # 运行托盘程序
dotnet run --project src/PrimeShift -- --settings  # 只打开设置窗口
dotnet publish src/PrimeShift -c Release -r win-x64             # 独立版单 exe
dotnet publish src/PrimeShift -c Release -r win-x64 -p:SelfContained=false  # 精简版
```

项目结构：

| 路径 | 内容 |
|---|---|
| `src/PrimeShift.Core` | 显示布局（CCD API）、快捷键解析与冲突检测、配置读写；与平台无关，可单元测试 |
| `src/PrimeShift/Agent` | 常驻托盘程序：隐藏窗口、托盘图标、右键菜单、全局快捷键 |
| `src/PrimeShift/Settings` | WPF 设置窗口（`PrimeShift.exe --settings`） |
| `tests/PrimeShift.Core.Tests` | xUnit 测试 |

## License

MIT

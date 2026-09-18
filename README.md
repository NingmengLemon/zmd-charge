# EndfieldCharge · 终末地风格电量 HUD

插上 / 拔掉充电器时，从屏幕顶部弹出一块"灵动岛"式 HUD，显示当前电量（mWh 与百分比）。
视觉与动画风格复刻《终末地》工业 / 超充模式 HUD。

- **插电**：完整三态动画 —— 电标弹出 → 胶囊撑高成圆角矩形显示「超充模式」→ 收成圆胶囊显示电量 → 停留 → 整体缩小收回
- **拔电**：简化动画 —— 只弹电量圆胶囊，内容在胶囊完全出来后快速显现 → 停留 → 收回

## 下载

从 [Releases](https://github.com/NingmengLemon/zmd-charge/releases) 下载：

| 平台 | 文件 |
|------|------|
| Windows x64 | `EndfieldCharge-x.y.z-win-x64.zip` |
| Linux x64 | `EndfieldCharge-x.y.z-linux-x64.tar.gz` |

解压即用（无安装器，不留卸载项）。Linux 包用 `tar.gz` 而不是 `zip`，因为 tar 保留可执行位。

## 功能

| 功能 | 说明 |
|------|------|
| 电量显示 | 剩余 / 满充容量（mWh，整数）与百分比。Windows 读 `CallNtPowerInformation`（WMI 兜底），Linux 读 sysfs |
| 电源监听 | Windows 用 `RegisterPowerSettingNotification` 订阅 GUID_ACDC_POWER_SOURCE，2s 轮询兜底，400ms 双向去抖（过滤 Windows 满电瞬时抖动）；Linux 是 2s 纯轮询 |
| 低电量变色 | 电量低于设置里的低电量阈值时黄绿电量圈变红（默认 20%，#FF4D4F） |
| 提醒通知 | 低电量提醒（阈值可调 5–40%）与充满提醒（≥99%），卡牌风格弹窗，4s 自动消失。由 30s 独立采样驱动，插拔瞬间额外复核一次 |
| 设置窗口 | 全局缩放（0.4–1.2）、显示时长（3–10s）、HUD 位置（顶部居中/靠右/靠左）、显示器选择、语言、开机自启，保存即生效并持久化 |
| 托盘图标 | 左键单击播放一次 HUD 动画，右键弹出菜单（预览 / 设置 / 检查更新 / 退出） |
| 动画微调 | 设置窗口「动画」页实时预览并微调时长 / 回弹 / 波纹参数，保存即生效并持久化 |
| 节能模式提示 | 开 / 关节能（省电）模式时弹出对应 HUD。Windows 旧系统的路径可用，见「已知缺陷」；**Linux 上不支持**（没有跨桌面环境的统一接口） |
| 检查更新 | 读取 GitHub Releases API，比较程序集版本，一键跳转下载页。目标仓库由 CI 用 `github.repository` 注入，本地构建退回本仓库 |
| 多语言 | 中文 / 英文，默认跟随系统，可在设置中手动切换 |
| 开机自启 | 设置窗口「通用」页开关。Windows 写 `HKCU\...\CurrentVersion\Run`，Linux 写 XDG autostart，都是当前用户级、无需管理员 |
| 单实例 | 重复启动不会开出第二个常驻进程，而是让已有实例弹一次 HUD |
| 统一图标 | 托盘 / 各窗口 / exe 统一使用 `Assets\tray_bolt` 图标 |
| 日志 | `%TEMP%\EndfieldCharge\log-YYYYMMDD.txt`（Linux 上即 `/tmp/EndfieldCharge/`；保留 7 天，单日上限 5MB） |

## 已知缺陷

- **24H2+（build 26100+）的节能模式检测不可用**。
  `Services/Windows/PowerNative.cs` 里的 `GuidEnergySaverStatus` 取值
  `550e8400-e29b-41d4-a716-446655440000` **没有出处**：
  Windows SDK 10.0.19041 / 10.0.22621 的 `winnt.h` 里只有
  `GUID_ENERGY_SAVER_SUBGROUP` / `_BATTERY_THRESHOLD` / `_BRIGHTNESS` / `_POLICY`，
  整个 `Include` 树里搜不到 `ENERGY_SAVER_STATUS`；而这个值本身是 RFC 4122 的示例 UUID。
  拿它去调 `RegisterPowerSettingNotification` 收不到任何通知，所以那条路径实际是死的。
  代码暂时保留并已标注，删除前需要先确认 24H2+ 上正确的替代方式。
  旧系统（< 24H2）走 `GUID_POWER_SAVING_STATUS` + `GetSystemPowerStatus.SystemStatusFlag`，可用。

## 运行要求

- Windows 10 1809+ / Windows 11，或带桌面环境的 Linux（x64）
- .NET 10 运行时（Release 为框架依赖单文件发布）
  - Windows 需要 [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0)
  - Linux 需要 .NET 10 Runtime，桌面环境还需提供 X11 或 Wayland 与一个状态栏宿主（见「平台差异」）
- x64

## 构建

```bash
# 调试
dotnet build -c Debug

# 单测（纯逻辑，Debug 构建即可）。路径用正斜杠，Windows 与 Linux 都吃
dotnet test --project tests/EndfieldCharge.Tests -c Debug

# 发布（单文件，输出到 publish/）。Release 的 RID 按构建主机自动选 win-x64 / linux-x64
dotnet publish -c Release -o publish
```

> 正在运行本程序时 `dotnet build -c Release` 会因 apphost 被占用而失败（MSB3027），
> 先退出托盘里的程序再构建，或改用 `-o <其它目录>` 输出。

> 注意：`PublishSingleFile` 只把托管 dll 打进可执行文件，SkiaSharp 的 native 库仍需与它同目录
> —— Windows 上是 `libSkiaSharp.dll` / `libHarfBuzzSharp.dll` / `av_libglesv2.dll`，
> Linux 上是 `libSkiaSharp.so` / `libHarfBuzzSharp.so`（Linux 侧没有 ANGLE 那个）。
> 便携分发请打包整个 `publish/` 目录，不要只拷可执行文件。

### 构建层约定

| 文件 | 作用 |
|------|------|
| `global.json` | 钉住 SDK 主版本（`rollForward: latestFeature`），并声明 `test.runner` 用 Microsoft.Testing.Platform |
| `Directory.Build.props` | 两个工程共享的编译属性：TFM、Nullable、LangVersion、PlatformTarget、`TreatWarningsAsErrors`、`AnalysisLevel=latest-recommended`、`EnforceCodeStyleInBuild` |
| `Directory.Packages.props` | 中央包管理（CPM）：包版本只写在这里，工程里只写 `PackageReference Include` |
| `.editorconfig` | 代码风格基线。标成 `warning` 的规则会直接让构建失败 |

几点容易踩的：

- **警告即错误**（`TreatWarningsAsErrors`）在本地与 CI 是同一道闸门，CI 里不再单独传参。
- **`AnalysisLevel=latest-recommended`** 会打开一批质量规则（区域设置相关的格式化、可索引集合上的
  LINQ、可释放字段、P/Invoke 字符集等）。`.editorconfig` 里只把「本项目已全部满足且值得守住」的
  规则标成 `warning`，其余是 `suggestion`，不参与构建。
- **`IDE0005`（多余的 using）** 要在构建期生效，必须打开 `GenerateDocumentationFile`
  （Roslyn 的硬性前置条件，dotnet/roslyn#41640）。本项目是应用不是库，顺手关掉了
  「公开成员缺 XML 注释」的 CS1591；CI 打便携包时会排除这个 `.xml`。
- **`AllowUnsafeBlocks` 只给主工程**：`Services/Windows/PowerNative.cs` 用 `[LibraryImport]`，
  它的源生成器会产出 unsafe 代码（SYSLIB1062）。测试工程不需要，所以这个许可没放进共享属性。
- **测试是 xunit v3 + Microsoft.Testing.Platform**：测试工程是 `Exe`，编译产物自己就能跑测试。
  MTP 模式下 `dotnet test` 必须用 `--project` 指定工程，**在仓库根目录直接敲 `dotnet test`
  会报「未找到任何测试项目」**（根目录的 `EndfieldCharge.csproj` 不是测试工程，
  MTP 模式也不会递归子目录）。也可以直接运行
  `tests/EndfieldCharge.Tests/bin/Debug/net10.0/EndfieldCharge.Tests`（Windows 上带 `.exe`）。

### CI / 发布（GitHub Actions）

推送到 `main` 分支会自动在 **Windows 与 Linux 两个 runner 上各构建一遍**
（matrix，`fail-fast: false`，一个平台挂了也能看到另一个的结果），Actions 页面可下载 artifact。
推送 `v*` 标签（如 `v1.2.0`）会额外创建 GitHub Release，并把两个平台的包都附上：

```bash
git tag v1.2.0
git push origin v1.2.0
```

发 Release 是单独一个 job（`needs: build`）：两个平台的构建是并发跑的，
各自去创建同一个 Release 会打架，所以先都上传 artifact，再由这个 job 统一下载并发布。

主干构建的版本号是 `<最近 tag>.<run_number>`（如 `1.2.0.500`），第 4 段让它比同名
tag 更新 —— 否则 `0.0.N < 1.1.1` 会让应用一直提示更新，并把用户带到比当前代码更旧的发布版。

同一个步骤还会把 `github.repository` 注入程序集元数据（`UpdateRepoOwner` /
`UpdateRepoName`），所以任何 fork 的 CI 产物都查自己的 Release。本地构建不注入，
退回 `EndfieldCharge.csproj` 里的默认值。

## 调试参数

启动时追加参数，无需真的插拔电源：

| 参数 | 作用 |
|------|------|
| `--demo` | 用示例数据播放一次**完整**动画（插电） |
| `--preview` | 用本机真实电池数据播放一次完整动画 |
| `--preview-unplug` | 用示例数据播放一次**简化**动画（拔电） |
| `--debug-ring` | 静态呈现状态 C（电量态）1.5s |
| `--power-log` | 输出电源事件日志到 `%TEMP%\power-log.txt`（上限 2MB，Debug / Release 均生效） |
| `--show-fps` | 打开 Avalonia 渲染器自带的帧率叠层 |

> 注意：前四个参数互斥，按 `--demo` → `--preview-unplug` → `--preview` 的优先级生效。

## 项目结构

```
EndfieldCharge/
├─ Animations/
│  └─ HudAnimations.cs      # 时间线与动画轨道（KeySpline 逐段缓动）
├─ Services/
│  ├─ AlertPolicy.cs        # 低电量 / 充满的提醒判定（纯函数，可单测）
│  ├─ AutoStart.cs          # 开机自启门面（按 OS 分派到下面两个实现）
│  ├─ BatteryService.cs     # 电池快照与读取门面（按 OS 分派）
│  ├─ IPowerWatcher.cs      # 电源监听接口 + 工厂
│  ├─ Logger.cs             # 文件日志（临时目录，7 天保留）
│  ├─ ShowHudChannel.cs     # 唤醒已有实例的通路（抽象 + 工厂）
│  ├─ UpdateChecker.cs      # GitHub Releases 更新检查（目标仓库编译期注入）
│  ├─ UrlLauncher.cs        # 用系统默认程序打开 URL
│  ├─ WmiBatteryStatus.cs   # Win32_Battery 状态码语义（纯函数，平台无关）
│  ├─ Windows/              # Windows 专有实现，全部 [SupportedOSPlatform("windows")]
│  │  ├─ PowerNative.cs     #   P/Invoke：powrprof、message-only 窗口（[LibraryImport]）
│  │  ├─ WindowsBatteryReader.cs  # powrprof 主路径 + WMI 兜底
│  │  ├─ WindowsPowerWatcher.cs   # 电源通知 + 去抖确认 + 轮询兜底
│  │  ├─ WindowsAutoStart.cs      # HKCU Run 键
│  │  └─ WindowsShowHudChannel.cs # 命名 EventWaitHandle
│  └─ Linux/                # Linux 专有实现
│     ├─ LinuxBatteryReader.cs    # sysfs（根路径可注入，便于用 fixture 单测）
│     ├─ LinuxPowerWatcher.cs     # 纯轮询
│     ├─ LinuxAutoStart.cs        # XDG autostart
│     └─ LinuxShowHudChannel.cs   # Unix 域套接字
├─ Settings/
│  ├─ AppSettings.cs        # 设置模型（主构造函数 + 默认值，JSON 源生成序列化）
│  ├─ SettingsJsonContext.cs# 设置文件的 JSON 源生成上下文
│  ├─ SettingsManager.cs    # 设置加载与持久化
│  ├─ SettingsViewModel.cs  # 设置窗 ViewModel（CommunityToolkit.Mvvm）
│  ├─ SettingsWindow.axaml  # 设置窗口（通用 / 动画 / 通知 / 关于），全部编译绑定
│  └─ SettingsWindow.axaml.cs
├─ Views/
│  ├─ HudWindow.axaml(.cs)  # HUD 视觉树（胶囊 / 电标 / 标题 / 数字 / 徽章 / 波纹）
│  ├─ AlertWindow.axaml(.cs)# 卡牌风格提醒窗
│  ├─ MessageBoxWindow.*    # 极简消息框
│  └─ IHudPreview.cs        # 设置窗预览动画所需的最小 HUD 能力
├─ Styles/                  # HUD 配色（单一真源）与图标几何（StreamGeometry）
├─ Assets/                  # tray_bolt.png（托盘/窗口图标）+ tray_bolt.ico（Windows exe 图标）
├─ tests/
│  └─ EndfieldCharge.Tests/ # 纯逻辑单测（版本解析 / 百分比 / 提醒判定 / 时间线映射 /
│                           #   设置契约 / 设置窗 VM / Linux sysfs 电池读取）
└─ .github/workflows/       # CI：两个平台各构建一遍 + 打标签发 Release
```

## 平台差异

Windows 与 Linux 共用一份代码（单 TFM `net10.0`）。平台专有实现分别放在
`Services/Windows` 与 `Services/Linux`，由 `OperatingSystem.IsWindows()` 分派；
`[SupportedOSPlatform]` 配合编译期的 CA1416 负责把漏标注的地方抓出来。
这比双 TFM 少一套条件编译，代价是平台专有 API 必须显式守卫。

| 关注点 | Windows | Linux |
|--------|---------|-------|
| 电池读取 | powrprof `CallNtPowerInformation`，失败退回 WMI `Win32_Battery` | `/sys/class/power_supply`：优先 `energy_*`（µWh），没有就退回 `charge_*`（µAh）× `voltage_now`（µV）；不依赖 UPower |
| 电源变化 | `RegisterPowerSettingNotification` + 2s 轮询兜底 + 400ms 双向去抖 | 2s 轮询。sysfs 是被动读值，没有 Windows 那种瞬时误报，所以不需要去抖 |
| 开机自启 | `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` | XDG autostart：`~/.config/autostart/endfieldcharge.desktop` |
| 单实例 | 命名 Mutex | 命名 Mutex（跨进程语义与 Windows 一致，实测过） |
| 唤醒已有实例 | 命名 `EventWaitHandle` | Unix 域套接字（`$XDG_RUNTIME_DIR/endfieldcharge.sock`，没有该变量时退回临时目录） |
| 省电模式提示 | 可用（旧系统路径） | **不支持**，没有跨桌面环境的统一接口 |

单实例那两行值得单独说：**命名 `EventWaitHandle` 与命名 `Semaphore` 在 Linux 上会抛
`PlatformNotSupportedException`**，只有命名 `Mutex` 例外地可用，所以唤醒通路必须分平台。

### Linux 侧已验证与未验证

已在 Debian 13 + .NET 10.0.401 上实测：构建与 109 个单测、Release 发布（RID 正确解析为
`linux-x64`）、单实例判定、唤醒通路、HUD 窗口的尺寸与定位（Xvfb 下实测窗口为
`448x72+1056+4`，即 560×90 × 0.8 且水平居中）、打包出的 tar.gz 解压后可直接运行。

**尚未验证**（需要带桌面环境的实机）：

- 托盘图标。Avalonia 在 Linux 走 DBus StatusNotifierItem，需要桌面环境提供宿主；
  GNOME 默认不显示托盘图标，可能要装 AppIndicator 扩展。
- 窗口透明、`Topmost`、Wayland 下的行为、多显示器与 DPI。
- 真实电池。测试机是虚拟机，没有电池，电池读取目前只由 fixture 目录的单测覆盖。

## 动画实现要点

- Avalonia 的 `KeyFrame` 使用 **`KeySpline`（贝塞尔控制点）** 做逐段缓动，多关键帧下 `Animation.Easing` 不生效 —— 每段必须显式指定 `KeySpline`，否则该段为线性。
- `Border.HeightProperty`（即 `Layoutable.HeightProperty`）可直接动画，因此胶囊高度的 `60 → 90 → 60` 用独立轨道驱动。
- 收尾「整体缩小关没」由外层 `ScaleHost` 的 `RenderTransform` 统一缩放，胶囊本身宽度不动。
- HUD 窗口尺寸只比可见内容大一圈（`560×90 × 全局缩放`）。窗口是 `Topmost` 且背景可命中，开多大就会在插拔那几秒吞掉多大的鼠标点击区域。

## 托盘菜单为什么用 NativeMenu

`TrayIcon` 只暴露 `Clicked`（左键）；右键由 Win32 后端的私有方法
`OnRightClicked()` 处理，且**仅在 `Menu` 非空时**才弹菜单（`ITrayIconImpl` 上没有右键事件，
反射也挂不上）。所以右键菜单走 `TrayIcon.Menu`：弹窗由 Avalonia 渲染
（`MenuFlyoutPresenter`，样式在 `App.axaml` 里对齐了原来的深色观感）。

上面这段是 Windows 后端的行为。Linux 上 Avalonia 走的是另一套（DBus StatusNotifierItem），
`TrayIcon.Menu` 同样有效，但图标能不能显示取决于桌面环境有没有提供状态栏宿主。

## 设置窗为什么用 MVVM

设置窗有 50 多个控件，改造前靠手写代码逐个赋值本地化文案、逐个字段搬运
Load / Collect、手工同步 6 组「滑块值 → 数值标签」。每加一个设置项要改 5 个地方，
漏一处就是静默不一致。

现在逻辑全在 `SettingsViewModel` 里：设置值、数值读数、页签状态、状态文案都是可观察属性，
XAML 走编译绑定（Avalonia 12 默认开启，路径写错在编译期就会失败）；保存 / 检查更新 /
安装字体 / 播放预览 / 切换页签是 `[RelayCommand]`。

本地化那边把 `Localization` 从静态类改成了可观察单例（`ObservableObject` + `Current`）：
绑定需要一个带 `INotifyPropertyChanged` 的实例，这样才能写成 `{Binding L.TabGeneral}`，
而不必在 ViewModel 里再抄一遍几十个属性名。语言切换时 `UseSettings` 调 `NotifyAll()`
（空属性名按 INPC 约定表示「全部属性已变更」），全部绑定一次性刷新。

HUD 窗仍然保留 code-behind：它由代码驱动时间线动画，硬套 MVVM 只会多一层没有收益的间接。

## 许可证

MIT

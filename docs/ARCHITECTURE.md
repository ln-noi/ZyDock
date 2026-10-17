# 架构与文件说明

本文说明每个源文件的职责、模块之间的数据流，以及「想改某个东西该动哪个文件」。

---

## 1. 三个运行位置

| 位置 | 内容 | 是否进仓库 |
| --- | --- | --- |
| 安装目录（如 `C:\Users\<你>\Apps\ZyDock\`） | 程序本体 + .NET 运行时（247 文件 / ≈145 MB） | ❌ 构建产物 |
| `%APPDATA%\ZyDock\settings.json` | 用户配置（图标列表与顺序、外观、行为） | ❌ 运行时生成 |
| `%LOCALAPPDATA%\ZyDock\logs\app.log` | 日志（2 MB 上限 + 一次轮转，已脱敏） | ❌ 运行时生成 |

仓库里只有源码、文档与开发脚本；`bin/`、`obj/`、`dist/`、`.tools/` 都在 `.gitignore` 中。

---

## 2. 源码：`src/ZyDock.App`（WPF 表现层）

| 文件 | 行数（约） | 职责 |
| --- | --- | --- |
| `Views/DockWindow.xaml.cs` | 1660 | **核心**：图标构建、布局应用、逐帧动画、命中测试、拖入、拖动排序、右键分发、自动隐藏、托盘联动、调试钩子（编译期可选） |
| `Views/DockWindow.xaml` | 64 | 窗口骨架：底板位图层 / 图标层 / 状态点层 / 名称气泡层 |
| `Views/SettingsWindow.xaml(.cs)` | 400 / 660 | 设置界面与逻辑：滑块绑定、180 ms 防抖保存、图标增删改序、屏幕选择、恢复默认 |
| `Views/PopupMenuWindow.cs` | 200 | 自绘右键菜单（图标菜单 / Dock 菜单 / 托盘菜单共用） |
| `App.xaml(.cs)` | 510 | 组合根：单实例、托盘、任务栏偏好、设置读写分发、全局异常兜底、各事件串联 |
| `Rendering/GlassRenderer.cs` | 200 | 底板一次性渲染：薄雾渐变、顶部高光、内倒角、底部反光、柔和投影、内描边 |
| `Rendering/DockLayout.cs` | 160 | 纯几何：图标尺寸/间距/内边距 → 胶囊尺寸、窗口尺寸、图标坐标、命中判定 |
| `Interop/Displays.cs` | 155 | 显示器枚举、工作区、DPI、刷新率 |
| `Interop/NativeMethods.cs` | 200 | 全部 Win32 P/Invoke 声明 |
| `Interop/TrayIcon.cs` | 225 | 托盘图标（消息窗口 + `Shell_NotifyIcon`） |
| `Interop/TaskbarAutoHide.cs` | 85 | 通过 AppBar 接口开关任务栏自动隐藏，退出恢复原状态 |
| `Interop/TaskbarProbe.cs` | 50 | 测量 `Shell_TrayWnd` 真实矩形（不信 `WorkArea`） |
| `Interop/SingleInstance.cs` | 130 | 命名 Mutex + 事件：重复启动唤出已有实例 |
| `Themes/Palette.xaml` · `Styles.xaml` | 60 · 335 | 配色与控件样式（按钮 / 开关 / 滑块 / 滚动条） |
| `app.manifest` | 28 | PerMonitorV2 DPI 感知、`asInvoker` |
| `Assets/App.ico` | — | 应用图标（由 `tools/IconGen` 生成） |

## 3. 源码：`src/ZyDock.Core`（无 UI 的核心逻辑）

| 文件 | 职责 |
| --- | --- |
| `Models/DockSettings.cs` | 配置模型 + `Clamp()` 范围钳制（新增设置项从这里开始） |
| `Models/DockItem.cs` | 单个 Dock 项（名称、目标、启动方式、可选参数） |
| `Services/SettingsService.cs` | 配置读写：原子写、损坏备份、旧版本目录迁移 |
| `Services/IconProvider.cs` | 图标获取与缓存（256 px） |
| `Services/PackagedIconResolver.cs` | UWP 图标：`AppxManifest.xml` + 资源限定符择优 |
| `Services/FallbackIcons.cs` | 取不到图标时的矢量兜底 |
| `Services/AppLauncher.cs` | 启动：exe / 快捷方式 / 文件夹 / shell 位置 / UWP 激活 |
| `Services/DockItemFactory.cs` | 路径 → Dock 项（**拖入功能**复用） |
| `Services/LaunchCatalog.cs` | 默认图标清单与常用应用探测 |
| `Services/RunningAppTracker.cs` | 运行状态判定（状态点数据源，默认关闭） |
| `Services/AutoStartService.cs` | 开机启动（仅 `HKCU\...\Run`） |
| `Helpers/PathResolver.cs` | 路径解析：裸名字、环境变量、`ms-settings:` 等 shell 位置 |
| `Interop/ShellInterop.cs` | `IShellItemImageFactory`、回收站、快捷方式解析 |
| `Interop/ApplicationActivation.cs` | `IApplicationActivationManager`（UWP 激活） |
| `Diagnostics/Log.cs` | 追加写日志：2 MB 轮转 + **路径脱敏** |
| `AppPaths.cs` | 所有路径的唯一来源（配置/日志目录、`ZYDOCK_DATA_DIR` 覆盖、写权限探测回退） |

---

## 4. 数据流

```
设置窗口 ─┬─ 拖滑块 ──► DockSettings ──► SettingsService.Save ──► settings.json
          └─ 增删图标 ─┘                        │
                                                 ▼
                                    App.OnSettingsChanged
                                                 ▼
                        DockWindow.ApplySettings（重建图标 / 布局 / 底板）
                                                 ▼
                        DockLayout（几何） + GlassRenderer（底板位图）

拖入 / 拖动排序 ──► DockWindow 事件（ItemsDropped / ItemsReordered）
                                                 ▼
                        App 更新配置并保存 ──► 同上重排

点击图标 ──► DockWindow.Launch ──► AppLauncher ──► Shell / UWP 激活
图标显示 ──► IconProvider ──► ShellInterop / PackagedIconResolver ──► FallbackIcons
```

## 5. 「想改某个东西该动哪个文件」

| 目标 | 文件 |
| --- | --- |
| 玻璃观感（透明度、高光、边框、圆角） | `Rendering/GlassRenderer.cs`（`PlateSpec` 决定参数） |
| 尺寸 / 间距 / 内边距 / 命中范围 | `Rendering/DockLayout.cs` + `Models/DockSettings.cs` |
| 放大手感（跟随速度、放大范围） | `DockWindow.xaml.cs` 的 `OnFrame`（`MotionTau`、σ、`MagnifyFactor`） |
| 图标取法 / 清晰度 | `IconProvider.cs`、`PackagedIconResolver.cs` |
| 默认图标清单 | `LaunchCatalog.cs` |
| 设置界面字段 | `SettingsWindow.xaml` + `DockSettings.cs`（加字段要同时改两处） |
| 右键 / 托盘菜单项 | `App.xaml.cs`（菜单数据）+ `PopupMenuWindow.cs`（渲染） |
| 拖入与排序行为 | `DockWindow.xaml.cs` 的 `#region reorder by dragging` / `#region drop from Explorer` |
| 任务栏处理 | `TaskbarAutoHide.cs`、`TaskbarProbe.cs` |
| 日志内容 / 轮转 / 脱敏 | `Diagnostics/Log.cs` |

## 6. 环境变量

| 变量 | 作用 |
| --- | --- |
| `ZYDOCK_DATA_DIR` | 覆盖配置/日志目录（便携运行、自动化测试） |
| `ZYDOCK_DEBUG_INPUT=1` | 开启自动化消息钩子（仅 `DEBUG` 或 `ZYDOCK_AUTOMATION` 构建中存在） |

## 7. 单帧渲染上限说明

WPF 的逐像素 alpha 分层窗口每帧都要在软件合成下重绘整个窗口表面，因此单帧成本约 8–10 ms（100–135 fps），**与图标数量无关**（3 个图标、清空桌面实测相同）。要突破这一点必须换成 DirectComposition / D3D 合成，属于另一套渲染架构。

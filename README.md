# ZyDock

一个用 **C# / WPF / .NET 8** 写的 Windows 11 桌面 Dock：悬浮在屏幕底部、**近乎全透明的清玻璃**、磁吸放大动效、真实系统图标、拖入即添加、拖动即排序、配置可持久化。

无第三方依赖（只用 .NET 官方库）、不联网、不收集任何数据。

![桌面效果](docs/screenshots/desktop.png)

| Dock 近景 | 悬停放大 + 名称提示 |
| --- | --- |
| ![dock](docs/screenshots/dock.png) | ![hover](docs/screenshots/hover.png) |

---

## ✨ 功能

| 功能 | 说明 |
| --- | --- |
| 透明悬浮 Dock | 无边框、置顶、不进 Alt+Tab、**不抢焦点**，统一胶囊体居中贴底 |
| 清玻璃底板 | 不模糊、不取壁纸、无噪点；只有极淡薄雾 + 顶部高光 + 内描边 + 柔和投影，桌面直接透出 |
| 透明区穿透 | 自实现 `WM_NCHITTEST`，只有胶囊/图标/气泡可命中，阴影与留白不挡桌面点击 |
| 磁吸放大 | 指针距离驱动的缩放曲线（最高 1.62×，相邻递减），指数平滑、可随时打断 |
| 真实系统图标 | `IShellItemImageFactory` 取 256 px 真实图标；UWP 应用从 `AppxManifest.xml` 解析官方 logo |
| 悬停名称提示 | 图标上方淡入气泡 |
| 拖入即添加 | 从资源管理器把 exe / 快捷方式 / 文件夹拖到 Dock 上，提示「松开即可添加到 Dock」 |
| 拖动排序 | 按住图标左右拖动（> 5 px 才触发，单击仍是启动），拖动中实时让位，松手保存 |
| 右键菜单 | 图标：打开 / 在资源管理器中显示 / 从 Dock 移除；空白处：打开设置 |
| 托盘图标 | 打开设置、自动隐藏、任务栏、开机启动、重载配置、退出 |
| 设置窗口 | 外观 / 行为 / 图标管理 / 关于，改动 180 ms 防抖后即时生效并持久化 |
| 自动隐藏 | 鼠标离开滑出屏幕，移到屏幕底边唤回 |
| 高 DPI | manifest 声明 PerMonitorV2，150% / 200% 缩放下布局与清晰度正确 |
| 任务栏共存 | 直接测量 `Shell_TrayWnd` 真实位置，任务栏显示时浮在它上方、收起时贴底，永不重叠 |
| 单实例 | 重复启动不新开进程，而是唤出已运行实例的设置窗口 |

### 设置项

| 分组 | 项 | 范围 / 默认 |
| --- | --- | --- |
| 外观 | 图标大小 | 30–68 px（46） |
| 外观 | 图标间距 | 2–24 px（13） |
| 外观 | 内边距（左右 / 上下） | 0–80 / 0–60 px（30 / 18） |
| 外观 | 背景透明度 | 5%–92%（30%） |
| 外观 | 边框粗细 | 0–3 px（1，0 = 无边框） |
| 外观 | 玻璃高光 | 开 / 关 |
| 外观 | 鼠标放大 + 放大倍数 | 开 / 1.1–1.9（1.62） |
| 外观 | 名称提示 / 运行状态指示点 | 开 / 关 |
| 行为 | 自动隐藏、隐藏 Windows 任务栏、开机启动 | 关 / 关 / 关 |
| 行为 | 距屏幕底边 | 0–64 px（16） |
| 行为 | 显示屏幕 | 多显示器时可选 |

---

## 🚀 快速开始

### 方式 A：单文件版（最省事，推荐转发给别人）

`dist/ZyDock-1.0.0-singlefile/ZyDock.exe` —— 单个 **63 MB** exe，放进任意目录双击即可。
首次启动会把原生库解压到 `%TEMP%\.net`（之后复用缓存）。

### 方式 B：自包含目录版

`dist/ZyDock-1.0.0.zip`（≈61 MB）解压后得到 247 个文件（内含 .NET 8 运行时），双击 `ZyDock.exe`。
**必须整个目录一起用**：`ZyDock.exe` 只是 174 KB 的启动宿主，真正的代码在同目录的 `ZyDock.dll` / `ZyDock.Core.dll` 里。

> 未做代码签名，首次运行会出现 SmartScreen「Windows 已保护你的电脑」→「更多信息」→「仍要运行」。

#### 校验下载（可选但推荐）

发布页会给出每个文件的 **SHA-256**（同时提供 `SHA256SUMS.txt`）。下载后核对一下，可以确认文件在传输中没有损坏、也没有被替换：

```powershell
# 单个文件
Get-FileHash .\ZyDock.exe -Algorithm SHA256

# 或对着清单批量校验（文件名与清单里的名字一致时）
Get-Content .\SHA256SUMS.txt | ForEach-Object {
    if ($_ -match '^([0-9a-f]{64})\s+(.+)$') {
        $expect = $matches[1]; $name = $matches[2].Trim()
        $actual = (Get-FileHash ".\$name" -Algorithm SHA256).Hash
        '{0,-34} {1}' -f $name, $(if ($actual -eq $expect) { 'OK' } else { 'MISMATCH' })
    }
}
```

CMD 也可以用 `certutil -hashfile ZyDock.exe SHA256`；装了 7-Zip 的话右键 → `CRC SHA` → `SHA-256`。

> 哈希只能证明「和你发布的那份是同一个文件」，不能证明来源身份 —— 那是代码签名的事（本项目未签名）。

### 从源码构建

```powershell
# 编译（自动使用仓库内 .tools\dotnet 的 .NET 8 SDK，离线可编译）
powershell -ExecutionPolicy Bypass -File build.ps1

# 发布：自包含目录版 + zip；加 -SingleFile 同时产出单文件版
powershell -ExecutionPolicy Bypass -File publish.ps1
powershell -ExecutionPolicy Bypass -File publish.ps1 -SingleFile
```

环境搭建（含离线获取 SDK）见 **[docs/BUILD.md](docs/BUILD.md)**。

### 操作方式

| 操作 | 行为 |
| --- | --- |
| 左键单击图标 | 启动应用 / 打开文件夹 / 打开 shell 位置（失败会在 Dock 上方提示原因） |
| 悬停图标 | 磁吸放大 + 名称气泡 |
| **按住图标左右拖** | 调整顺序（> 5 px 才进入拖动） |
| **从资源管理器拖文件到 Dock** | 添加为 Dock 项 |
| 右键图标 | 打开 / 在资源管理器中显示 / 从 Dock 移除 / 编辑设置 |
| 右键 Dock 空白处 | 打开设置 |
| 托盘图标（左 / 右） | 设置 / 功能菜单 |
| 再次双击 exe | 唤出已运行实例的设置窗口（单实例） |
| 设置窗口「关闭」 | 收起到后台继续运行 |

> 自动隐藏开启时，把鼠标移到屏幕**底边**（Dock 宽度范围内）即可唤回。

---

## 🔧 技术要点

**分层窗口 + 自实现命中测试**
`WindowStyle=None + AllowsTransparency=true` 得到逐像素 alpha 的 layered window，再叠加 `WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE` 使其不进 Alt+Tab、不抢焦点。`WM_NCHITTEST` 把屏幕坐标换算成窗口 DIP，只有「圆角胶囊 / 放大后的图标矩形 / 气泡」返回 `HTCLIENT`，其余一律 `HTTRANSPARENT`，所以阴影和顶部预留空间都不会吞点击。

**逐帧驱动、时间相关的动效**
不用 `BeginAnimation`（互相打断会跳变）。动画由 `CompositionTarget.Rendering`（跟随屏幕 vsync）驱动，每个图标做指数平滑 `current += (target - current) * (1 - e^(-dt/τ))`（τ ≈ 52 ms），目标值由距离决定 `1 + (factor-1) · e^(-(d/σ)²)`（σ ≈ 1.55 × 图标尺寸）。全部收敛后立刻摘掉渲染回调 —— **静止时零帧开销**；动效与刷新率无关（本机 160 Hz 面板实测帧间隔 8–10 ms）。

**图标管线**
普通程序 / 文件夹 / shell 位置走 `IShellItemImageFactory.GetImage(256)`；UWP 应用的 item 图像工厂只返回空白文档图标，因此 `PackagedIconResolver` 走 `AUMID → HKCU AppModel Repository → AppxManifest.xml → Square44x44Logo(targetsize-256_altform-unplated)` 取真实 logo；全都拿不到时才退回内置矢量图。

**底板一次性渲染**
`GlassRenderer` 把薄雾渐变、顶部高光、内倒角、底部反光、柔和投影一次性画进冻结的 `RenderTargetBitmap`，只有尺寸/设置变化才重建。悬停动画只改图标 `ScaleTransform`，按帧成本只有几次缓存位图缩放。

**DPI / 多屏 / 任务栏**
manifest 声明 PerMonitorV2；几何按目标屏幕物理像素计算，再用 `GetWindowRect` 回读校正；`DpiChanged` 与 `DisplaySettingsChanged` 都会重排。任务栏高度不信任 `WorkArea`（自动隐藏生效后它会报全屏），而是直接测量 `Shell_TrayWnd` 窗口矩形。

**健壮性**
全局异常（`DispatcherUnhandledException` / `AppDomain.UnhandledException` / `UnobservedTaskException`）兜底写日志；配置原子写（临时文件 + `File.Replace`），损坏自动备份并回退默认；退出时释放托盘、停所有定时器、恢复任务栏状态。

---

## 🔒 安全与隐私

| 项目 | 结论 |
| --- | --- |
| 网络 | **零联网代码**（无 `HttpClient` / `Socket` / 遥测 / 自动更新） |
| 第三方依赖 | **0 个 NuGet 包**，仅 .NET 8 + WPF 官方库 |
| 权限 | `asInvoker`，从不请求管理员，不装服务、不改 HKLM、不替换 Shell |
| 敏感 API | 不模拟输入、不安装钩子、不读写其他进程内存 |
| 注册表 | 仅在你打开「开机启动」时写一条 `HKCU\...\Run`，可随时关闭 |
| 文件写入 | 仅 `%APPDATA%\ZyDock`（配置）与 `%LOCALAPPDATA%\ZyDock\logs`（日志） |
| 拖入行为 | 只加入列表，不会自动执行；执行仅发生在点击时 |
| 日志 | 本地追加写、2 MB 上限 + 一次轮转；**路径已脱敏**（用户名替换为 `%USERPROFILE%`） |
| 调试钩子 | 仅 `DEBUG` 或显式 `ZYDOCK_AUTOMATION` 构建中存在，正式 Release 编译期移除 |

---

## 📁 项目结构

```
ZyDock/
├─ src/
│  ├─ ZyDock.App/          WPF 表现层（窗口、绘制、交互、设置界面）
│  └─ ZyDock.Core/         无 UI 的核心逻辑（配置、图标、启动、日志）
├─ docs/
│  ├─ ARCHITECTURE.md      每个源文件的职责 + 数据流 + 「改哪里」速查
│  ├─ BUILD.md             构建 / 发布 / 离线环境说明
│  ├─ 使用说明.txt          面向使用者的说明（随发布包附带）
│  └─ screenshots/         截图
├─ tools/                  开发与验证脚本（截图、光标/拖拽注入、窗口枚举、图标生成）
├─ build.ps1  publish.ps1  构建 / 发布入口
├─ Directory.Build.props   版本号、语言版本、隐式 using
├─ nuget.config            本地离线包源
├─ CHANGELOG.md            逐轮变更记录
└─ ZyDock.sln
```

详细文件说明见 **[docs/ARCHITECTURE.md](docs/ARCHITECTURE.md)**。

---

## ⚠️ 已知限制

1. Dock 固定贴屏幕底边，**顶部 / 侧边布局未实现**。
2. 多显示器选择逻辑已实现，但开发机只有一块屏，**跨屏与跨屏 DPI 迁移未实测**。
3. 未做代码签名，SmartScreen 会提示未知发布者。
4. 单帧渲染上限约 100–135 fps：WPF 逐像素 alpha 分层窗口的固定开销，与图标数量无关；要锁满 160 fps 需改用 DirectComposition/D3D 合成。
5. 系统亚克力（DWM Acrylic）实测会在分层窗口上产生整窗矩形灰块，因此**未提供**。

---

## 📝 变更记录

见 **[CHANGELOG.md](CHANGELOG.md)**（从初版到当前的全部迭代，含每次踩坑与验证数据）。

## 📄 许可

当前未附加许可证文件。如需开源分发，建议自行选择（MIT / Apache-2.0 等），或继续保持「保留所有权利」。

代码除 .NET 官方运行时外无任何第三方组件，可自由修改。

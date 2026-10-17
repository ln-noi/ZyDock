# 构建与发布

本文说明如何在另一台机器上复现构建、发布与验证。仓库**零 NuGet 依赖**，可完全离线构建。

---

## 1. 环境要求

| 项 | 值 |
| --- | --- |
| 操作系统 | Windows 10 / 11 x64（manifest 同时声明 Win10 兼容） |
| .NET SDK | **8.0.426**（`net8.0-windows`），或任意 8.0.x SDK |
| 运行时（SDK 自带） | `Microsoft.NETCore.App` / `Microsoft.WindowsDesktop.App` 8.0.32 |
| NuGet 依赖 | **零**（不引用任何第三方包） |
| Visual Studio | 不需要 |

### SDK 位置

构建脚本优先使用仓库内 SDK `.tools\dotnet\dotnet.exe`，不存在时退回 PATH 上的 `dotnet`。

如果机器上没有 .NET 8 SDK，可以用仓库里的 Node 脚本把官方 SDK 下到仓库内（不污染系统目录）：

```powershell
node tools\fetch-sdk.mjs "https://builds.dotnet.microsoft.com/dotnet/Sdk/8.0.426/dotnet-sdk-8.0.426-win-x64.zip" ".tools\dotnet-sdk.zip"
tar -xf .tools\dotnet-sdk.zip -C .tools\dotnet
```

> 之所以用 Node 而不是 `curl` / `Invoke-WebRequest`：某些受限环境（如 agent 沙箱）里 .NET/PowerShell 的 SChannel TLS 不可用，Node 自带的 TLS 栈可以正常工作。

---

## 2. 构建

```powershell
powershell -ExecutionPolicy Bypass -File build.ps1            # Release
powershell -ExecutionPolicy Bypass -File build.ps1 -Debug     # Debug
powershell -ExecutionPolicy Bypass -File build.ps1 -Clean     # 先清理 bin/obj
```

脚本通过 `tools\env.ps1` 设置：

```
DOTNET_ROOT      = <repo>\.tools\dotnet
DOTNET_CLI_HOME  = <repo>\.tools\home
NUGET_PACKAGES   = <repo>\.tools\nuget
TEMP / TMP       = <repo>\.tools\temp
```

把临时目录也固定在仓库内，是为了让构建在「用户目录 / `%TEMP%` 被策略限制」的环境里同样能跑通（例如受限令牌下的沙箱）。普通开发机上不设置也没问题。

等价裸命令：

```powershell
$env:DOTNET_ROOT = "$PWD\.tools\dotnet"
$env:PATH = "$env:DOTNET_ROOT;$env:PATH"
$env:DOTNET_CLI_HOME = "$PWD\.tools\home"
$env:TEMP = "$PWD\.tools\temp"
.\tools\dotnet\dotnet.exe build ZyDock.sln -c Release
```

---

## 3. 开发期运行

```powershell
powershell -ExecutionPolicy Bypass -File tools\run-app.ps1                          # 使用默认配置目录
powershell -ExecutionPolicy Bypass -File tools\run-app.ps1 -DataDir .tools\appdata  # 独立配置目录
```

> ⚠️ **不要在受限令牌的沙箱里直接运行 `ZyDock.exe`。** 落在工作区目录下的可执行文件若被施加受限令牌，会出现四个症状：托盘图标不出现（`Shell_NotifyIcon` 拒绝）、任务栏自动隐藏不生效（`SHAppBarMessage` 返回 0）、配置写不进 `%APPDATA%`（回退到 `exe 同目录\data\`）、UWP 激活返回 `E_ACCESSDENIED` 并弹 `explorer.exe - 0xc0000142`。
> 把程序放到 `C:\Users\<你>\Apps\ZyDock\` 之类的普通用户目录下运行则一切正常。

---

## 4. 发布

```powershell
# 默认：自包含目录版 + zip
powershell -ExecutionPolicy Bypass -File publish.ps1

# 额外产出单文件版
powershell -ExecutionPolicy Bypass -File publish.ps1 -SingleFile

# 额外产出框架依赖版（体积小，但要求目标机装了 .NET 8 Desktop Runtime）
powershell -ExecutionPolicy Bypass -File publish.ps1 -FrameworkDependent

# 只构建不打 zip
powershell -ExecutionPolicy Bypass -File publish.ps1 -SkipZip
```

### 自包含发布需要的离线包

`--self-contained` 需要 runtime pack。`nuget.config` 已把包源指向本地目录 `.tools\nuget-local`（并关闭公网源），首次发布前执行一次：

```powershell
node tools\fetch-packs.mjs .tools\nuget-local `
  "Microsoft.NETCore.App.Runtime.win-x64:8.0.32" `
  "Microsoft.WindowsDesktop.App.Runtime.win-x64:8.0.32" `
  "Microsoft.AspNetCore.App.Runtime.win-x64:8.0.32" `
  "Microsoft.NET.ILLink.Tasks:8.0.32"
```

> 版本号必须与本机 SDK 的运行时一致（`dotnet --list-runtimes` 里 `Microsoft.WindowsDesktop.App` 的版本）。

### 产物

| 路径 | 大小 | 说明 |
| --- | --- | --- |
| `dist\ZyDock-<ver>\` | ≈145 MB / 247 文件 | **自包含目录版**（默认产物），内含 .NET 8 运行时 |
| `dist\ZyDock-<ver>.zip` | ≈61 MB | 上面那份的分发包 |
| `dist\ZyDock-<ver>-singlefile\ZyDock.exe` | ≈63 MB | 单文件版（`-SingleFile`），启动时解压到 `%TEMP%\.net` |
| `dist\ZyDock-<ver>-singlefile.zip` | ≈58 MB | 单文件版分发包 |
| `dist\optional\ZyDock-<ver>-needs-dotnet8-runtime\` | ≈0.4 MB | 框架依赖版（`-FrameworkDependent`，需目标机装 .NET 8 Desktop Runtime） |

单文件版解压目录不可写时，可用环境变量 `DOTNET_BUNDLE_EXTRACT_BASE_DIR` 指定别处。

> `dist\` 已在 `.gitignore` 中；发到 GitHub 时应作为 **Release Assets** 上传，而不是提交进仓库。

---

## 5. 图标资源

`Assets\App.ico` 由 `tools\IconGen` 生成（9 种尺寸 16/20/24/32/40/48/64/128/256，PNG 载荷）：

```powershell
.\tools\dotnet\dotnet.exe build tools\IconGen\IconGen.csproj -c Release -o .tools\icongen
.\.tools\icongen\IconGen.exe src\ZyDock.App\Assets\App.ico
```

托盘图标优先取 `Assets\App.ico`；单文件发布时该文件不一定随包，此时从 `ZyDock.exe` 内嵌的应用图标里 `ExtractIconEx` 取，最后才退回系统默认图标。

---

## 6. 验证辅助脚本

```powershell
# 截屏 / 裁剪 / 移动光标
powershell -File tools\capture-screen.ps1 -Path shot.png
powershell -File tools\crop-image.ps1 -Source shot.png -Out crop.png -X 1200 -Y 1800 -W 1600 -H 300 -Scale 1.0
powershell -File tools\move-cursor.ps1 -X 1200 -Y 600

# 真实鼠标拖拽（验证拖动排序、拖入）
powershell -File tools\mouse-drag.ps1 -FromX 1600 -FromY 2020 -ToX 1800 -ToY 2020

# 枚举进程窗口（含位置 / 样式），排查定位与 DPI 问题
powershell -File tools\inspect-windows.ps1 -ProcessId <pid> -Filter ZyDock

# 驱动真实悬停 / 点击代码路径（需要 ZYDOCK_DEBUG_INPUT=1，且为 Debug 或 ZYDOCK_AUTOMATION 构建）
powershell -File tools\dock-debug.ps1 -Hover 184
powershell -File tools\dock-debug.ps1 -ClickX 352 -ClickY 96
```

自动化构建（保留调试钩子）：

```powershell
.\tools\dotnet\dotnet.exe build ZyDock.sln -c Release -p:ZydockAutomation=true
```

---

## 7. 已验证 / 未验证

**已实测**

- 编译零警告零错误；自包含目录版、自包含单文件版、框架依赖版均可运行
- Dock 定位与任务栏共存（测量 `Shell_TrayWnd`）、清玻璃底板、图标清晰度（256 px）、磁吸放大曲线、名称气泡
- 透明区命中测试（`HTTRANSPARENT`）、150% DPI 排版、160 Hz 屏逐帧驱动（8–10 ms/帧）
- 点击启动（记事本 / 文件夹 / 回收站 / UWP 设置与媒体播放器）、托盘图标、任务栏自动隐藏（回读状态=1）、开机启动注册表写入
- 拖入 `notepad.exe` + `Desktop` 文件夹（8→10 项并写盘）、真实鼠标拖动排序并持久化
- 配置原子写与重载、自动隐藏与边缘唤回、单实例、日志脱敏、正式版调试钩子失效

**未实测**

- 多显示器 / 跨屏 DPI 迁移（开发机单屏）
- 非 150% 缩放比例的排版细节（仅逻辑保证）
- 顶部 / 侧边 Dock 布局（未实现）

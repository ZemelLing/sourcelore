# Windows x64 构建

本工程使用 .NET SDK 10.0.401、Visual Studio 2022 的“使用 C++ 的桌面开发”和 Windows SDK。
本机构建工具链为 VS 17.14.41、MSVC 14.44.35207、Windows SDK 10.0.26100.0。
依赖版本和来源见 [dependencies.md](dependencies.md)。需要联网进行首次 NuGet 还原。

在仓库根目录运行：

```powershell
dotnet restore SourceLore.slnx --locked-mode
dotnet build SourceLore.slnx --no-restore
$env:SOURCELORE_SERVER_EXE = (Resolve-Path artifacts/research/loreserver-v0.10.0/loreserver.exe).Path
dotnet test --project tests/SourceLore.Core.Tests --no-restore
dotnet test --project tests/SourceLore.Lore.IntegrationTests --no-restore
./scripts/Publish-Windows.ps1
$env:SOURCELORE_PUBLISHED_EXE = Join-Path (Get-Location) 'artifacts/publish/win-x64/SourceLore.App.exe'
dotnet test --project tests/SourceLore.App.SmokeTests --no-restore --output Detailed
```

测试使用 Microsoft.Testing.Platform（已在 global.json 和测试工程中显式配置）。
发布脚本只为当前子进程设置 `VSCMD_SKIP_SENDTELEMETRY=1`，避免无人值守环境卡在 VS 开发环境遥测；不会改变系统设置。
不需要手工修改 Visual Studio 或 NuGet 包内文件。

发布目录为 `artifacts/publish/win-x64`，日志为 `artifacts/publish.log`，SHA256 清单为 `manifest.sha256`。
完整复制目录，运行 `SourceLore.App.exe`。目录必须包含 `lorelib.dll`、`libSkiaSharp.dll`、
`libHarfBuzzSharp.dll`、`av_libglesv2.dll`、Lore 第三方通知以及 `licenses/`。
PDB 可用于诊断；不能遗漏 DLL 或许可文本。核心 .NET 代码由 NativeAOT 编译进 exe；不需要额外安装 .NET、Git 或 Lore CLI。
“无这些软件的干净机器”运行仍须单独验收，不用本机构建成功代替。

自动窗口冒烟入口（程序完成检查后自行退出）：

```powershell
$report = Join-Path (Get-Location) 'artifacts/ui-smoke.txt'
$process = Start-Process ./artifacts/publish/win-x64/SourceLore.App.exe -ArgumentList @('--smoke-ui', $report) -PassThru -WindowStyle Hidden
$process.WaitForExit()
Get-Content $report
```

该检查要求 `RuntimeFeature.IsDynamicCodeSupported == false`，验证窗口可见、UI 派发、标题编译绑定和正常退出。
基础窗口检查不代表客户端功能已交付。所需固定服务器下载、哈希和启动隔离方法见
[controlled-server.md](controlled-server.md)；测试不会隐式下载或使用外部用户服务。

发布包工作流测试通过独立 AOT 进程打开 SDK 创建的隔离中文路径仓库，检查原生状态、差异、
暂存/取消暂存、暂存后编辑、选择提交与未选内容保留，以及无效提交/仓库、只读控件行号、滚动、选择和复制。
复制检查会在操作前读取并保存剪贴板的所有可用数据格式，检查后恢复。
依赖测试另行复制 exe 和渲染 DLL 到隔离目录，分别检查缺失和损坏的 lorelib.dll；测试清理只针对自己的目录。
另有完整 AOT 双克隆测试 `NativeAotSevenCapabilitiesRunThroughTwoClonesAndControlledServer`：
发布进程中执行克隆、编辑、两种差异、选择提交、推送、第二目录同步、历史比较与分支操作。
它只接受回环服务器和 `%TEMP%/SourceLore.OnlineSmokeTests/` 下的隔离目录，不触及外部测试服务。
通过这些测试仍不能代替干净 Windows 系统和真实受保护服务验收。

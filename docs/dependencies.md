# 固定依赖

初始组合（2026-10-13），以各项目 `packages.lock.json` 为完整传递依赖记录：

| 依赖 | 版本 | 来源 |
| --- | --- | --- |
| .NET SDK | 10.0.401 | 本机 SDK，global.json 禁止自动滚动 |
| Avalonia / Desktop / Fluent | 12.1.4 | nuget.org，AvaloniaUI |
| Avalonia.AvaloniaEdit | 12.0.0 | nuget.org，AvaloniaUI |
| LoreVcs | 0.10.0 | nuget.org，EpicGames/lore-dotnet |
| LoreVcs.runtime.win-x64 | 0.10.0 | nuget.org，随包携带 runtimes/win-x64/native/lorelib.dll |
| xunit.v3 / VS adapter | 4.0.2 / 4.0.1 | nuget.org，仅测试 |
| Microsoft.NET.Test.Sdk | 18.10.1 | nuget.org，仅测试 |

包源限定为 nuget.org。正式构建使用 `--locked-mode`，升级依赖时显式修改集中版本、更新锁文件并重新执行 AOT 验证。
LoreVcs 0.10.0 包中实际提供 net9.0 程序集，.NET 10 可引用；没有假定它与上游主分支源码完全一致。
原生库由配套运行包提供，不从 PATH 或用户安装的 Lore CLI 拷贝。配套 loreserver 0.10.0
已通过隔离本地服务器集成检查。外部服务与受保护浏览器认证仍待验收。

发行许可检查包括每个还原包的 nuspec、包内许可/通知和 Lore 原生第三方通知。
仅声明 MIT 而未携带完整文本的 15 个包，使用其 nuspec 指定源码提交的上游许可文本补齐，
来源和 SHA256 固定在 `third-party-licenses/sources.json`；发布脚本核对提交与哈希，不联网替换文本。

上游：[Lore SDK](https://github.com/EpicGames/lore-dotnet)、[Lore](https://github.com/EpicGames/lore)、[Avalonia](https://github.com/AvaloniaUI/Avalonia)、[AvaloniaEdit](https://github.com/AvaloniaUI/AvaloniaEdit)。

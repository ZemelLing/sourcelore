# Spec Delta

## Purpose

为独立 Lore 桌面客户端定义 Windows x64 运行与发行契约，使用户能够直接启动应用、理解依赖故障，并在仓库操作期间继续查看状态和使用不冲突的界面功能。

## ADDED Requirements

### Requirement: Self-contained Windows x64 distribution

客户端 MUST 提供 Windows x64 原生编译的发行包，包含运行所需的应用资源和 Lore 原生依赖。用户运行该发行包 SHALL 无需另行安装 .NET SDK、.NET 托管运行时或 Lore CLI。

#### Scenario: Launch from an extracted release package
- **WHEN** 用户在符合发行说明系统要求、未安装 .NET 或 Lore CLI 的 Windows x64 环境解压完整发行包并启动应用
- **THEN** 应用显示可交互的仓库入口，并能够读取有效本地 Lore 工作目录的状态

### Requirement: Actionable startup diagnostics

客户端 SHALL 在原生依赖缺失、位数错误或已知版本不兼容时显示依赖名称、失败原因和恢复建议，并阻止受影响的仓库操作。错误信息和诊断日志 MUST 不包含认证凭据。

#### Scenario: Native dependency is unavailable
- **WHEN** 发行包中的 Lore 原生库缺失或不能加载
- **THEN** 应用提供明确的修复提示且不将任何仓库展示为已成功连接

### Requirement: Responsive operation presentation

客户端 SHALL 为耗时的扫描、差异加载、历史查询和远端操作显示进行状态，在执行期间保持窗口重绘和导航响应。进度未知时 SHALL 使用不确定进度指示；操作完成或失败 SHALL 显示真实结果。

#### Scenario: Long-running repository operation
- **WHEN** 某仓库正在执行耗时扫描或同步
- **THEN** 用户仍可移动窗口、查看操作状态及浏览已加载内容，界面不因等待操作完成而冻结

### Requirement: Independent Lore client identity

客户端 SHALL 以 Lore 的仓库、版本、暂存、同步和分支概念呈现工作流，并在可见导航中提供工作区和历史入口。首版 SHALL 不依赖 Git 可执行程序来完成任何 Lore 操作。

#### Scenario: Use a machine without Git
- **WHEN** 用户在未安装 Git 的系统上打开有效 Lore 工作目录
- **THEN** 工作区、差异、提交和历史功能仍可通过 Lore 后端使用

# Spec Delta

## Purpose

管理用户访问 Lore 工作目录和远端仓库的入口，使多个工作目录保持独立会话，并在目录无效、克隆中断或认证失效时提供可理解且不会破坏已有文件的恢复路径。

## ADDED Requirements

### Requirement: Open and identify a Lore working directory

客户端 SHALL 接受本地目录路径，通过 Lore 后端验证并解析工作目录身份，显示根路径、当前分支和当前版本。无效或不受支持的目录 SHALL 被拒绝，且不得自动初始化或转换该目录。

#### Scenario: Open a nested directory
- **WHEN** 用户选择有效 Lore 工作目录内的子目录
- **THEN** 客户端解析其所属工作目录并打开该会话

#### Scenario: Reject a non-Lore directory
- **WHEN** 用户选择普通文件夹或只有 Git 元数据的仓库
- **THEN** 客户端解释无法打开的原因并保留目录内容

### Requirement: Isolate workspace sessions and recent entries

客户端 SHALL 按 Lore 工作目录实例隔离选择、提交说明草稿和操作状态，避免同一目录重复建立冲突会话，并保存最近成功打开的目录。相同远端的不同本地工作目录 SHALL 保持独立。

#### Scenario: Switch between workspaces
- **WHEN** 用户从工作目录 A 切换到 B，随后 A 的历史或差异查询才返回
- **THEN** 该结果不会覆盖 B 的界面，且返回 A 后仍能看到 A 自己的草稿和状态

#### Scenario: Reopen a missing recent directory
- **WHEN** 最近记录指向已移动或删除的目录
- **THEN** 客户端报告目录不可用，并允许移除记录而不删除任何磁盘内容

### Requirement: Clone into an explicit destination

客户端 SHALL 支持输入 Lore 远端地址和本地目标目录完成克隆，并在成功后打开工作目录。目标目录必须不存在或为空；失败时 SHALL 保留诊断及残留路径信息，不覆盖用户原有内容，也不自动删除失败克隆目录。

#### Scenario: Successful clone
- **WHEN** 用户指定可访问远端和有效空目标目录并完成克隆
- **THEN** 新工作目录被打开并加入最近记录

#### Scenario: Reject a populated destination
- **WHEN** 用户指定包含文件的目标目录
- **THEN** 客户端在开始克隆前拒绝目标并要求选择其他路径

### Requirement: Use Lore authentication lifecycle

客户端 SHALL 使用 Lore 支持的认证流程访问受保护远端，显示登录结果，并在取消或凭据失效时保留本地工作。用户输入和返回的令牌 MUST 不出现在普通配置、最近记录、日志或错误详情中。

#### Scenario: Authentication expires
- **WHEN** 远端操作返回认证失败或凭据过期
- **THEN** 客户端给出重新登录入口；完成登录后由用户显式重试，原操作不被自动重复执行

#### Scenario: Cancel authentication
- **WHEN** 用户取消浏览器认证或认证过程失败
- **THEN** 客户端显示未完成认证，保留当前本地会话且不报告远端操作成功

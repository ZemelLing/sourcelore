# Spec Delta

## Purpose

使程序员能够显式发布本地提交和取得远端更新，在认证、网络、分支并发及内容冲突发生时看到真实状态，并通过可恢复流程继续工作而不隐式丢弃本地内容。

## ADDED Requirements

### Requirement: Push and synchronize as explicit operations

客户端 SHALL 分别提供推送和同步入口，显示工作目录、目标远端及分支。推送 SHALL 发布已有本地提交；同步 SHALL 取得目标分支状态并按 Lore 语义处理本地分歧。两者 MUST 不自动提交未提交文件，也不隐含强制重置。

#### Scenario: Push existing local commits
- **WHEN** 用户在有本地提交的分支选择推送
- **THEN** 客户端仅发起该分支的推送，显示进度，并在后端确认成功后更新远端状态

#### Scenario: Remote branch has advanced
- **WHEN** 推送因远端分支已前进而被拒绝
- **THEN** 客户端解释分歧并提供同步入口，不自动强制推送或覆盖远端

### Requirement: Preserve local work during synchronization

客户端 SHALL 在同步前展示目标及检测到的本地修改或未推送状态，使用保留本地工作的操作模式。后端不能安全合并时 SHALL 停止或进入显式冲突流程，不擅自选择本地/远端一方，也不删除阻挡同步的未跟踪文件。

#### Scenario: Synchronization would overwrite local work
- **WHEN** 目标更新与本地修改或未跟踪文件发生覆盖风险
- **THEN** 客户端拒绝破坏性继续或展示后端报告的冲突，并保留用户内容

### Requirement: Resolve conflicts through an explicit recovery flow

客户端 SHALL 展示后端报告的冲突文件及解决状态，允许用户在外部工具编辑后显式标记解决，并在全部冲突解决后继续后端规定的流程。中止入口 SHALL 说明影响、要求确认并采用已验证的 Lore 语义，不能承诺恢复后端无法恢复的数据。

#### Scenario: Resolve a text conflict
- **WHEN** 用户在外部编辑器处理冲突并返回客户端标记该文件已解决
- **THEN** 客户端通过后端记录和重新核实解决状态，只有后端确认所有冲突已解决才允许继续

#### Scenario: Abort a conflicted operation
- **WHEN** 用户选择中止并确认所展示的影响
- **THEN** 客户端执行对应中止操作并核对实际状态；不在确认前丢弃内容，也不把失败的中止显示为成功

#### Scenario: Reopen a workspace with unresolved conflicts
- **WHEN** 应用重启后打开仍存在冲突状态的工作目录
- **THEN** 客户端恢复冲突入口，并阻止普通提交、同步或分支切换绕过未完成流程

### Requirement: Prevent overlapping mutations

客户端 MUST 不在同一工作目录同时执行提交、同步、推送、分支切换或冲突写操作。按钮禁用或请求排队 SHALL 向用户说明当前操作；外部 CLI 或其他客户端引起的竞争 SHALL 通过后端结果和状态核对处理。

#### Scenario: Attempt a branch switch during synchronization
- **WHEN** 用户在同步尚未结束时请求切换同一工作目录的分支
- **THEN** 客户端阻止并行执行并显示等待原因

### Requirement: Recover from interrupted or uncertain operations

客户端 SHALL 在远端连接中断、应用重启或完成结果不明确时区分失败与结果待核实，重新查询本地及可获得的远端状态，并要求用户显式重试。MUST 不自动重放提交、推送或同步，不通过杀死应用来宣称已安全取消写操作。

#### Scenario: Network disconnects during push
- **WHEN** 推送连接在收到完成结果前断开
- **THEN** 客户端保留本地提交，显示结果待核实或后端明确的失败，核对后提供显式重试入口

# Spec Delta

## Purpose

为程序员提供 Lore 分支的基础导航、创建和切换能力，始终明确当前工作目录实际所在的分支与版本，并在本地修改、离线或后端失败时避免隐式覆盖和误报切换结果。

## ADDED Requirements

### Requirement: List and identify branches

客户端 SHALL 列出后端可见分支并明确标识当前分支、后端返回的分支身份和可用版本信息。离线或刷新失败时 SHALL 标记列表来自缓存或可能过期，不伪造其他分支已不存在。

#### Scenario: Refresh branches while offline
- **WHEN** 用户离线查看分支列表
- **THEN** 客户端显示可获得的本地信息及其限制，当前分支以工作目录实际状态为准

### Requirement: Create and switch a branch from the current revision

客户端 SHALL 支持输入分支名称并从当前版本创建且切换到新分支，使用后端的命名和重复检查规则。入口 SHALL 明确标为“创建并切换”并显示当前起始版本，成功后 SHALL 刷新列表并核对实际当前分支和版本。首版 SHALL 不提供历史版本创建或仅创建不切换选项。创建前 SHALL 核对本地变更和未完成操作；存在冲突或无法保证本地内容和暂存意图保留时 MUST 阻止操作。

#### Scenario: Create and switch from the current revision
- **WHEN** 用户在预检通过的工作目录中选择“创建并切换”并输入有效的新分支名
- **THEN** 新分支出现在列表中，后端核对确认当前分支为新分支，起始版本为操作前显示的当前版本，界面刷新工作区与历史

#### Scenario: Block unsafe creation or reconcile uncertain results
- **WHEN** 创建并切换存在未完成冲突、无法保留本地工作，或后端返回失败或结果不确定
- **THEN** 客户端阻止不安全的创建，或在已启动操作后重新读取实际分支和版本，不乐观地把新分支标记为当前，也不隐含重置文件

#### Scenario: Reject an invalid or duplicate branch name
- **WHEN** 后端拒绝分支名称或报告分支已存在
- **THEN** 客户端显示具体原因并保留当前分支与本地修改

### Requirement: Switch without implicit data loss

客户端 SHALL 在切换前显示目标分支并核对本地变更和未完成操作。存在冲突流程时 MUST 阻止切换；普通修改和暂存选择只有在能够保留时才能继续，否则 SHALL 拒绝切换并解释原因，首版不得隐含执行重置。

#### Scenario: Switch a clean workspace
- **WHEN** 用户从无本地修改且无未完成操作的工作目录切换到可访问分支
- **THEN** 切换成功后界面更新当前分支、版本、变更与历史，原分支的迟到查询不覆盖新状态

#### Scenario: Preserve staged local edits
- **WHEN** 用户带有已暂存或未暂存的修改请求切换分支
- **THEN** 客户端核实操作能保留相关内容和提交选择后才继续，否则保持当前工作并提示先处理修改

#### Scenario: Switch fails or its result is uncertain
- **WHEN** 后端切换失败或未返回可确认的完成结果
- **THEN** 客户端重新读取实际分支和版本，不乐观地把目标分支标记为当前分支

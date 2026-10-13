# Spec Delta

## Purpose

为程序员提供准确的本地变更列表和文件级提交工作流，将文件内容变化与参与下一次提交的选择分别呈现，并在外部编辑、扫描异常或提交失败时保留用户尚未提交的工作。

## ADDED Requirements

### Requirement: Discover and refresh working changes

客户端 SHALL 在打开工作目录时执行初始状态核对，随后发现外部新增、修改和删除，并提供手动重新扫描入口。扫描中、状态可能过期和扫描失败 MUST 与无变更状态区分显示；文件忽略与仓库边界 SHALL 遵循 Lore 的结果。

#### Scenario: Edit files outside the client
- **WHEN** 外部编辑器新增、修改或删除工作目录内未被忽略的文件
- **THEN** 客户端更新相应变更，在扫描完成前不把旧列表宣称为最新状态

#### Scenario: Recover after missed filesystem events
- **WHEN** 客户端检测到监控事件丢失，或者用户手动刷新
- **THEN** 客户端重新扫描并核对状态，清除已恢复文件的过期变更记录

#### Scenario: Scan is incomplete
- **WHEN** 扫描因为文件无法访问或后端错误而未完成
- **THEN** 界面显示错误和状态不完整标识，不把部分结果当作完整且干净的工作区

### Requirement: Stage files as inclusion intent

客户端 SHALL 支持按文件暂存和取消暂存，并将实际 Lore 暂存状态作为是否参与提交的依据。界面 MUST 说明暂存文件在提交时读取最新内容，不承诺暂存时内容快照，也不提供按行或块暂存。

#### Scenario: Edit a staged file again
- **WHEN** 用户暂存文件后继续修改该文件
- **THEN** 文件保持已暂存状态，其预览随刷新更新，提交采用后端在提交时读取的内容

#### Scenario: Unstage without reverting
- **WHEN** 用户取消某已修改文件的暂存
- **THEN** 该文件不再参与下一次提交，但磁盘修改仍保留并显示为未暂存变更

#### Scenario: Partial staging failure
- **WHEN** 批量暂存中部分文件成功而部分失败
- **THEN** 客户端报告失败路径并重新读取实际暂存状态，不把整个批次显示为全部成功

### Requirement: Commit staged files locally

客户端 SHALL 在说明非空、存在可提交的已暂存变更且无待解决冲突时允许本地提交。提交成功后 SHALL 显示返回的版本身份并刷新状态和历史；提交 MUST 不隐含推送、全量暂存或丢弃未选择的修改。

#### Scenario: Commit a subset of changed files
- **WHEN** A 和 B 均有修改，用户只暂存 A 并输入非空说明后提交
- **THEN** 新版本包含 A 的提交内容，B 的本地修改保留，且客户端没有发起远端推送

#### Scenario: Reject an invalid commit request
- **WHEN** 提交说明只有空白、没有可提交的暂存变更，或仍存在未解决冲突
- **THEN** 客户端禁止提交并给出具体原因

### Requirement: Preserve intent when commit completion is uncertain

客户端 SHALL 在提交失败或结果不确定时保留说明草稿并重新查询版本和暂存状态。只有后端确认提交成功后才能清空已完成提交的草稿；结果未核实时 MUST 不自动重试提交。

#### Scenario: Commit reports an error
- **WHEN** 提交失败或客户端未收到可确认的完成结果
- **THEN** 界面显示失败或待核实状态，保留说明并核对仓库，避免用户因自动重试产生重复提交

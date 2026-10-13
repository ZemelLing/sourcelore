# Spec Delta

## Purpose

让程序员按分支查看和逐步加载 Lore 版本历史，通过版本身份、提交说明及受影响文件追溯代码变化，同时明确区分已确认的远端状态与尚未核实的本地信息。

## ADDED Requirements

### Requirement: Load branch history incrementally

客户端 SHALL 按所选分支提供有界分页历史，显示版本身份、提交说明、时间及后端提供的作者信息，并可继续加载较早记录。一次浏览会话内的页面 SHALL 使用一致的历史起点，避免新提交插入导致重复或遗漏。

#### Scenario: Browse an existing branch
- **WHEN** 用户打开存在较多提交的分支历史
- **THEN** 客户端先呈现首批结果，允许继续加载，而不要求先取回全部历史

#### Scenario: A new revision appears during pagination
- **WHEN** 用户正在加载较早历史时发现新的分支最新版本
- **THEN** 客户端提示刷新；刷新前维持当前分页起点，刷新后重新建立一致列表

### Requirement: Show revision details and changed files

客户端 SHALL 在选择提交后展示完整说明、版本身份、父版本及受影响文件列表，并允许进入文件差异。缺失的作者等可选元数据 SHALL 显示为空或未知，不推测其值。

#### Scenario: Select a historical revision
- **WHEN** 用户选择一个历史版本
- **THEN** 客户端显示该版本详情，选择其中的文本文件后可查看对应历史差异

### Requirement: Distinguish local revisions from confirmed remote state

客户端 SHALL 使用后端提供的本地版本与最后成功核实的远端状态显示未推送信息，并标明远端状态是否过期或未知。不能访问远端时 MUST 不把未知数量显示为零，也不将本地提交误报为已推送。

#### Scenario: Commit locally while offline
- **WHEN** 用户离线提交且远端状态无法重新核实
- **THEN** 新提交出现在本地历史，远端状态显示为基于最后核实信息或未知

#### Scenario: Confirm a successful push
- **WHEN** 推送成功且后端确认目标远端已包含对应提交
- **THEN** 客户端刷新其推送状态，不再将这些提交标记为待推送

### Requirement: Explain unavailable historical data

客户端 SHALL 在离线、权限不足或历史内容尚未缓存时报告具体缺失范围，并保留已加载的列表和选择。访问失败 MUST 不被表示为空历史或空文件差异。

#### Scenario: Uncached revision content is requested offline
- **WHEN** 用户离线请求未缓存的历史文件内容
- **THEN** 客户端显示内容当前不可用并提供重试入口，已加载的历史列表保持可用

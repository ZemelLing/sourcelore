# LoreVcs 0.10.0 实施核对

2026-10-13，在 Windows x64 上通过真实 SDK 集成测试观察到以下行为。
原生库报告 `0.10.0+1172-lore_v0.10.0__urc_main`，来自锁定的 `LoreVcs.runtime.win-x64/0.10.0`。

## 已验证

- `RepositoryInfo` 的本地元数据读取需要 `LoreGlobalArgs.Local=true`。仅 `Offline=true` 仍会选择远端信息路径；没有远端的仓库返回错误 111。
- SDK 的 `RepositoryPath` 需要根目录。客户端先向上寻找 `.lore` / 旧版 `.urc` 目录标记，再调用 SDK 验证，并用 `RepositoryInstanceList` 返回的实例身份和根路径作为会话身份。不解析内部存储格式。
- `RepositoryStatus(Staged=true, Scan=true, CheckDirty=true)` 在新增、修改、删除样本中返回真实路径和独立标志。
- `FileDiff` 在中文文本样本中返回基线与工作文件的 patch。
- `FileStage` 暂存的是路径意图，暂存后再次编辑，`RevisionCommit` 提交最新内容；未暂存文件的修改保留。
- `FileUnstage` 保留文件内容，空暂存提交返回失败。
- 参数对象保留到 `WaitAsync` 返回，事件中的字符串和身份数据在回调中复制。运行库记录活动调用，禁止仍有调用时关闭。

测试入口：`NativeRuntimeTests.WorkingCopyUsesNativeStagingIntentAndPreservesUnselectedEdits`。
这些是 JIT 集成测试证据；尚未用它们替代任务 3 的真实 NativeAOT 仓库工作流验证。

## 已确认的分支范围调整

`NativeRuntimeTests.RecordPinnedSdkBranchCreationBehavior` 实测输出：

```text
Lore 0.10.0+1172-lore_v0.10.0__urc_main: BranchCreate changed current branch from main to feature-probe; revision unchanged=True
```

已安装程序集中的 `LoreBranchCreateArgs` 只有 `Branch`、`Category`、`Id`，没有起始版本或“仅创建”选项。
`LoreBranchSwitchArgs` 则有 `Branch`、`Revision`、`Reset`、`Bare`；它不改变创建 API 的语义。
用户于 2026-10-13 确认首版改为“仅从当前版本创建并切换”，历史版本创建留待后续扩展。proposal、design、branch-management spec 和任务 8.2/8.4 已同步。不能在 UI 中暗示创建不改变当前分支，也不能用未经验证的临时切换/切回掩盖副作用。

无需为此维护原生 API 扩展；能力映射与创建/切换的真实 SDK 和 AOT 检查均已完成。

## 后续集成确认

- `RepositoryConfigGet(remote_url)` 返回远端端点；仓库与分支身份从状态事件复制，不猜测克隆路径。
- `FileInfo(Revision=...)` 仍会报告只存在工作目录的新增路径，须排除 `FlagAdded`，不能把它当作版本文件。
- 默认传输只保留状态/高优先级片段；克隆、同步与明确联网读取设置 `Cache=true`，实际 AOT
  克隆后编辑可离线比较。历史缺失仍明确报错，不保证永不淘汰缓存。
- `RevisionSync` 指定 Revision 会走历史检出，代码中 location 仍为 Local，不更新远端同步锚点；
  即使文件更新也可能阻止后续提交。客户端正常同步不传 Revision，使用默认分支同步、
  Reset=false / ForwardChanges=false，预检全部物理未跟踪/忽略文件以保护可能后来前进的目标。
- 已提交合并的 `RevisionMerged` 可能保留第二父版本；零暂存 hash 或暂存版本等于当前版本
  不表示未完成合并。判定结合 `RevisionStaged` 和真实冲突标志，合并完成后分支操作已实测。

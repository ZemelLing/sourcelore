# 固定 SDK 能力映射

基于 NuGet LoreVcs 0.10.0 随包 XML/程序集，以及配套原生库
`0.10.0+1172-lore_v0.10.0__urc_main`。接口签名由
`tests/SourceLore.Lore.IntegrationTests/SdkApiCompileContract.cs` 在每次构建时验证。
该编译契约不执行远端操作，也不代替后续真实认证、分页和冲突验收。

| 能力 | SDK 入口与关键参数 | 使用的事件/结果 | 性质及限制 |
| --- | --- | --- | --- |
| 根与实例 | RepositoryInfo；RepositoryInstanceList | REPOSITORY_DATA、REPOSITORY_INSTANCE | Local=true 读取本地信息；SDK 要求根路径，先查目录标记后由 SDK 验证 |
| 目标与远端锚点 | RepositoryConfigGet(Key=remote_url)；RepositoryStatus(RevisionOnly=true，联网) | REPOSITORY_CONFIG_GET；REPOSITORY_STATUS_REVISION 的 Repository/Branch/RevisionLocal/RevisionRemote/授权标志 | 只读取指定公开配置键，不解析内部文件；缓存更新仍串行调度，数量未知不能用版本号相减 |
| 状态/扫描 | RepositoryStatus(Staged, Scan, CheckDirty, SyncPoint, Paths) | REPOSITORY_STATUS_REVISION、REPOSITORY_STATUS_FILE、ERROR | 扫描/check-dirty 持久化 dirty 状态，按写操作调度；非致命 ERROR 也不能当作完整干净状态 |
| watcher 线索 | FileDirty(Paths) | COMPLETE/END；之后核对状态 | 元数据写；路径不是内容快照 |
| 暂存/取消 | FileStage(Paths, Scan)；FileUnstage(Paths) | FILE_STAGE_*、FILE_UNSTAGE_*；实际 status | 元数据写；显式文件路径，取消不恢复内容 |
| 提交 | RevisionCommit(Message) | REVISION_COMMIT_REVISION、COMPLETE/END | 文件/版本写；读取已暂存路径的最新内容；不调用 push |
| 文本/历史内容 | FileInfo(Paths, Revision)；FileDiff(Paths, SourceRevision, TargetRevision, ContextLines)；FileWrite(Path, Revision, Output) | FILE_INFO、FILE_DIFF、FILE_WRITE | FileWrite 将版本内容导出到受控临时文件，不能以工作文件作 Output；读取/网络缓存仍统一调度 |
| 历史与详情 | RevisionHistory(Revision, Branch, Length, Date, OnlyBranch)；RevisionInfo(Revision, Delta, Metadata)；RevisionDiff | REVISION_HISTORY_ENTRY、REVISION_INFO、REVISION_INFO_DELTA、METADATA、REVISION_DIFF_FILE | Length=0 无界，禁止用于 UI；Date 是停止条件，不是分页 cursor；固定 tip 与 DAG continuation 由适配层维护 |
| 登录 | AuthLoginInteractive(RemoteUrl, NoBrowser) | AUTH_URL、AUTH_USER_INFO、AUTH_IDENTITY、COMPLETE/END | 凭据状态写；NoBrowser=true 由 UI 提供入口；参数没有 CancellationToken，UI 取消不能提前释放原生调用 |
| 克隆 | RepositoryClone(RepositoryUrl, Revision, View, Bare, DirectFileWrite) | REPOSITORY_CLONE_BEGIN/PROGRESS/END | 文件/存储写；目标必须不存在或为空；错误后不自动删除残留 |
| 推送 | BranchPush(Branch, FastForwardMerge=false) | BRANCH_PUSH_*、COMPLETE/END | 远端写；默认关闭额外快进合并优化，失败先核对再明确重试 |
| 同步 | RevisionSync(Revision留空, ForwardChanges=false, Reset=false) | REVISION_SYNC_TARGET、REVISION_SYNC_FILE、REVISION_SYNC_REVISION | 本地文件/元数据写；按正常分支语义同步，不能以指定哈希的历史检出替代；传输启用 Cache，已由双目录 fixture 验证 |
| 冲突 | RepositoryStatus 的 Conflict/Unresolved；BranchMergeStart(Branch, NoCommit)、BranchMergeResolve(Paths)、BranchMergeRestart(Paths)、BranchMergeAbort | BRANCH_MERGE_CONFLICT_FILE、BRANCH_MERGE_RESOLVE_*、BRANCH_MERGE_ABORT_* | 元数据/文件写；继续是否需要 RevisionCommit、abort 的数据影响以任务 7 实测为准，不提前承诺安全硬取消 |
| 分支 | BranchList；BranchInfo；BranchCreate(Branch, Category, Id)；BranchSwitch(Branch, Revision, Reset=false) | BRANCH_LIST_ENTRY、BRANCH_INFO、BRANCH_CREATE、BRANCH_SWITCH_* | create 会立即改变当前分支且没有历史起点参数；用户已确认仅从当前版本创建并切换；switch 使用预检与保留策略 |

全部调用使用独立的 disposable 参数对象，保持至 WaitAsync 真正返回（END）。
不使用持续订阅接口的完成语义来推断普通调用；事件中需要保留的字符串、数组、hash 和数字必须在回调中复制，Core/VM 不保存 FFI 或 SDK wrapper。

本地真实状态、差异、stage/unstage/commit 和 branch-create 行为已实测；详见 [sdk-findings.md](sdk-findings.md)。
未验证的在线行为仍保留在各自任务中；API 存在和可编译不等于完整功能已交付。

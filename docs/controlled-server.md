# 受控 Lore 服务器测试

服务器仅供集成测试，不随客户端发行。使用官方 v0.10.0 的 Windows x64 压缩包：
[loreserver v0.10.0](https://github.com/EpicGames/lore/releases/download/v0.10.0/loreserver-v0.10.0-x86_64-pc-windows-msvc.zip)。
本次下载的 SHA-256 为 `E137BB4CFAA1E40D26C8421B22AC26EE95C925B13E044299778BAEF85FD74DFA`，
二进制 `--version` 返回 `loreserver 0.10.0`。

下载并解压到 `artifacts/research/loreserver-v0.10.0` 后运行：

```powershell
$env:SOURCELORE_SERVER_EXE = (Resolve-Path artifacts/research/loreserver-v0.10.0/loreserver.exe).Path
dotnet test --project tests/SourceLore.Lore.IntegrationTests/SourceLore.Lore.IntegrationTests.csproj --no-restore
```

ControlledServer fixture 为每次测试生成独立临时根，分配端口，所有网络端点绑定
`127.0.0.1`。存储和证书临时目录均位于测试根；清除子进程继承的 LORE__ 配置，
通过 HTTP 健康检查等待启动。只停止自己启动的服务进程，等待退出并验证清理路径后删除测试目录。
认证关闭，不使用已有仓库、共享存储、凭据或全局配置。客户端操作全部使用 SDK，无 Lore CLI。

已实测：创建远端仓库、本地提交/推送、第二独立实例克隆、远端更新同步、非空目标拒绝、
克隆不存在仓库后保留残留路径、同文件独立提交产生冲突、中止、重新同步、人工解决后提交并推送。
两次分歧同步均正常返回，但状态存在未解决冲突；因此成功返回不等于合并完成。
事件包括 `REVISION_SYNC_TARGET`、`BRANCH_MERGE_START_BEGIN/END`、
`BRANCH_MERGE_CONFLICT_FILE`、`REVISION_SYNC_REVISION`、`COMPLETE` 和 `END`。
中止回到同步前 B 的本地版本及内容，未删除 B 的本地提交；解决路径后仍需提交合并版本才能推送。

固定 SDK 的新克隆不会保存仓库描述元数据。客户端遇到本地 RepositoryInfo 的
Address not found（80）时，通过 RepositoryInstanceList 识别当前根与实例，
随后由状态查询核实工作目录。名称暂用目录名，远端地址未知时不推测；不会改读内部存储。
推送/同步目标通过公开 RepositoryConfigGet(remote_url) 和状态事件取得远端端点、仓库与
分支身份；配置返回端点，不包含原克隆路径，因此显示实际仓库 UUID，不能拼出猜测地址。

新增生产适配检查已通过：准备目标不写入、远端前进推送拒绝、忽略物理文件阻止同步并
保留本地未推送提交、未解决时拒绝继续、中止前原始字节备份、合并提交后创建/切换分支。
已提交双父版本不能误报未完成合并。克隆/同步启用片段缓存，并在 AOT 实际克隆后编辑
及比较中验证；完整 AOT 双克隆流程见 [验证记录](validation.md)。

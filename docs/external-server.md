# 外部测试服务器连接记录

2026-10-13，用户提供 `lore.inkarrays.com:41339`，并明确授权创建测试仓库、
提交/推送两行测试文本及克隆验证。

已核实：

- HTTP `http://lore.inkarrays.com:41339/health_check` 返回 200。
- TCP 41337 可达。该端口的 TLS 证书通过 Windows 和 Python 默认信任校验，
  Python 握手协商 TLS 1.3、ALPN `h2`，服务器提供四张证书。没有关闭证书校验。
- Windows HTTP/2 请求返回 `application/grpc`。
- 通过 HTTP/2 发起只读
  `/lore.environment.v1.EnvironmentService/EnvironmentGet`，HTTP 200、
  `grpc-status: 0`，响应体 7 字节。不记录响应中的地址或认证信息。

固定 LoreVcs / 原生库 0.10.0 的实际连接结果：

- `lore://lore.inkarrays.com:41337/sourcelore-test-20261013-7f942ca1` 在读取环境配置时
  返回 28 / Disconnected。
- 核实该入口使用 TLS 后改用
  `lores://lore.inkarrays.com:41337/sourcelore-test-20261013-7f942ca1`，仍在建立 gRPC
  连接阶段返回 28 / Disconnected，细节为 `transport error`。

两次均未进入远程仓库创建阶段；没有提交、推送或克隆成功，不以健康检查或独立
HTTP/2 成功替代 SDK 验证。测试目录保留在 `artifacts/research/external-server/`，
工具和脱敏报告位于 `artifacts/research/RemoteProbe/` 及 `external-server/latest-result.txt`。
暂不自动重试创建，不修改服务端或系统信任配置。

下一步需核对服务器版本、41337 的 TLS/gRPC 配置及该时段服务端连接错误，
以区别服务配置与固定 SDK 的传输兼容问题。当前证据不足以确定根因。

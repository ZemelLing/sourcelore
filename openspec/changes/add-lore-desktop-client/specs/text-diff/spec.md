# Spec Delta

## Purpose

帮助程序员在提交前和检查历史时理解文本变化，明确每次比较的文件与版本来源，并在非文本、无法解码或超出预览限制的情况下保持界面可用和原始文件内容不变。

## ADDED Requirements

### Requirement: Identify comparison sources

客户端 SHALL 支持工作文件对当前版本、历史版本对所选父版本以及用户指定两个历史版本的同路径文件比较，并清楚显示路径与两侧版本身份。初始历史详情 SHALL 默认采用后端指定的主父版本，并允许选择其他父版本。

#### Scenario: Compare the working file
- **WHEN** 用户选择工作区中的已修改文本文件
- **THEN** 界面显示当前版本内容与最新读取的工作文件内容之间的差异，并明确标识工作文件一侧

#### Scenario: Inspect a merge revision
- **WHEN** 用户查看存在两个父版本的历史提交并切换比较父版本
- **THEN** 差异和版本标签均切换到所选父版本，不能继续显示另一父版本的比较结果

#### Scenario: Compare an added or deleted file
- **WHEN** 文件只存在于比较的一侧，或者提交没有父版本
- **THEN** 缺失的一侧按空内容比较，界面明确显示新增或删除状态

### Requirement: Provide consistent read-only diff modes

客户端 SHALL 支持统一和并排两种只读文本差异视图，展示行号、增加和删除行，并保持两种视图使用相同的比较来源及差异结果。切换视图和复制文本 MUST 不修改文件、编码或换行符。

#### Scenario: Switch diff presentation
- **WHEN** 用户在同一比较中切换统一与并排模式
- **THEN** 两种视图表达相同改动，原始文件字节内容不变

### Requirement: Handle encoding and preview limits explicitly

客户端 SHALL 正确预览 UTF-8 和带 BOM 的 UTF-16 文本，识别换行方式与末尾换行差异。非文本、未知编码、无法解码或超出已公布预览限制的文件 SHALL 显示原因和可用的外部打开入口，不以乱码或截断结果冒充完整差异。

#### Scenario: Compare Unicode text with line-ending changes
- **WHEN** 用户比较包含中文、CRLF/LF 差异或末尾换行变化的受支持文本
- **THEN** 字符正确显示，并标识相关换行变化，不自动规范化文件内容

#### Scenario: Decline an unsupported preview
- **WHEN** 文件为二进制、无法可靠解码或超过预览大小/行数限制
- **THEN** 客户端解释无法内置预览的原因并保持可交互，不尝试资产专用预览或无界加载

### Requirement: Discard obsolete comparisons

客户端 SHALL 在文件、工作目录或比较版本变化后使旧查询结果失效。读取工作文件期间检测到内容再次变化时 SHALL 将结果标记为过期或重新读取，不把旧结果绑定到新的选择。

#### Scenario: Quickly switch selected files
- **WHEN** A 的差异尚未完成时用户选择 B，而 A 的结果晚于 B 返回
- **THEN** 界面仍显示 B 的差异和标签，A 的结果不覆盖当前选择

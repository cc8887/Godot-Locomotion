# 相对路径修复与历史清理

当前源码、README 和历史验证文档中的固定盘符路径已改为相对仓库路径。Lyra 的 PowerShell 与 Python 工具通过共享路径解析器定位 UE 工程、引擎、引擎源码及 Rig 对照工程；默认目录和环境变量见 README。调用者的工作目录不参与默认依赖定位，本机覆盖保存在被忽略的配置中。

验证：114 个 Python 文件通过 AST 解析，29 个 PowerShell 文件通过语法解析；Python 三项和 PowerShell 三项路径测试通过，覆盖仓库外调用、含空格的相对配置及绝对本机覆盖。Windows 临时目录的短文件名会被 Path.resolve 展开，测试已按规范化路径比较。

从仓库外目录运行 Godot 4.7.2 .NET 的步枪 Direction 60Hz 两项测试（短起步及稳定前进后向右换向），均通过，日志没有 Godot 错误或警告。动画实现未变更，资源 JSON 与依赖哈希未格式化。

按用户明确授权改写公开历史及已发布 tag。原历史完整 Git bundle、提交映射与 Release 原始元数据保存在被忽略的 `artifacts/path-portability/`，原始资产字节不变。公开资源清单、校验和与 Release 的源码提交号随 tag 同步更新。用户已有的 `p4_locomotion_demo.tscn` 修改保留，不纳入本批提交。

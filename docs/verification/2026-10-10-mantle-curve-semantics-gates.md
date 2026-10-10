# Mantle 曲线用途与能力门禁（2026-10-10）

按用户要求，将“避障前移属于时间驱动控制，仅高度入口使用距离匹配，尚未接入持续距离驱动播放或 Root Motion Warping”的判断落实为导出数据与发布检查。稳定字段和算法见[控制契约](../architecture/als-mantle-contact-control.md#distance-matching-与-motion-warping-的导出判断)。

## 实现范围

- 新增 `tools/kimodo_pipeline/mantle_curve_contract.py`：独立对照原始 Root 输入，验证用途／能力声明、修正前距离和实际平台段，不通过前移单调性认证完整匹配能力。
- `mantle_contacts.py` 在每个关键帧保留 `rootAdvanceCm`，导出 `curveContract`／`curveAnalysis`，烘焙完成即检查。既有前移、抬升、接触权重与锚点计算保持。
- `publish.py` 写入包的 `mantleCurveContract`，stage 写包前与正式启用均检查对应载荷摘要、最终输入身份、用途和平台分析；正式启用继续保留完整质量／几何重烘焙门禁。
- `full_pipeline.py` 在启动构建／Godot 前检查，成功记录 `mantle-curve-contract.json`；失败保存 `runtime-result.json` 并拒绝继续运行，不冒充运行验收成功。

声明不是消费项目能力自动握手。当前 C# Import 能读取追加字段，但未新增对这些元数据的解析门禁；Pipeline 对其执行校验。旧活动包继续正常使用原合同，新完整 Pipeline 会拒绝没有新声明的旧包，要求构建新版本，不自动覆盖旧资产。

## 验证与证据

证据目录：[mantle-curve-contract-20261010-v1](../../artifacts/kimodo_als/mantle-curve-contract-20261010-v1/)。

Python 相关四模块最终 **49/49**：曲线语义、实际接触烘焙、离线转换编排和依赖图／缓存。覆盖平台段继续抬升、严格递增仍不认证未实现功能、虚假持续匹配／Warping 声明拒绝、距离替换／缺失／重采样／接触损坏／旧分析拒绝、manifest／输入绑定及运行前保留失败。日志：[python-final-tests.log](../../artifacts/kimodo_als/mantle-curve-contract-20261010-v1/python-final-tests.log)。

首次单测发现 Windows 临时目录短名与解析后的长名不一致，修正受控夹具的路径解析。首次四模块运行 48/49：旧运行编排测试只有模拟 pack，没有新合同，因新增前置门禁拒绝；该测试已单独模拟通过语义前置检查，保留原“静态蒙皮失败仍执行平台矩阵、整体运行失败”断言，并新增前置检查已执行的断言。真实语义门禁由独立正／负夹具覆盖。首次失败日志保留为 `python-tests.log`，未跳过门禁或削弱几何标准。

`probe.py` 读取当前活动 v5 包，将两份最终输入按原字节复制到独立 `control-probe/config`，重烘焙两次并验证确定性。三个 sequence 均记录前移平台段，包／资源合同检查通过；去除新增元数据和原始距离字段后，控制文件与活动包原控制值完全相同。活动指针、包、姿态／Root／控制输入五份文件的 SHA256 前后完全保持。结果：[probe-result.json](../../artifacts/kimodo_als/mantle-curve-contract-20261010-v1/probe-result.json)。这是控制资源诊断探针，不是完整可运行替换包。

Release .NET 探针通过真实 `AlsMantlingContactCompiler` 读取新文件，对照活动包旧文件，三个 sequence、**173 个关键帧**的进度／双手接触／锚点采样一致，五种高度的入口匹配保持。结果：[import-probe-result.json](../../artifacts/kimodo_als/mantle-curve-contract-20261010-v1/import-probe-result.json)，日志：[import-probe-final.log](../../artifacts/kimodo_als/mantle-curve-contract-20261010-v1/import-probe-final.log)。首次探针项目引用多写一级父目录导致构建失败，已修正，失败日志 `import-probe.log` 保留；最终运行 0 退出。

## 提交前检查

用户随后要求 commit。由于专用 Mantle 烘焙／手部修复及 Core／Import／Godot 控制仍未提交，本次按 60 文件清单纳入这些必要依赖、测试、诊断脚本和对应文档；共享 `full_pipeline.py`、`ROADMAP.md`、文档导航仅暂存 Mantle 内容。Viewer、资源扫描／运行用例选择、Unreal exporter、活动指针、生成资产、二进制及测试输出继续留在工作区或 ignored 目录。清单见本地证据 `commit-scope.json`。

通过从 Git index 读取 Python 源码、将文件身份仍映射到主目录的方式检查实际提交内容，不创建项目副本或 worktree。首次暂存源码回归发现新增语义门禁抢先改变了“缺少完整逐资产质量证明”的错误优先级；已将完整质量范围检查恢复到语义门禁之前，保留原断言和全部门禁。另一失败来自诊断加载器在 subprocess mock 生效后尝试读取 Git；修正为测试前预读 index 源码。首次失败证据保留为 `staged-python-tests.log`，最终七模块 **81/81**，暂存文档没有缺失的应跟踪目标，见 `staged-python-final-tests.log`／`staged-validation.json`；未依赖工作区未暂存的 Python 修改。

Release Core Mantling **46/46**、Import Motion／Contact **9/9**，实际 Godot 项目 Optimize 构建零警告／错误。独立日志为 `commit-core-tests.log`、`commit-import-tests.log` 和 `commit-optimize-build.log`。提交准备未重跑游戏矩阵或生成资产。

## 覆盖边界

文档七份、340 个相对链接目标检查通过；新增判断章节锚点与 `git diff --check` 通过。最终再次核对活动指针／包／三份输入摘要保持。

本次曲线语义门禁没有修改角色移动／姿态算法，没有实际 Kimodo 扩散、新 FBX 导出／回读或 Godot 游戏矩阵，不扩大[既有中高攀爬验收](2026-10-10-medium-high-mantle.md)。没有实现持续距离驱动播放、warp 窗口／目标运行修正器或任意障碍避障。活动资产与原生几何阈值保持；提交范围见上节，没有推送。

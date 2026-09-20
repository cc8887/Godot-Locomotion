# Falling Lean 专属采样与空中输入历史

日期：2026-09-12。完整性修复第七十五批，分支 `feature/p5a-events-actions`。

## 实际实现

- 导出器增加 `-FallingLean` 路径，读取 `ALS_N_Lean_Falling` 的原生网格、
  过滤、五个样本、附加基准与原生采样轨迹。仓库和 UE 项目插件源码一致。
- 新正式数据 `assets/config/v4_falling_lean_sampling.json`：2×2 分格、9 个
  网格点、27 个顶点项，五个局部空间附加样本，以 `ALS_N_FallLoop` 第零帧
  为基准。地面 Lean 的 4×4 分格和 `ALS_N_Run_BasePose` 保持独立。
- `AlsLeanSamplingCompiler.CompileFalling` 按 compiled node 260/282 绑定
  Fall/Jump；播放器 67/68、样本起点 93/98。相同资产不合并播放身份。
  校验轴、网格顺序、来源、样本速率、附加基准、通知及不支持的插值/逐骨配置。
- `AlsLeanBlendSpace` 支持两种网格，保留原生角点遍历、平坦索引边界、
  等权样本排序和阈值剔除。归一化改为单精度倒数乘法以匹配实际 UE 输出。
- `AlsAirPoseInputs` 独立保存 Fall/Jump 节点输入历史。预测进入/退出使用
  20/5；Flail、Fast 使用 5/5；先映射/插值，保留未截断历史，最后限制权重。
  全权预测分支不更新下落/Lean，全权 Flail 不更新内层 Fast。Initialize
  重置插值初始化标志和 Lean 采样缓存标志，保留节点已捕获的输出。
- `AlsLeanPoseSampler` 按每个动画的曲线 ID 查询，新增保留存在性的曲线
  合成入口；旧数值入口继续可用。Falling 的五样本及基准在当前导入定义中
  没有自带浮点曲线；外层图写入的 BasePose_N/Weight_InAir 不由 Lean 伪造。

## 本轮验证

| 检查 | 结果 |
| --- | --- |
| 新空中输入专项 | Debug/Release 各 8/8 |
| 新 Falling Lean 导入专项 | 20/20 |
| Grounded/Falling Lean 合并专项 | Debug/Release 各 43/43 |
| Core 常规 | 1976/1976，沿用排除 AlsP5aGoldenTests、AlsP5aTraceSchemaTests 的入口 |
| Import 全套 | 1269/1269 |
| Godot 优化构建 | 零警告、零错误 |
| 原生权重对照 | 441 静态点、30/60/120 Hz 共 840 连续帧，两种身份均逐 float、顺序一致 |
| Falling Lean 真实资源 | 2562 组输入回放、26 组单样本原动画重建、7686 次缺失曲线检查、重复姿势相同 |
| Grounded 回归 | Crouching Cycles 1248 帧及初始化测试、Standing/Detail/Pivot/Sprint、Main 六缓存 1680 帧通过 |
| Worker | single/parallel 各 180 帧一致；每模式 10 事件；parallel 晚期来源事件与整帧回滚通过 |

Worker 结果 `21E164D829153157`、完整姿势 `CF9225D4DE9B2C8B` 保持。
TRX 位于 `artifacts/test-results/falling-lean/`；Godot 最终日志
`artifacts/falling-falling_lean_smoke.log` 及 `artifacts/falling-*.log`。

Godot 新测试使用已导入的真实动画、独立给定的合法来源秒数及实际 FallLoop
基准，并未接入主移动真实时钟或角色输入。26 个端点以绝对导入动画独立重建，
其余帧验证取样及同输入重试一致，不称为完整 UE 骨骼图逐帧等价。
7686 次曲线检查覆盖缺失名称；另外检查已有零/非零基值保留，不能将这些
计数描述为 7686 条原生动画曲线或外层 ModifyCurve 验证。新 smoke 未声明零分配。

## UE 构建与导出验证

使用 `ue-diagnosing-plugin-build-load` 完整 Editor target 构建/审计。
本次复核日志前缀：
`D:/AdvancedLocomotionSystemV/Saved/Logs/PluginBuild/20260911T183853534Z-e2256f1fc34440fa84f213afef5255f4`。
三个适用项目插件审计通过，状态指纹
`B83A829F602E00D04370E67742574066B159844D2052B20B5002D635064E1760`。
引擎版本 5.9，BuildId `015ca4ed-618b-4c74-9b03-a4854c06ae7b`。

- 冷启动重复 Falling 导出退出零，零错误/警告；与正式数据 SHA256 一致：
  `8B779C030F7E280A8F6DFBEE30969303A0FFDED0CBEEA0C5B7CA5E7AF3E3E651`。
- Grounded Lean 重导出退出零，输出与旧正式文件字节一致，SHA256：
  `82A3DE78BA6B29B63917556C20C99C497073F5EAABAB1C1DAC2756DFE41D0241`。
- DataValidation 退出零，零错误、三条既有 AI/导航资产警告。
- 独立 BuildPlugin 验证包成功：
  `artifacts/unreal/AlsFallingLeanValidation-20260912`。未部署包内 DLL。
- 普通 `UnrealEditor.exe` 重启、只读脚本导出与退出成功，退出码 0；
  `ALS_YAW_INPUTS_OK curves=2 samples=2882 graphs=3 assets_saved=0`。
  仍有两条既有 `LogAutomationTest: Error: Condition failed`，不称无错误启动。
- 插件源码两处 SHA256 均为
  `61BA085DF6C0D2C6DFACE2A58F8AC09B6320E76204BB8010E28116CA62AAD54A`。

日志前缀 `artifacts/ue-falling-`，重复 JSON 位于 `artifacts/falling-lean-repeat.json`。
UE 导出、DataValidation、独立打包与普通 Editor 顺序执行，没有并行启动 UBT。
保留早先系统 .NET 缺 10.0 的启动失败记录，使用引擎自带运行时完成构建，
没有修改系统 .NET、BuildId 或复制单个 DLL 来绕过审计。

## 首错与修正

- 原生权重精确测试首次 40/43，通过静态点而在三种帧率下连续输入失败：
  期望 `0.440000057`，实际 `0.440000027`。核对 UE 网格/累积/归一化源码，
  并比较单精度除法与倒数乘法；后者匹配现有原生导出。未放宽该精确断言，
  最终两种构建配置均通过。此结论限于当前引擎输出和已测输入。
- 新 Godot 覆盖检查误以为 Falling 资产包含浮点曲线，先后保留初始和带计数
  诊断日志。核对实际定义后，明确检查当前六个资产无曲线，验证外层拥有的
  曲线名称在 Lean 内保持缺失，并保留调用者已有值。骨骼回放当时已通过。
- Worker 命令误传不支持的 `--als-failure-policy=none`，在初始化拒绝；正常
  路径改为不传该参数，故障路径使用正式 `late_source_event`。没有改测试器
  接受非法参数，最终双模式和回滚通过；首错日志保留。
- 早先空中输入测试误把 -4 m/s 的 Fast 映射期望为 0，按原映射修正为 0.4，
  最终 Debug/Release 通过；没有修改实际速度映射。

## 仍未完成

空中输入目前是可复用组件，尚未用严格外层图编译合同替代其固定映射常量，
也未与整个 Fall/父级 Jump 姿势、来源初始化/相关性和主状态转换联测。
下一批从这些外层连接继续，随后统一 Main Movement 的候选所有者、真实
Grounded 缓存、Slot/Montage、最终惯性化与 Demo。不要重复导出已有 Lean 网格。

最终 YawOffset 到角色朝向、动态 Layering/Add/LS、完整脚部/pelvis 和平台
反馈仍须推进。基础视觉修复不推迟到 P5C/P6；原 P5A–P7 全范围、Overlay
和道具均保留，音频暂缓。没有新人工移动截图、完整 UE 图骨骼轨迹、完整 P4
聚合脚本或十分钟性能采样，不关闭滑步、交错步、上身或平台脚锁验收。
未提交、回滚、合并分支或改动用户已确认的键鼠操作。

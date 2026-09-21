# 真实接触历史匹配与回存对照

本批在 `.` / `main` 推进。上一批已修复 Gather 的.NET9叉积舍入。本批补齐真实历史边界，未修改匹配、求解或休眠公式，普通demo未切换。

## 捕获与原生重放

WorldContacts提供只读pending诊断，覆盖已准备的全部形状对，包括接触消失后“当前零点、上一帧仍有锚点”的对子。旧solver-order接口只包含活跃对子，会漏掉清空历史的一帧。新增接口保留shape key、epoch、匹配设置、旧saved、所有检测点（含禁用点）、实际assigned与最终solver摩擦比/initialPhi。

快照在StageCommit之前完成，`historyResults`表示待提交的实际求解结果，不代表整步已发布。验证通过下一帧捕获的saved检查实际发布，不把当前临时结果当提交证明。正常热路径仅保存匹配设置值类型，诊断数组只在选中帧分配；现有零分配测试通过。

采集51份快照：

- 高速120Hz两模型6–8、28–33帧，及AnimMan1140–1142帧。
- 普通60Hz两模型6–8、24–29帧，及Mannequin540–542帧。
- 平移平台30Hz两模型6–8帧，及仍未休眠的AnimMan297–299帧。

确认包含真实首次地面接触：高速AnimMan30帧、Mannequin31帧，普通两模型25帧开始出现新的环境接触对。总计573对、1007点、57个无旧锚点的新点、24个滑动摩擦回存点、3次空流形。410组同identity/相邻epoch可直接核对上一帧计算的saved是否成为本帧真实输入。

新增 `PhysicsActualHistoryInputs=` / `PhysicsActualHistoryOutput=`：UE以公开SetSolverResults初始化旧锚点，执行原生Activate/AssignSavedManifoldPoints，再以捕获的真实摩擦比和更新后initialPhi执行原生SetSolverResults。替身sphere/box仅选择quadratic匹配策略，不运行窄相。旧参考导出不变。

新参考 `assets/config/v4_physics_actual_history_reference.json`：2,307,188 bytes，重复冷导出均退出0且字节相同，SHA256：

`1290AF30E877DEA0E7CF8F58EDD0677C273F55A641FE114B46211DFB1055C9B4`

旧快照缺少history边界时明确拒绝，进程退出1且不生成输出。

## 结果与边界

.NET8.0.28与.NET9.0.17都得到：573对/51帧/1007点/410相邻状态/3空流形/57新点/24滑动点，最大锚点误差0；initialPhi、hasAnchor、initialContact、savedIndex及回存点数量也一致。当前Core、原生匹配输出和实际Godot捕获三者对齐。旧192组、1152帧人工历史参考在两运行时同样误差0。

这排除了受检帧的锚点匹配、摩擦比回存以及相邻帧发布差异，不能证明所有输入或整个UE物理世界等价。原生重放仍采用捕获的“历史是否可用”判断与求解结果作为输入，没有独立重建UE世界中的身份分配、midphase激活、岛划分或完整动力学轨迹。

- Godot优化构建0错误/0警告。
- Core历史/世界/流形/持久对子相关30项通过，含新增空帧清空、abort保留、pending访问约束和已有零分配测试。
- Import新旧history、raw Gather、contact与coupled相关10项在.NET8/.NET9各通过。
- Godot60Hz完整contact smoke通过，包括369个raw Gather和246个胶囊实际输入回放。
- 三类实际采集场景仍失败，休眠/落地/限位诊断分别23、4、1行与上一批逐行一致。本批未重跑全部十二项矩阵或Core/Import全量；最新完整基线仍2835/2424+1skip/8of12，不能把定向30/10称为全量。
- UE完整Editor构建/插件审计通过，fingerprint `FEF8E71818F2A72F4771CFC1CDA69DA2376E41FC4F2A88F8A8188D66A05F732B`；DataValidation退出0，0错误/3既有警告。未发现额外打包要求。
- 普通Editor PID16452加载标记成功，本次原生退出0且DLL释放；两个既有Condition failed仍在，过去间歇0xC0000005未修复。UE项目镜像与主仓库本批三个导出器文件hash相同。

所有日志、快照和重复参考位于 `artifacts/physics-actual-history-20260922/`，未建立新项目副本。用户P4规划保持原hash。

## 下一步

受检真实接触几何、Gather、给定接触行后的共同求解，以及现在的历史匹配/回存均已有原生证据，不应继续凭猜测修改这些公式。下一步建立使用相同PhysicsAsset、初始位置/速度、重力和时间步的完整UE落地/休眠基线，核对原生是否也触发当前四项失败，并比较每帧身体/接触数、岛与休眠状态。已有 `AlsPhysicsJointSolverReference.cpp` 中的隔离Scene和Step封装可复用，但现有孤立关节参考不能替代整角色世界证据。

普通60/高速120/平移30/旋转30失败仍未关闭。普通Ragdoll/Get-up/Pose Recovery、Mantle、完整Camera与最终十分钟性能预算仍是完整目标的未完成内容。

# Refactored Stop 姿态合成与骨架曲线元数据

在 `.` main 完成。普通 Demo 未切换；本批不是完整停止动作、Ragdoll 或最终角色观感验收。

## 实现

新增只读 `export_refactored_skeleton_curves.py`，从原 Stand Pose 的 Skeleton 引用导出 SK_Als 原生文本，独立资源绑定现有 catalog 字节哈希，不重写原 catalog/动画资产。

导出的 43 条 CurveMetaData 均为空，无 LinkedBones。`AlsRefactoredSkeletonCurves` 校验源身份、catalog、数量、唯一名称与空元数据；若将来出现关联骨骼或非默认元数据则明确拒绝，不静默省略 UE 的过滤步骤。

`AlsRefactoredStopPose` 按已验证原图求值：

1. 从冻结的十二个 evaluator 叶子取得原始绝对姿态与曲线。
2. 按本帧已更新的 HipsDirection selector 权重混合横向姿态，再按 VelocityBlend 合成四方向 MultiWay。零有效通道使用原 reference pose，曲线为空。
3. 按原左右腿遮罩进行 mesh-space rotation 混合，位置/缩放保持原 local 混合。曲线按 Override 的存在性规则覆盖；未出现在 layer 中的 base 曲线仍保留。
4. 设置对应 FootLock=1，最后按 Stop 状态标准过渡栈顺序混合并归一化旋转。Entry 与 Lock 状态直接使用完整 Movement Details 基底。

构造时冻结骨架、曲线并集、映射和固定叶子；锁曲线索引预计算。采样器校验源/机器候选归属、帧、骨架尺寸和有效数值，独立缓冲区与重入保护，支持重复求值。

基底输入必须是调用者本帧完成的 cache66，即经过 node119 的完整 Movement Details。当前 API 接收姿态/曲线缓冲区，尚不证明宿主给出的基底确实属于该帧；统一缓存宿主需要承担这一检查并接通真实顺序。

## UE 导出与构建证据

使用 UE 插件构建诊断技能要求的完整 Editor-target 构建与审计：UE `../UnrealEngine` 5.9，0 actions，ALS / AlsGodotExporter / AutoTestTools / BlueprintLisp 全部通过。

- 构建日志前缀：`20260925T060042406Z-3b0a74a41b8d46ee958135cf5a03a1c5`。
- fingerprint：`FA68F4872E1F3257ABA5208517AA41A339139838F2C35AE3476C635B880EEB72`。
- BuildId：`f7c75dee-59c9-476c-85d9-645c917dd66f`。
- 冷启动 Python commandlet 实际退出 0，写入正式资源；普通 Editor PID 40896 重启导出后实际退出 0。
- 正式资源与普通 Editor 结果字节相同，SHA256：`742D189244752D86C47B56580E47767BA1A7D15B75810A8EF902E2A0423F58F5`，18257 字节。
- 两次均记录 `ALS_SKELETON_CURVES_OK`、`assets_saved=0`；Python 语法解析通过。

记录在 `artifacts/refactored-skeleton-curves`。普通 Editor 仍有两条既有 `Condition failed` 及 PawnActionsComponent、NavMesh、LineSet material、MotionVectorSimulation、CrowdManager 旧警告；没有将它们称作干净日志。本批无 C++/插件配置修改，未重跑 DataValidation 或打包。

## 测试及范围

- 新 9 项：4 种曲线元数据变异拒绝、左右单 Forward Plant 遮罩/曲线验证、三频率连续姿态。
- 30/60/120 Hz 共 1260 帧、99540 骨输出，覆盖四种停止目标、换髋、混合栈、全零方向和取消重试；独立 sampler 输出一致。
- `related.trx`：Import 相关 66 项通过；最后预计算锁曲线索引后 `pose-final.trx` 9 项通过。
- `dotnet build GodotALS.csproj -p:Optimize=true --no-restore`：0 警告、0 错误。

首轮 `pose.trx` 为 7 通过/2 失败，原因是测试假定不受腿层影响的相同位置/缩放经过过渡仍精确不变。原状态栈先使用 float alpha/补数，再做 double TRS 累积，存在约 1e-8 量级漂移。测试改为独立计算该栈的期望位置/缩放，原 1e-20 平方差断言阈值未放宽；生产算法未因失败改动。`pose-second.trx` 与最终 9 项通过，失败记录保留。

当前连续测试基底为 reference pose，不是实际 Direction→Lean→Details→119 输出；单向验证复用底层 mesh-space blend 辅助算法，不能当独立 UE Stop 整图 oracle。没有本批 Core 全量、Godot 运行/渲染、最终性能或普通 Demo 切换。

## 下一步

统一 cache66→67→方向缓存的更新/初始化/姿态求值与 frame 归属；补 Idle/Rotate 状态源、外层118惯性化、停止状态回调与 StopQuick 的实际动作消费，进行真实 Parent 与缓存驱动的 UE 连续整图对照。随后接普通 Demo 并验证动作衔接。Ragdoll/Get-up 和原定其他功能/性能目标继续保留；音频、道具物理、头颈专项仍暂缓。用户已有修改未纳入本批。

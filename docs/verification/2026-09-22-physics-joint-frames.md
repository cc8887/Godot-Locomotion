# 关节创建缩放与原生惯量存储

主目录 main 补齐三处物理输入差异：骨骼身体创建缩放对关节位置的修正、UE 标量除法的运算顺序、独立保存的原生逆惯量。76 个关节连接端和 38 个动态身体的 conditioned inverse inertia 均与独立 UE 观测逐值一致，Godot 实际首步的八类身体求解输入差值全部为 0。

但完整世界轨迹仍不一致：普通 120 Hz 的 Mannequin 再次未在十秒内休眠，矩阵从上批 **9/12 退回 8/12**。本批关闭的是明确的数据/运算遗漏，不能称稳定性或普通 Ragdoll 已完成。

## 实现与依据

1. `USkeletalMeshComponent::InstantiatePhysicsAsset_Internal` 创建关节前用 `Instance.Scale3D * (Default.Scale3D * componentScale).Reciprocal()` 调整 Pos1/Pos2。旧导出只有资产 GetRefFrame，nativeBodyComponent 又没有保存身体创建缩放。新增只读 `-PhysicsJointFramesOutput=<新绝对路径>`，记录两模型的身体缩放、绑定质量/惯量、原始/实例/实际物理线程关节帧。未修改 UE 引擎、未保存资产。
2. `AlsPhysicsJointFrameCompiler` 严格绑定 mesh、asset、身体顺序、质量、惯量、原始关节帧，按上述创建公式重建位置。运行时不复制导出的 actual frame；它只用于独立断言。当前限于已有单位组件/近单位参考创建缩放，任意缩放需另行重建几何和质量。Mannequin 无位置变化，AnimMan 有 8 个连接端改变，全部 76 个端点的位置/旋转以及实例帧逐值一致。非幂等的重复修正会被原始帧校验拒绝。
3. 原生 `FVector / scalar` 先计算一次 float 倒数再逐分量乘；Core 原先直接 Vector3 除法。现保持原生顺序。
4. `FChaosEngineInterface::SetMassSpaceInertiaTensor_AssumesLocked` 从原始 double tensor 分别存储 float I 与 float InvI。二者相互取倒数不能复原原始位值。`AlsBodyInertiaCompiler` 现读取旧独立惯量输入中的 raw InvI，校验与资产质量/惯量绑定及正值/kinematic 零值，再重新计算 correction scale；没有复制 actualScale。新增 RawInverseInertia、ConditionedInverseInertia，Core 整链消费后者。
5. `PhysicsCoreJointReplay` 在全链初始化前应用新关节帧，同一结果供惯量、约束、接触诊断使用。独立对子仍按其原生参考输入驱动。普通 demo 尚未接此后端。
6. `Compare-WorldCapture.ps1` 的速度比较先恢复 float 存储值，避免 C# 与 UE 不同 JSON 数字字面量产生假残差。旧报告不改写；用新工具重新计算上批捕获作为本批对照。

冻结输入 `assets/config/v4_physics_joint_frame_inputs.json`，97596 字节；原生导出、冷复导出和仓库副本 SHA256 均为 `5843163F656799D55E2BF5B28D04AF700466C043A78DE912B3C70B61DA7B092B`。旧资产及参考文件字节未修改。

## 精确对照

两模型前三步的 114 个动态积分样本继续满足 COM 位置/旋转、V/W 差 0。两个运行时的新测试均确认 38 个动态身体首步惯量修正后值差 0。

Godot 实际捕获与独立原生窗口比较：首步 initial/predicted COM 位置/旋转、V/W、inverseMass、conditionedInverseInertia 八类全部为 0。这里仅指已观测的身体输入，尚非完整 joint Gather、调度或求解过程等价。

完成步求解后的最大线速度差（cm/s），两列均以恢复 float 后的相同工具计算：

| 模型 / 完成步 | 上批 | 本批 |
| --- | ---: | ---: |
| AnimMan / 1 | 0.000588717567 | 0.000007718172 |
| AnimMan / 3 | 0.000228889783 | 0.000008740569 |
| AnimMan / 160 | 0.332946733 | 0.283742769 |
| Mannequin / 1 | 0.000007705442 | 0.000007647998 |
| Mannequin / 2 | 0.000007921814 | 0.000068869962 |
| Mannequin / 147 | 0.038806040 | 0.067903835 |
| Mannequin / 150 | 1.497343196 | 1.498944339 |
| Mannequin / 160 | 1.398949384 | 1.276331118 |

部分帧改善、部分退化；仍不能声称整段轨迹更准确。完成 143..152 步的 20 个帧样本、416 个接触对，其端点方向、图内顺序、集合仍全部与原生一致。

## 稳定性矩阵

| Hz | 普通 | 高速 | 平移平台 | 旋转平台 |
| ---: | --- | --- | --- | --- |
| 30 | 休眠失败 | 通过 | 启动前休眠失败 | 启动前休眠失败 |
| 60 | 通过 | 通过 | 通过 | 通过 |
| 120 | 休眠回归 | 通过 | 通过 | 通过 |

普通 120：AnimMan 第 686 步睡眠；Mannequin 到 1200 步仍醒，右上臂/前臂/手的平滑角速度仍超阈值。末秒最大线速度 2.269686460494995 cm/s、角速度 0.2717686891555786 rad/s，最大锚点 0.5033142490207706 cm，接触点累计 98096。上批两者 679/1054 步入睡，而原生基线 635/1004 步，因此此回归必须继续处理。

普通 30：AnimMan 第 138 步睡眠，Mannequin 未睡；末秒线速度 6.8651251792907715 cm/s、角速度 0.41302865743637085 rad/s。原生相同场景自身也未满足十秒休眠；三个既有 30 Hz 失败保留，不降低门槛。

其他八项通过且末秒 V/W 为 0；60/120 平台完整经历静止、启动唤醒、运动、停止和再次睡眠。本批不重新解释或删除上批成功/失败结果。

## 验证记录

产物 `artifacts/physics-joint-frames-20260922/`：

- Core Release 固定 JIT、既有 P5a 过滤、串行全量 **2873 通过**；Import Release 固定 JIT串行全量 **2471 通过 / 1 既有跳过**。
- Import 定向最终 20 项在 .NET 8.0.28 / 9.0.17 均过；Core 惯量 4 项在 .NET 9 通过。拒绝旧绑定、单位/缩放/质量错误、非法逆惯量和重复非幂等修正。
- Godot Optimize 构建 0 warning / 0 error；30/60/120 接触 smoke、60 场景 smoke、144 组×12 帧原生对子、8 场景世界姿态检查通过。十二项矩阵以上表和 matrix/ 日志为准；失败场景没有成功 JSON。
- 74 个当前捕获在 captures/，输入报告 input-differences.json，最终速度报告 differences.json，重新计算的上批基线 prior-differences-stored.json，接触顺序 contact-order.json。
- 首轮 C++ 编译因 TObjectPtr 的 auto* 推导失败，改为显式 USkeletalBodySetup* 后完整 Editor 目标构建/审计通过；失败日志保留。最终 fingerprint `292DFBDA71496A0F988D4B83D2F86263A4A1A6E34AE505FB641A3EEE0782D36F`，主仓库/项目插件三个改动文件哈希一致。
- 新原生输出冷重导字节一致。DataValidation 0 error / 3 旧 warning；普通 Editor PID 33400 类加载标记成功、原生退出 0。两条旧 Condition failed 与历史间歇退出 AV 未修复。
- 首次测试错误地要求无变化的 Mannequin 也拒绝重复编译，后按是否实际改变修正断言；惯量残差经历关节缩放、标量倒数顺序、原生 InvI 逐层定位，首次未处理 kinematic root 的零 InvI 导致的拒绝亦保留在日志中。最终断言精确相等，没有扩大轨迹/休眠阈值。

## 下一步

从当前首步身体输入已一致的状态，独立观察原生实际关节求解次序、Gather 和首步输出边界，定位仍有约 7.7e-6 cm/s 残差及 Mannequin 第二步放大；随后重新验证普通 120 Hz 和完整稳定性。继续普通 Ragdoll/Get-up/Pose Recovery、Mantle、完整相机和十分钟性能目标。用户 P4 文档修改未纳入本批。

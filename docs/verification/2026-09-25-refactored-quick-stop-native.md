# StopQuick 原生连续播放与姿态对照

扩展现有 UE Transition trace exporter 的 Standing 分支：在真实 ALS Character/Parent/Linked Standing 实例上设置原状态字段，调用实际生成的 `AnimNotify_StopQuick`，执行原 Parent QuickStop 方法、Montage权重/推进/求值快照以及原Transition Slot。没有用C#逻辑生成参考结果。

新增30/60/120 Hz三组轨迹，共1050帧、82950骨，42个独立Montage实例、109个多实例求值帧。覆盖四序列、±180及175/176边界、Input/Target、Aiming/View模式、未知stance、大世界角度、零delta、多通知、移动时通知、显式停止、自然结束。Slot基底是受控原Stand Pose，不是完整Crouching或完整角色图。

## 原生发现与修复

首轮三组native测试均失败：176度速率UE为3.02777767，本地为3.02777791。保持速率零容差，调整本地Lerp运算分组以匹配此UE构建的优化舍入：`Min + abs(angle) * ((Max-Min)/180)`。

另一个差异来自实际字段类型：InputYawAngleWorldSpace/TargetYawAngleWorldSpace是float，RotationWorldSpace.Yaw是double。原模型以double接收前两项后直接相减不符合状态存储；现先量化所选世界角度为float，再提升至double与角色角度相减，最后转float执行unwind。参考中1000000090输入实际存为1000000064，因此相对1000000000得到64度，速率2.1944444。

这修正了上一份QuickStop验证记录的“double域先减”说明及176度测试预期。第一次本地测试只证明本地算式一致，不能代替native证据；本次保留失败TRX并纠正生产代码，没有增加容差或跳过测试。

## UE 运行证据

- 本地源与UE项目插件镜像修改同步，最终文件SHA均 `B18EDB57B8F31F3CE5783E8660D97DD92E72528FCD3736EDA9BE5D700C71E59C`。
- 全Editor构建前缀 `20260925T080604343Z-f701a0adc3b047a9b3b31e6c1072b137`：4actions、退出0，四插件审计通过。BuildId `f7c75dee-59c9-476c-85d9-645c917dd66f`，fingerprint `7E4D1FD3641A20374F671C846C603C5872EBDA6615AA49D9D9D41938D9C17F8D`。
- 冷导出PID25824、普通Editor PID3568均实际退出0；日志 `refactored-quick-stop-trace-cold.log`、`refactored-quick-stop-trace-normal.log`成功标记均完整。
- 冷/普通两份11907620字节JSON SHA256均 `B736A74879C92E71D0585486D038E398A46A793E6724D61C208E9109C2B93129`，保存原catalog/settings/stance/slot摘要与requestDigest。
- DataValidation PID26436实际退出0：0 errors、3条既有警告。普通Editor仍保留两条旧Condition failed及PawnActionsComponent/Navmesh/LineSet/MotionVector/Crowd警告，未称其修复。没有打包。

## 本地结果与限制

- `artifacts/tests/quick-stop/quick-stop-native-initial.trx`：原生三组初失败。
- `quick-stop-native-corrected.trx`：17项全部通过，包含14项QuickStop本地测试和3组原生对照。
- 补充TotalNodeWeight断言后，`quick-stop-native-final.trx`三组全部通过。
- 三组时钟/权重差0、速率严格相同；姿态最大差6.039613253960852e-14、曲线最大差1.1920928955078125e-7。沿用既有Transition位置2e-5cm、旋转/scale/curve2e-6等预算；并非逐位姿态相同。
- Godot Optimize构建0警告0错误，diff whitespace检查通过。

本批证明受控通知→原Parent QuickStop→连续Montage/Slot链路，不证明完整Standing/Rest Parent的连续刷新、角色物理或完整Demo。下一步继续其余Rest/Standing原生整链与统一宿主、Crouching、普通Demo和最终视觉/性能；所有Ragdoll/Get-up等原目标仍未缩减。用户改动及暂缓项未纳入本批。

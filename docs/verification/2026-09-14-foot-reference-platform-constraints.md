# 参考骨架绑定与平台约束验收（第一百八十一批）

## 本批结果

生产 Movement/Overlay 的参考腿骨位置、初始腿长与 UE 实际 Mannequin 网格
及 Skeleton 一致。第 180 批已经验证原生位置节点，本批补上其参考骨架来源
证据后，修正平台测试遗漏新 Rig 腿长裁剪的问题。运行时算法、动画数据和
裁剪比例没有改变；整段约 1.51/1.55 mm 位移仍然如实记录。

新/旧平台约束检查通过。这不等同于完整 UE AnimBP 最终姿势、实际脚掌接触
或用户视觉验收通过，也不关闭起步、换髋和上身问题。

## 原生参考骨架

`tools/unreal/export_foot_reference_binding.py` 在临时 `URigHierarchy` 中调用
`ImportBonesFromAsset`，读取 `GetGlobalTransform(bInitial=true)`。分别读取
`Mannequin.Mannequin` 和 `ALS_Mannequin_Skeleton` 的 pelvis、左右 thigh/
calf/foot 共七骨初始变换，不保存资产。完整 Editor 构建/审计、冷启动导出
与普通 Editor 重复均退出 0，本批未修改插件源码，无需重新打包未变的插件。

两个输出 `artifacts/foot-reference-181-{native,editor}.json` 字节相同，
SHA-256 `76065A4036F7FCE3A7836B76396342B1E8F414A66AC4A941256317874A6A61E3`。
已保存正式夹具 `tests/Als.Import.Tests/Fixtures/Refactored/foot_reference_binding.json`，
换行格式不同，但 JSON 语义与两次导出完全一致。

新增 `AlsFootReferenceBindingTests` 使用实际 Movement/Overlay 来源编译器、
完整来源索引和原始片段，再通过生产 `ToNativeComponents` / `Bind` 路径检查：

- 两套来源、两种 UE 资源、七个参考骨位置，最大差异均为 0 cm。
- 左腿初始链长 `82.76873 cm`，与原生层级逐边 float 累加完全一致。
- 左脚初始高度 `13.465731 cm`。
- 生产平台回放的腿长也是 `82.76873 cm`；原图规定左右腿共用左腿链长。

本检查直接证明参考位置与绑定长度，不把读取了旋转值等同于完整旋转/姿势
对照。现有新 Rig 的 `.99` 约束仍然使用这些原始输入。

## 平台测试修正

旧测试只用 `Based.ThighConstrained` 排除脚锁模块的腿根约束，没有识别后续
Refactored 位置节点的腿长裁剪。现在新 Rig 路径按输入几何判断：脚锁目标
加当前 Z 弹簧结果，到 thigh 的距离是否超过 float `LegLength * .99`。
不根据观测误差大小判断是否应该豁免。

- 无两类约束的连续窗口：目标水平误差及平台相对漂移仍要求 `< 0.0001 m`。
- 所有锁定样本：要求两个已求值、全权重脚部，以及同帧查询身份；最终脚骨
  必须在 `< 0.0001 m` 内达到位置节点目标，覆盖裁剪期间。
- 整段锁定漂移和原始目标水平误差继续输出；没有删除失败轨迹或增大容差。
- 旧 Rig 保持原来的完整判定分支。

`artifacts/foot-reference-181-{rotation,translation,old-rotation}.log`：

| 指标（m） | 新旋转 | 新平移 | 旧旋转 |
| --- | ---: | ---: | ---: |
| 总帧数 | 360 | 360 | 360 |
| 连续锁定样本 | 119 | 119 | 119 |
| 真正无约束的双脚连续样本 | 73 | 73 | 119 |
| 新 Rig 裁剪帧 | 45 | 45 | 0 |
| 无约束窗口最大漂移 | 7.218071e-6 | 5.462856e-7 | 1.4789761e-5 |
| 无约束目标水平误差 | 4.7683716e-7 | 1.1920929e-7 | 1.5078915e-6 |
| 最终脚骨到位置节点目标误差 | 6.7582965e-7 | 3.26808e-7 | 不适用 |
| 整段原始漂移 | 0.0015091707 | 0.0015490056 | 1.4789761e-5 |

两份新轨迹的 `RefactoredRigFeet` 输入/脚骨及 `RefactoredRig` 全部 360 帧
与第 180 批完全相同，重新比较第 180 批原生输出通过。绿灯来自正确区分
约束语义，没有修改角色动作来迎合检查。第 180 批分析器的 192 个样本按
每只脚独立分段；这里 73 个样本要求双脚同时无约束，统计口径不同。

## 回归清理

此前 Import `PublicSurfaceIsFrozenStackSafeAndGodotFree` 的唯一失败是清单
缺少已有 `SourceProfile`。生产 Sprint 与 Cycle Tail 使用它保持同一来源
闭包；配置类型的数组 getter 返回副本，构造也复制输入。本批更新接口清单，
新增只读属性、旧绑定拒绝读取、Core/Graph/来源身份一致和外部数组修改不
影响快照的测试。未修改任何姿势数值或动画 digest 基准。

- 相关 75 项通过：`artifacts/tests/reference-binding-181-targeted.trx`。
- 更新前全量：2237 通过、1 失败、1 跳过，正常结束。
- 更新后全量：2239 通过、0 失败、1 跳过，正常结束，
  `artifacts/tests/import-181-final.trx`。跳过的仍是原 LayerBlending Editor 项。
- 旧栈溢出用例单项及整类 32 项通过，两次全量亦未复现。未调整线程栈大小，
  未宣称根因已修复；保留第 178 批失败证据及后续复现债务。
- Godot 优化 Debug 构建 0 警告/0 错误，Python 编译检查和 diff 检查通过。

## 后续

继续新生产分发 30/60/120 Hz、多角色与阶段取消/恢复，之后对照整角色
起步接触、换髋、上身及原失败路线，验收通过后才切换默认完整入口。
Core 的 23 项历史失败本批未重跑；Import 的跳过项与未归因栈溢出也仍开放。
原 P5A–P7、全部 Overlay/道具、动作/物理恢复/相机及十分钟预算继续推进，
音频暂缓。未提交、合并或回滚用户修改。

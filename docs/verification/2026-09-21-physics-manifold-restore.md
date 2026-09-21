# 多边形接触流形的跨帧恢复

本批在主目录 `D:/GodotALS` 的 `main` 实现，接续 `2026-09-21-physics-capsule-manifold.md`。
普通 Demo 尚未切换到该 Core 物理后端；不是 Ragdoll/Get-up 完成交付。

## 补齐的原生行为

之前 `AlsContactHistory` 只保存摩擦锚点，几何接触点每帧重建。新增 `AlsContactManifoldCache`，独立保留上一次窄相产生的原始局部接触点与相对姿态，按 Chaos `FPBDCollisionConstraint::TryRestoreManifold` 恢复。

- 只对盒体/凸包组合启用；球体和胶囊不启用，遵守 `CollisionUtil.h` 的原生资格规则。
- CollisionTolerance 为较小形状完整包围盒最大边长的 `0.1f`，单位 cm。
- 相对世界位移按分量不超过 `0.2 * tolerance`，相对旋转 dot 大于 `0.9999`；dot 不取绝对值，保存姿态遵守原生 float 边界。
- 原始接触点横向漂移严格小于 `0.8 * tolerance`。更新第二端局部点及深度，保留点序；漂移点停用，原已停用点不复活。
- 活跃点不得少于 `min(原点数, 4)`。恢复后的基准仍是最近一次窄相，而非上一帧，防止逐帧累积漂移被掩盖。
- 空流形、漏帧、body generation/shape revision 变化、尺寸容差变化会重新查询。恢复后的最小深度超出当前 cull 则退出接触，下次重新发现。

`AlsWorldContacts` 将几何缓存和摩擦历史一起 Stage/Commit/Abort。先登记待处理槽，再调用 provider，确保查询异常、Gather 异常和后续求解失败均不会提前发布缓存；睡眠期间保持原 epoch。
Godot provider 每次检查实际资源 revision/dirty，缓存不能绕过失效检查。Trace 装饰器透传资格；`CORE_CONTACT_TRACE` 现在只代表新查询，恢复帧的求解输入应看既有逐阶段 capture。

没有扩大 Jolt margin。实际发现仍采用 cull=0；完整原生分离接触发现、动态 cull 距离、初次盒/凸包点生成、动态接触 shock propagation 尚未完成。

## 原生与生命周期验证

新增只读 UE 导出模式：

```text
-run=AlsGodotExport
-PhysicsManifoldRestoreOutput=<新的绝对JSON路径>
-unattended -NullRHI
```

参考文件 `assets/config/v4_physics_manifold_restore_reference.json` 调用真实原生恢复函数。
72 组 × 8 帧 = 576 帧，覆盖 1/4/6 点、累计位移、位移及转角阈值两侧、共同世界平移/旋转、大世界坐标、四元数反号、停用点、横向漂移、分离及中途空流形。
Core 独立重放给定输入；289 次恢复、287 次重新生成、28 个恢复时停用点，恢复判定全一致，局部点和最小深度最大误差均为 0。
该对照使用显式接触输入，不证明初次窄相、积分或完整物理轨迹等价。参考本身不执行 midphase cull；Core/Godot 生命周期检查另行覆盖当前 cull=0 的退出与重新查询。

两次冷导出字节一致，SHA256：
`F01E2F0E3C192CA7A2D371764AEFF6BAB6552965D76D8F910E9405C11EB9FEAB`。

新增 Core 11 项：累计基准、失败重试、身份/epoch/容差/四元数反号、4/6 点停用规则、容量/非法输入、零分配、世界 cull、quadratic 排除、回滚、过滤/revision 失效。
Godot 60 Hz 接触探针增加 5 项实际形状检查：原生尺寸容差、复用跳过窄相、脏资源拒绝且不推进 epoch、sphere/capsule 排除；旧 367 精度、7 几何、5 睡眠生命周期和 3 动态场景均通过。

## 十二项实际整链回归

保留原生睡眠阈值/迭代次数、十秒落地、二十四秒平台和至少一秒睡眠保持门槛。M=Mannequin，A=AnimMan。

| 平台 | Hz | 初次休眠 M/A | 停止后休眠 M/A | 结果 |
| --- | ---: | --- | --- | --- |
| 平移 | 30 | 188/153 | 451/439 | 通过 |
| 旋转 | 30 | 236/154 | 均未睡 | 失败 |
| 平移 | 60 | 92/69 | 877/1151 | 通过 |
| 旋转 | 60 | 92/69 | 876/880 | 通过 |
| 平移 | 120 | 170/128 | 1723/1909 | 通过 |
| 旋转 | 120 | 170/938 | 1744/1839 | 通过 |

| 落地 | Hz | 最大锚点 cm | 末秒线速度 cm/s | 休眠 M/A | 结果 |
| --- | ---: | ---: | ---: | --- | --- |
| 普通 | 30 | 2.586935 | 2.348445 | 未睡/66 | 失败 |
| 高速 | 30 | 7.657850 | 5.754269 | 均未睡 | 失败 |
| 普通 | 60 | 1.199867 | 0 | 415/145 | 通过 |
| 高速 | 60 | 2.206116 | 1.399692 | 592/211 | 失败，M 睡得太迟 |
| 普通 | 120 | 0.423387 | 2.532747 | 未睡/254 | 失败 |
| 高速 | 120 | 0.853214 | 0.740318 | 1135/393 | 失败，M 睡得太迟 |

通过数仍为平台 5/6、落地 1/6，总计 6/12；没有关闭上一批六项失败。
60 Hz 普通落地实际复用 1,602 对接触，恢复已生效。30 Hz 旋转停下后末秒线速度 4.248536 cm/s，角速度 0.277157 rad/s，仍超自然休眠条件。
原生规则补齐并未带来所有连续轨迹改善：普通 120 Hz 末秒速度从 2.250259 增至 2.532747 cm/s。保留实际失败，不将独立公式对照当整链稳定性完成。

## 构建、保存与下一步

Core Release 2730 通过（既定两个旧 golden/schema 类排除）；Import 2367 通过、1 旧条件跳过；Godot 优化构建通过。
UE 完整 Editor target 和插件审计通过；参考冷导出与重导退出 0，canonical/mirror 三个源文件哈希一致。
普通 Editor 重启加载 exporter 并退出 0；启动日志仍有两条 `LogAutomationTest: Error: Condition failed`，在上一批同一启动位置也存在，未定位或宣称修复。DataValidation 退出 0，0 error/3 既有警告。此前偶发 Editor 退出异常没有因此宣称已修复。
本批日志与失败产物在 `artifacts/physics-manifold-restore-20260921/`。用户 P4 规划文档修改未动。

下一步继续检查盒/凸包初次接触的特征与点序、原生 cull/分离接触和动态接触质量缩放；之后完成普通动画到物理 owner 的交接、pelvis/胶囊/相机跟随、Get-up/Pose Recovery。Mantle、完整 Camera 和最终十分钟性能预算仍在后续清单。

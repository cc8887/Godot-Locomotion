# 实际失败帧的原生胶囊重放

本批在主目录 `.` / `main` 补齐当前 `native_capsule` 格式的 capsule-box 重放。不是修复整链休眠，也没有切换普通角色后端。

## 为什么需要新重放

旧 exporter 仅识别居中Z轴的 `capsule`，会跳过当前原生leaf胶囊。不能用 endpoint0 + axis * height 重新构造 FCapsule 后宣称复制了原始几何，因为构造器会再次计算并存储float轴和长度。

新增 `PhysicsNativeCapsuleTraceInput=` / `PhysicsNativeCapsuleTraceOutput=`。在隔离UE世界按trace中的mesh与bone创建真实PhysicsAsset身体，精确匹配原生leaf的端点、轴、长度、半径，再用FCapsule拷贝构造，不重新归一化轴。多匹配/不匹配明确拒绝。trace中的这些字段按float契约读取：System.Text.Json最短往返小数先恢复float再精确比较；没有扩大误差容限。

Godot opt-in trace增加局部两侧接触点、局部法向、NativePhi和实际detector cull。原生查询使用该float cull，不再仅重放固定0/3cm。期望接触只保存在source用于后续比较，不参与原生接触生成。旧export命令与旧参考未修改。

## 失败场景证据

重新运行高速120Hz整链，捕获1140–1145帧，筛选pelvis/thigh_l/calf_l/foot_l及其对端。落地、休眠、限位诊断行与上一批逐行一致，原有休眠失败仍出现。

726次新几何查询中：270次native_capsule/box、246次native_capsule/native_capsule、114次native_capsule/convex、84次box/convex双向、6次convex/convex、6次native_sphere/convex。

本批重放270次capsule-box，另外456次明确记录为其他类型；没有加入synthetic行凑数。270次中246次无接触，24次有接触，共54点。Core重新计算与UE输出逐点精确一致；同时原始Godot失败帧记录的点数/点序/局部点/法向/NativePhi与UE输出也精确一致，最大差均0。

这只能排除所捕获六帧内capsule-box新接触生成的偏差，不能排除其他形状、持续历史、Gather、solver或其他帧的问题。trace只观察provider新查询，恢复流形须另看island capture。

## 验证

- Godot优化构建0errors/0warnings，高速120Hz带trace实际运行完成并复现既有验收失败；诊断指标没有变化。
- 定向Import四项全部通过：新失败帧源接触对照、新270项Core重放、旧744与8640原生参考，所有点/法向/Phi差0。本批未改Core/Import生产逻辑，不重复全量；上一批全量为Core2834、Import2419/1既有跳过。
- 两次冷导退出0，611,251 bytes，字节一致，SHA256 `4B878E5AEFA7154E565237AFC9DB9B50C82A7C2AB3A763AA22663B96D62FD73A`。
- 篡改radius和缺少cull两项实际commandlet负例均退出47，明确错误原因，未创建输出文件。
- 最终完整Editor target构建/项目插件审计通过，fingerprint `B3723B7CC393E8E1DA00FB6543983556BF3D37DA3D0EEE46027085C55BE3ADAA`，主仓与UE镜像一致。DataValidation退出0，0errors/3既有warnings。
- 普通Editor PID33244加载标记成功、原生退出0、DLL无占用。两旧Condition failed仍在，既往间歇退出访问冲突未修复；不把一次退出成功作为修复证据。

产物与日志：`artifacts/physics-native-capsule-trace-20260922/`。完整十二项矩阵本批未重跑，最新仍上一批8/12，普通60/高速120/平移30/旋转30未过。用户P4规划修改保持原hash不变。

下一步优先重放同批246次capsule pair与114次capsule-convex，再核对历史/Gather输入；sphere混合路径、普通Ragdoll/Get-up/PoseRecovery、Mantle、完整Camera和十分钟预算仍需完成。

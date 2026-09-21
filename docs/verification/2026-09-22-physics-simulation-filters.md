# 原生有效碰撞过滤与早期突变修复

工作目录 `D:/GodotALS`，分支 `main`。普通角色入口尚未接入实验 Core 物理后端，本批不能视作 Ragdoll/Get-up 完成。

## 原因与修改

完整 UE 世界前 3 帧诊断证明：两套资产 root 的形状保持 simulation/query enabled，但有效 BlockChannels 为零，通道为 PhysicsBody（5）。其余形状阻挡掩码为完整 64 位全 1。原生没有 root→foot_r 接触，旧 Core 因所有形状统一注册 layer=mask=1，多出这条接触。

不能仅根据 authored `collisionEnabled=3`、`QueryAndPhysics` 或空 `collisionResponses.responseArray` 推断有效响应。也没有将所有 kinematic 身体禁碰：环境平台仍参与接触。

- UE `PhysicsWorldContactFrames=1..10` 可选输出前几帧真实形状过滤、ObjectState、膨胀 bounds，以及求解后接触点/禁用/激活/结果。默认 0 保留原输出路径。接触为 **after-solve**，不是 Core pre-Gather 输入。
- 掩码使用固定 16 位十六进制字符串，避免 JSON double 丢失 uint64 位。独立提取工具验证至少三帧过滤不变，绑定 mesh/body/bone/nativeIndex/authoredIndex 与 runtimeShapes SHA256。
- Core 在原 Godot 双向 layer/mask 和身体对禁碰之外，增加独立 64 位原生 channel/block 筛选。场景 static/kinematic 形状按诊断世界契约映射 WorldStatic/BlockAll，Godot 路由仍保留。
- 严格依据本机 UE `Chaos/CollisionFilterData.cpp:FShapeFilterData::NarrowFilter`：双方通道必须被对方阻挡。`MaskFilter` 是 query 元数据，不额外用于该 simulation narrow filter。Overlap 不生成阻挡行，本批不实现 overlap gameplay 事件。
- 所观察设置限定为 SkeletalMeshComponent PhysicsBody、QueryAndPhysics、all channels Block 的诊断配置；不是通用动态 collision profile 重建器。绑定变化会拒绝旧数据，不回退硬编码 root 名称。

## 实测

同初态普通 30 Hz，40 个身体、38 个逻辑关节，Godot 实际运行捕获与独立 UE 世界速度比较：

| AnimMan 完成步 | 修复前最大线速度差 cm/s | 修复后 cm/s |
| --- | ---: | ---: |
| 1 | 0.021499 | 0.0001530494 |
| 2 | 105.3521973 | 0.00004837695 |
| 3 | 95 量级 | 0.00003600880 |

新连续 20 步捕获进一步定位下一处分歧：AnimMan 前 14 步最大差不超过 0.0003442 cm/s，第 15 步 foot_l 升到 21.70255345 cm/s / 1.03844287 rad/s；Mannequin 第 18 步差 0.0003061 cm/s，第 19 步 lowerarm_r 升到 15.21846436 cm/s / 8.0736311 rad/s。因此第 2 步异常已修复，但后续完整轨迹仍不等价。

十二矩阵仍 **9/12**：60/120 Hz 普通、高速、平移和旋转全过；30 Hz 高速过，普通/平移/旋转休眠失败。普通 30 Hz AnimMan 第 204 帧入睡，原生第 159 帧；Mannequin 未入睡，末秒最大线速度 7.439918 cm/s、角速度 0.428579 rad/s。未放宽门槛。普通 30 Hz 最大锚点误差 1.740284 cm。

## 验证与产物

- 优化 Godot 构建零警告/错误；30/60/120 Hz 完整接触 smoke 全过。
- Core 固定 JIT Release 串行全量 2839 通过（保留既有两个过滤排除类）；最终移除 query-mask 误用后针对新过滤两项复验通过。
- Import 固定 JIT Release 串行全量 2439 通过、1 既有条件跳过；最终 query-mask 元数据独立保留后，新过滤/原生窗口 9 项在默认运行时与 LatestMajor roll-forward 各通过。未将后加的 1 项算入此前全量计数。
- 实际场景 60 Hz smoke：3 场景、13 几何、9 生命周期检查通过，包含平移/旋转响应。
- UE 完整 Editor 构建与插件审计通过，fingerprint `2C9945D4C49A8A7461F181C99C7DF7795939CCDF3D2C37D900E79D82ACD7B806`。
- 原生世界冷导和重复导出字节一致：SHA256 `481F683038D7A3DD07F100A1ADB72ECD5AE3D5EFEFFCB864EB15848FEE7AFB88`。
- DataValidation：0 error / 3 既有 warning。普通 Editor PID 30704 加载标记成功、原生退出 0、DLL 释放；两条旧 Condition failed 仍在，既往间歇退出访问冲突未修复。
- `v4_physics_simulation_filters.json` SHA256 `B236CCDC4EBF8FB5C444BF06BB1F5B45657E4545E9CA706BCFD1E6DB4EA3B1BE`。
- `v4_physics_world_contact_window.json` SHA256 `6A7BDEF5115D92626A09BE06ECA00B5B72D0F1ED9F5CE8485613CD3847ED952F`，两模型 frame0..3，包含 full-source hash，不包含不适用的十秒预算摘要。
- 原始构建/导出/测试日志、12 项矩阵、初始及连续 20 步捕获和差异报告：`artifacts/physics-world-contacts-20260922/`。已有 native-final/首次 contacts-only 产物保留，不与最终 structured 混用。

下一步围绕 AnimMan 完成第 15 步、Mannequin 完成第 19 步扩展原生接触窗口，对照实际接触发现/激活/历史与排序，再处理三个 30 Hz 休眠失败。普通 Ragdoll、Get-up、Pose Recovery、Mantle、完整 ALS Camera 与最终十分钟预算仍未完成。

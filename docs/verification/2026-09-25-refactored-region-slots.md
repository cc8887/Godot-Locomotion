# Refactored 区域 Slot 的共享播放与通知通道

在主目录 `.`、`main` 上扩展共享 Montage 通道。旧编号 0/1（转身）、2（BaseLayer）、3（Grounded）、4（PostLocomotion）保持不变，追加 Head、Spine、ArmLeft、ArmRight、Pelvis、Legs、Curves 为 5—11。

## 实现范围

- 统一 Slot 数量、合法性与名称映射；共享播放资源、姿态混合和通知绑定均接受这十二个编号，拒绝越界编号。
- 通知相关性从 byte 扩展为 ushort，允许最高 Curves 位；当前帧及上次提交帧的相关性沿用原行为。通知遍历顺序、随机数、实例身份、组内替换和候选提交/丢弃规则未改。
- 每个 Slot 的通知存储预分配由 5 组增为 12 组；全局事件容量未增大，也未引入每帧分配。代价是每个运行时实例多出 7 组固定通知缓冲。
- 实际 Refactored Layering 图编译时校验七个 Slot 名称，未知区域立即报错，防止运行时错误路由。普通 V4 宿主尚未使用新增高位。

## 验证

- Core Release Montage/TurnNotify 定向：172 通过、0 失败。新增七个区域通知用例与两个共享实例/边界用例；覆盖高位相关性、消失后上一帧保留、discard 重试、十二个 Slot 共存、右臂组内替换且不影响其他组。
- Import Release Montage/Mantling/RefactoredLayer：212 通过、0 失败，包含已有原生轨迹回归。加入未知 Slot 名称拒绝检查后，完整图测试再次运行：10 通过、0 失败（其中 9 项与前次重叠）。
- Godot Optimize 构建：0 warning、0 error。
- 普通并行场景 `native_camera_demo_smoke.tscn`：60 Hz、480 帧，`ALS_NATIVE_CAMERA_DEMO_OK`，含 Ragdoll 与第一人称切换，进程退出 0。
- 日志与 TRX：`artifacts/refactored-region-slots/`。未执行全量测试或新 UE 导出；新增区域姿态测试使用受控采样器，不是新的原生覆盖对照。

## 后续与边界

本批提供真实分层接入所需的共享播放基础，尚未完成普通 Demo 的 Refactored Layering 宿主迁移。下一步是区域 Slot provider、真实 Montage 覆盖的 UE 对照、生产骨骼/曲线布局与 Head/View 接入，再完成普通 Mantle 流程。此前物理稳定性、Flail、最终视觉和十分钟性能预算等缺口仍保留；头颈拉伸、道具物理、音频继续按用户要求暂缓。用户已有文件修改未纳入本批。

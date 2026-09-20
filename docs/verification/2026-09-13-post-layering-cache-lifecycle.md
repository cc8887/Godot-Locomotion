# Post Layering 求值缓存与遍历历史

日期：2026-09-13。第一百二十九批，承接真实输入与生产接线。

## 本批完成

新增 `AlsAnimationGraphFrame`，由分层帧所有者保存已提交的初始化、骨骼缓存、
更新和求值遍历计数。下一帧从已提交历史产生候选，取消不推进计数。原先
LayerBlending 用 FrameId 取模生成求值计数的做法已移除；计数采用原生
有符号 short 回绕规则，跳过无效值 -1，允许角色帧身份跳号。

初始化和骨骼缓存计数在同一固定骨架实例内保留，并传给 LayerBlending /
BasePoses。新角色代际创建新所有者。该值类型支持显式 globalFrame；当前
生产调用仍以角色 FrameId 作为全局帧标记，不能据此声称已经支持 UE 的
任意外部求值调度、热换骨架或根节点强制重新初始化。

`AlsAimLayerFrameStage` 现在持有 Post Layering 的双缓冲生命周期缓存，
初始化与 CacheBones 都访问原图的两个读取节点。一次根求值打开一个
作用域，按 Aim / Spine 的实际相关性读取缓存。缓存缺失时，通过
`IAlsPostLayeringPoseSource` 调用父所有者中的真实 LayerBlending 求值；
第二条分支复用相同姿势和曲线，不再次计算 LayerBlending。

生产组合的顺序为：BaseLayer、Overlay 求值，随后上身按需读取 Post
Layering，再执行双手 IK。来源时间和事件仍在共享更新批次中推进；缓存
回调不推进时钟、不发布 gameplay 事件。提交前要求作用域已关闭且源求值
恰好一次，整个帧被拒绝时保留上一份已提交缓存历史。

源回调异常会使候选缓存失效，取消时替换该缓存实例；这项分配仅发生在
异常路径。缓存填充成功但上身验证失败时，取消会关闭作用域，下次准备
重新复制已提交计数并清除 payload，不读取失败候选。

## 原生规则核对与尚未实现的根节点

本批只读核对了 UE5.9 的 `AnimNode_BlendListBase.cpp`、
`AnimNode_BlendListByEnum.cpp`、`AnimNode_SaveCachedPose.cpp` 以及本地
ALS V4 的 `v4_layering_inputs.json`：

- 最终 Root 来自 BlendListByEnum；普通分支接 Foot IK，Ragdoll 分支接
  Ragdoll States。实际暴露引脚的混合时间为普通 0.4 秒、Ragdoll 0.5 秒。
  导出的编辑器 Node 后备默认值 0.1 / 0.1 不能替代这些实际引脚值。
- 根的 ChildUpdateMode 为 Default，不等于激活时强制重置子图。具体状态机
  的重新相关初始化需要分别处理；不能把返回普通分支实现为所有动画归零。
- SaveCachedPose 的初始化、骨骼缓存和求值各有计数规则。沿用此前原生探针
  结论：不能借用 PoseLink 的调试 UpdateCounter，虚构 SaveCachedPose 自身
  的相关性间隔重置。求值 payload 还必须属于当前顶层作用域。

这些根选择器规则目前是审计结果，尚未接成完整普通/Ragdoll 根运行时。
上身缓存的初始化/骨骼缓存回调目前校验缓存身份；父所有者把同一组计数
传给 LayerBlending。不能把这一固定普通分支接线描述为任意根重新初始化
和热换骨架的回调传播已经完成。Ragdoll 物理和恢复仍归 P6。

## 验证结果

- Debug 优化构建通过，0 警告、0 错误。
- Core 遍历/缓存专项 40 项通过，包含独立计数、short 回绕、同全局帧内
  多次更新以及既有作用域/生命周期检查。
- 真实受控组合与统一所有者对照 1,260 帧通过。184 帧同时读取 Aim 和
  Spine 分支，Post Layering 每帧只求值一次；姿势、曲线、同步和事件保持
  一致。逐帧取消/重试及 10 次所有者求值异常保持提交历史不变。
- 真实输入专项 1,260 帧通过：空中 168、蹲伏 423、来源事件 32，逐帧
  重试一致，10 次异常回滚通过。
- 新 `PostLayeringCacheSmoke` 在 30/60/120 Hz 共 420 帧验证实际上身
  缓存适配器，140 帧双读取；420 次来源内部抛错、420 次缓存填充后输出
  验证失败，全部可同帧重试并与未使用缓存的组合逐项一致。此项使用受控
  Post Layering payload，故不充当独立 UE 最终姿势对照。
- 新生产 single / parallel 各 600 帧通过，result=`946C9F387EA43F26`、
  fullPose=`6B39A65B68F3FAE3`、root=`A4F6C26CBAB8A0E7` 一致，事件
  28，lag/stale 均为 0，与上一批新路径保持一致。
- 真实 OpenGL 渲染横移回放 720 帧完成，保存 24 张 PNG。已查看第 90、
  270、450 帧：手臂有弯曲和摆动，腿部存在交叉姿态。每 30 帧截图不足以
  判定换髋等待时序；交叉姿态本身也不能单独判定动画正确或错误。
  诊断最大单帧脚旋转 14.352 度，未触发该场景的检查，但这不是支撑脚
  水平滑移或 UE 等价性的验收标准。

证据：

- `artifacts/test-results/post-cache-core.trx`
- `artifacts/post-cache-owned.log`
- `artifacts/post-cache-mapped.log`
- `artifacts/post-cache-failure.log`
- `artifacts/post-cache-worker-single.log`
- `artifacts/post-cache-worker-parallel.log`
- `artifacts/post-cache-strafe-visual.log`
- `artifacts/post-cache-strafe-visual/`：截图及 frames / body-frames / violations JSON。

## 下一项与完成边界

本批补的是原 P3/P4 最终图依赖中的缓存接线，不是新增玩法，也没有关闭
当前交错步、换髋、上身和起步滑步问题。完整上身生产入口仍需显式
`--layered-frame`，默认 Demo 尚未切换。

继续普通分支根生命周期与重新相关处理，闭合最终 Foot IK、Foot Lock、
pelvis、平台的原图规则，再做 UE/Godot 同输入、同脚相位的密集多帧对照，
切换默认入口并人工验收。完整 Ragdoll 根分支随 P6 接入。

原 P5A 通用事件/动作剩余项、P5B 全 Overlay/道具玩法、P5C Mantle/Roll /
Root Motion、P6 Ragdoll/Get-up/Pose Recovery/Camera、P7 十分钟性能验收
均保留。全 Core 既有 23 项失败、Import 分配稳定性及 p95 2.559ms 超过
2.5ms 的既有问题未在本批关闭；没有重跑并声称全量通过。音频继续暂缓。
本批没有修改 UE 原生插件或新增 UE 全图输出探针。

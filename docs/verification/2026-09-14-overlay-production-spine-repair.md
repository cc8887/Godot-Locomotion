# Overlay 生产接入与脊柱坐标修复（第 189 批）

## 实际修改

继续 P3/P4 完整上身修复，并前置接入 P5B 所需的装备姿态选择。此前底层
Overlay 图已经存在，但生产调用 PrepareFromFrame 没有传入 overlayKind，
始终使用 Default；角色命令也不包含该状态。普通场景因此不能验证实际
手部 IK 和独立 Spine 分支。

本批新增值类型命令 RequestedOverlay，由玩家输入快照传入，经 Motor
的原始帧输入进入生产动画所有者。动画提交记录实际 Overlay，取消不会
发布候选状态。有效枚举包含全部 13 种选择，未知值在输入/命令解析处
拒绝；返回 Default 会清除装备姿态选择。没有读取 Worker 外的可变场景
装备状态来绕过每帧快照。

P4LocomotionDemo 增加导出的 Overlay 属性，也支持 --overlay=Rifle、
--overlay=Bow 等启动参数，要求已有 --layered-frame 完整入口。视觉场景
通过同一输入适配器读取选择。原键鼠映射保持不变。该接口选择的是动画
姿态，武器模型挂接、装备菜单、收起/取出与动作玩法尚未完成。

UE 对照捕获也改为读取实际 OverlayState/OverrideState，删除诊断中的
固定零值；不能在 Godot 已切换装备时仍让 UE 播放默认图。

## 新分支暴露的真实错误

初次 Rifle 瞄准回放中，MainMovement/BaseLayer/PostLayering 一致；第 661
帧站稳转镜头后，PostAim 的 pelvis 和三个 spine 骨各出现约 32.3026°
差异。输入、分层和曲线正确，首个差异就在四骨 ModifyBone 旋转。

AlsAimLayerRuntime 错将 Godot 世界空间的 -Y yaw 用于 FBX 骨骼姿势。
当前导入姿势与 UE 的四元数转换是 (x,y,z,w) → (-x,y,-z,w)，所以
原 UE +Z yaw 在这一姿势空间应绕 -Z，而非世界 -Y。已修正旋转轴，
保留原 yaw/4、四骨顺序、组件空间累积、曲线反馈、过渡与求值时序。

更新原坐标测试的明确空间合同，并通过完整原 UE 节点回放验证，未调
权重或放宽阈值。失败的 movement-189-rifle-scoped.json 保留；修正前后
graph.request.json 完整解析一致，因此修正后复用同一个原生输入基准，
再用普通 Editor 对修正后请求独立重复。

## 原 UE 对照

| 场景 | 帧数 | 实际启用 | 四阶段最大位置 cm / 旋转 ° | 最终六根手臂骨最大旋转 ° |
| --- | ---: | --- | --- | ---: |
| Rifle | 1080 | 左手 IK 1078 帧，Spine 1078 帧 | 0.000029373 / 0.000092775 | 0.000072767 |
| Bow | 1080 | 右手 IK 1078 帧，Spine 1078 帧 | 0.000030575 / 0.000095287 | 0.000127141 |

四阶段为 MainMovement、BaseLayer、PostLayering、PostAim。原门槛仍为
0.001 cm / 0.02°，缩放 1e-5，共享曲线 1e-4；两组共享曲线最大差
4.76838e-7，均无排除帧。两组各 2549 次来源 Tick 时间差为零、权重最大
差 2.98024e-7。均包含实际原地 Rotate 控制 19 帧及停止 Montage。

新增 compare_production_hand_chains.mjs，先要求同文件的四阶段报告全部
通过，再比较最终 upperarm/lowerarm/hand 左右共六根骨的局部变换。
最终手臂位置最大差约 0.000001908 cm。原生后处理对手臂产生的最大
旋转变化分别约 10.2016° / 15.0652°，不是权重为零或未产生修正的空分支。

V4 手部在脚部之前、当前 Refactored 脚部在手部之前，顺序差异仍明确
保留。这里证明的是最终六根手臂骨局部结果，不包含整角色世界空间、
pelvis、脚部、接触、其他骨骼或所有 Overlay 的完整等价性。六条
Refactored 专用曲线仍在有限范围报告中逐项排除，未宣称全曲线相同。

步枪和弓箭各输出 90 张 PNG，视觉检查通过，最大脚单帧角度分别为
11.690° / 11.671°。抽查步枪第 672/720 帧可见瞄准与转身后姿态；没有
武器模型，不能把姿态截图当作完整装备玩法或用户人工验收。

## 回归与构建

- 输入/合同/命令解析：60 项通过；Aim/手部专项：31 项通过。
- Godot 输入 Smoke：13 个 Overlay 选择、同帧重复读取、非法值后同帧
  重试、返回 Default 通过；原 11 输入动作及镜头方向检查通过。
- 正式 60 Hz 十角色单线程/并行各 3621 个提交帧。角色交错使用 Rifle
  和 Pistol2H，切换并最终返回 Default；含变化视角、Aiming、跳跃、
  蹲姿，两次取消及一次提交等待。每组 2400 个非默认装备帧，实际
  动画选择与捕获命令/角色/帧逐帧相同。
- 最终修正版本两模式摘要完全相同：pose=93D8549BFC22BF7D，
  root=5EA89CAFCC73D6BF，result=32844E54C7800317；events=150，rays=6544。
  早期零瞄准回归日志另外保留，不混入最终版本结果。
- Godot 最终构建 0 warning/0 error；Node 语法和 diff 空白检查通过。
- 本批没有改 UE 插件源码，复用第 188 批完整构建/四插件审计通过的
  探针，不将旧构建、DataValidation 和打包重复计为本批新增成果。
  两组冷探针和普通 Editor 均退出 0，完整结果解析后分别相同。
  普通 Editor 保留既有日志错误/警告，不能称为干净日志。

旧 Core 23 项失败及缓存测试 79/68 骨布局失败未清理，保持原测试债务。
默认完整入口尚未切换，P5A–P7 仍开放，音频暂缓。

## 最终复验与下一项

默认姿态回归 1080 帧、36 PNG 通过，最大脚单帧角度 13.264°；与第 188
批瞄准场景的 input、四阶段及 PreFoot、最终 pose/curves、players、
Montage、IdleControl、Stop 通知逐帧完全相同，并通过原 UE 基准对照。
步枪和弓箭的普通 Editor 重复结果均与各自冷探针完整解析一致。

下一项继续最终脚部、支撑接触、
平台和整角色人工配对，之后切换默认完整入口；P5B 的全部 Overlay
切换/道具挂点/装备动作仍须完成，不能用本批两类姿态对照替代。

主要产物：artifacts/movement-189-rifle-fixed/、movement-189-bow/，及
对应 -native.json、-editor.json、-scoped.json、-hands.json、-clocks.json。
回归日志为 overlay-189-input.log、overlay-189-dispatch-fixed-single.log、
overlay-189-dispatch-fixed-parallel.log。原失败 Rifle 捕获和报告保留。

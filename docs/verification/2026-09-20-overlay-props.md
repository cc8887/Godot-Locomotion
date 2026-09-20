# Overlay 原生道具装备与弓动画

本批在 `.` 的 main 上继续，基于 `230930d`。普通 Demo 新增 Q / E
切换全部 13 种 Overlay，并按源蓝图装备 8 种持物状态、7 个不同模型。
前 5 种 Overlay 清空道具；两种手枪状态复用 M9 实例。音频仍暂缓。

## 源规则与实现

本机锁定的 ALS-Refactored `b754d6f0f2bb03741d301f8fb88077ebfe561e17`
的 AlsCharacterExample 没有这批 V4 道具的装备表，因此对应 UE 资产的
`ALS_AnimMan_CharacterBP` 和 `Bow_AnimBP` 是本批持物规则的来源。
`tools/unreal/export_overlay_props.py` 导出原生图文本、实际组件默认值、
17 次原生 UpdateHeldObject 执行结果和 Bow_Draw 原始 DataModel keys。
未改写、保存 UE 资产。原始 keys 的文件字节哈希绑定到独立来源索引。

按 ue-diagnosing-plugin-build-load 技能要求，导出前完成完整 Editor Target
构建及 ALS、AlsGodotExporter、AutoTestTools、BlueprintLisp 插件 receipt / BuildId
核对，全部通过。引擎 BuildId：`65396328-6094-4236-9daf-a6f6f212788e`。
证据在 `artifacts/overlay-props-20260920/ue-build-contract.log` 与
`ue-corrected-export.log`。

编译器读取 Switch、清空顺序、受保护的 Mesh/AnimClass 赋值、SnapToTarget、
Offset 和 Bow Draw 的完整连接，原生执行结果用作交叉验证。

| Overlay | 模型 | 挂点 |
| --- | --- | --- |
| Rifle | M4A1 | VB RHS_ik_hand_gun |
| Pistol1H / Pistol2H | M9 | VB RHS_ik_hand_gun |
| Bow | Bow | VB LHS_ik_hand_gun |
| Torch | Torch | VB LHS_ik_hand_gun |
| Binoculars | Binoculars | VB RHS_ik_hand_gun |
| Box | Box | VB RHS_ik_hand_gun |
| Barrel | Barrel | VB LHS_ik_hand_gun |

这两个 VB 是逻辑虚拟骨，没有独立 Godot physical bone。运行时使用最终逻辑
虚拟骨局部姿态，乘上最终物理父骨的世界姿态。按实际导入场景计算并抵消一次
FBX 轴转换，防止道具方向被再次旋转；保留模型几何、蒙皮和材质绑定。

Bow_AnimBP 的 SequenceEvaluator 用 `Enable_SpineRotation` 驱动 ExplicitTime。
Worker 以同帧最终曲线采样 Bow_Draw 的 24 骨原始姿态；没有独立 AnimationPlayer
时钟，也不使用 FBX 重烘焙关键帧。固定大小值快照携带 frame/character/generation、
Overlay、挂接变换和弓姿态，随原有成功结果发布。Main 通过身份检查后更新预缓存
道具实例；Worker 不访问道具 Node。暂停保留当前提交，语义停用清空持物，销毁
释放角色自己的实例。等待提交或动画失败时保持旧道具。

## 验证结果

证据根目录：`artifacts/overlay-props-20260920`。

- Godot 构建 0 警告 / 0 错误。Import 全量 2283 通过、1 个已有 skip；后续
  补充默认组件及原生执行覆盖校验后，道具专项 8/8 通过，最终 Bow gate 校验
  再次 8/8 通过，见 `compiler-gate-final.log`。
- Core Release 2545/2545 通过、0 skip；本次按既定回归命令排除两组历史
  P5A Golden / TraceSchema 大型回放，未把它们计入本批认证。
- 原生 Bow oracle：9 个时间点 × 24 骨，最大位置误差 `5.9604645E-08` 米；
  最大四元数 `1-|dot|` 为 `1.1920929E-07`。1000 次热采样托管分配为 0。
- 真实 Demo 输入专项单线程、并行各完成 1600 帧；13 种 Overlay、Draw 0～1、
  切换等待 Main Commit、语义停用恢复、E 重复键过滤通过。最终有图运行增加
  Q 反向环绕与 E 恢复检查，见 `runtime-final-render.log`。
- 连续两次 Worker BeforePublish 失败并同时替换动作：Rifle 保持到成功帧，
  成功后发布 Bow；Motor 不重复积分，见 `failure-prop-switch.log`。
- 携带 Bow 的 generation 回调退役、持有未提交帧时停用恢复通过，见
  `generation.log`、`deactivation.log`。普通键鼠回归 360 帧通过，见 `keyboard.log`。
- 十角色 Single / Parallel 各 3621 帧，新增逐角色道具身份检查通过。
  两种模式 pose `CEC4EC705E945A65`、root `E030B6049AEDDCE1`、result
  `8406F373DABD4C3A`，与上一批一致。分别有 50 接受、20 替换、20 取消、
  10 完成；见 `ten-single-final.log` / `ten-parallel-final.log`。

有图专项保存各 Overlay 横移、停下瞄准两阶段截图，并逐项检查 8 种持物姿态。
例如 `prop-0700-Rifle.png`、`prop-1000-Bow.png`、`prop-1060-Bow.png`、
`prop-1300-Binoculars.png`、`prop-1420-Box.png`、`prop-1540-Barrel.png`。
测试场景使用单独近距离相机和补光；正式 Demo 相机和灯光未随测试改写。
未见明显脱手、反向挂接；这不是全地形、全动画过渡的人工观感验收。

复现：

```powershell
dotnet build GodotALS.csproj -p:Optimize=true
& '<Godot-4.7.2-console.exe>' --headless --path . res://scenes/tests/overlay_prop_smoke.tscn -- --single
& '<Godot-4.7.2-console.exe>' --path . --resolution 1280x720 res://scenes/tests/overlay_prop_smoke.tscn -- --render-props
```

## 首错与覆盖边界

最初编译检查错误地查 physical bone，已改成逻辑 VB + 物理父骨。首次 exporter
对 package path 求 ID，已改为完整 object path；错误 ID 的生成文件保留在
`bow-raw-wrong-id.json`，没有作为有效资产提交。导入变换诊断首次在树初始化
前读取 GlobalTransform，修正为 deferred 检查，首错日志保留。
首次 Godot 构建 Environment 名称歧义已修正。首轮十角色测试假设整个角色
只有一个 Skeleton，加入道具后该断言失败；已把暂停姿态检查限定为 VisualWorker
身体骨架，重跑通过。首错 `ten-single.log` / `ten-parallel.log` 保留，其退场
回调报错是测试退出后的次生错误，不作为成功证据。

仍未关闭整个 Overlay gameplay / ALS 清单：源 RagdollStart 清物、RagdollEnd
重新装备的规则已导出，须随真实 Ragdoll 玩法接入与认证；当前只完成普通姿态
持物、曲线驱动及现有停用/退役/失败边界。后续还有剩余 Notify gameplay、
Roll/Mantle 碰撞安全 Root Motion、Ragdoll/Get-up/Pose Recovery、完整 ALS Camera。
起停滑步、换髋、上半身与复杂地形仍须全流程观感验收；新的同代故障恢复在
移动平台上的专项、完整图当前版本认证、十分钟性能预算也仍未完成。

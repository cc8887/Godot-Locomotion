# 原始 PhysicsAsset 导出与导入定义

本批在 `D:\GodotALS` / `main` 基于 `d3a8cfd` 推进真实 Ragdoll 的数据前置。
新增 `assets/config/v4_physics_asset_inputs.json` 和 `AlsPhysicsAssetCompiler`，
尚未创建 Godot 物理骨架，也未改变普通 Demo 的物理所有权；不能据此关闭 P6。

## 已取得的原生数据

UE 5.9.0 直接加载当前源工程资产，记录两套网格各自的 PhysicsAsset 绑定。
原始 `ALS_AnimMan_CharacterBP_C` CDO 实际使用 AnimMan / AnimMan_PhysicsAsset，
mesh-to-character 为原生相对变换。当前 Godot locomotion profile 使用 Mannequin，
后续必须消费 Mannequin 的物理资产，不能因为共用骨架就混用 AnimMan 的参数。

| 网格 | 物理体 | 关节 | 碰撞形状 | 显式排除对 | 原生体质量之和 |
| --- | ---: | ---: | --- | ---: | ---: |
| Mannequin | 19 | 18 | 15 capsule、5 box、1 sphere | 45 | 82.17038989067078 kg |
| AnimMan | 21 | 20 | 17 capsule、2 box、2 convex、1 sphere | 47 | 81.48245322704315 kg |

质量之和包括 kinematic root，不应解释为所有模拟体的动态总质量。
每套保留 79 个参考骨骼（包含虚拟骨骼），消费时按名称绑定实际目标骨架。

导出内容包括：

- 原生顺序 body / constraint 索引、骨骼绑定、完整参考局部姿势。
- Sphere、Box、Capsule、Convex 的尺寸、局部变换、质量贡献与碰撞模式、
  RestOffset；Convex 顶点和三角索引。导出支持 tapered capsule，但当前两套
  资产没有此类型，导入端遇到该类型明确拒绝；其他未支持几何也不能静默丢弃。
- 反射读取 BodyInstance 非 transient / deprecated 配置，包括阻尼、质量覆盖、
  惯量缩放、重力、solver iteration、CCD 等；另记录 PhysicsType 与有效物理材质。
- 在独立无 tick 的 reference-pose physics world 创建真实 UE bodies，读取
  FBodyInstance 的质量、主惯量、质量局部坐标系和 body component transform。
  这不是依据形状体积重新猜算的质量，也不是运行中角色姿势/速度 oracle。
- 两端关节局部 frame；原生 ConstraintInstance（含 angular offset、线性/摆动/
  扭转限制、软限制、投影、质量调节、驱动配置）；命名 constraint profiles。
  当前源资产的命名 constraint profiles 为空。
- 原始 CollisionDisableTable，按索引排序。UE 按 key 是否存在禁用碰撞，
  不以 map 保存的 bool 判断，因此导入保留 membership 语义。

值得后续消费特别处理的原生事实：两套 root 都是 Kinematic，pelvis-root 的
线性与角度自由度均 Free；不能给它错误添加固定锚点。AnimMan 两个足部 convex
有 `(1, 1.2249667644500732, 1)` 非均匀缩放，不能按单位形状重建。
Capsule length 是圆柱段长度，完整总长还包含两端 radius。

所有数据保留 UE cm / kg / degrees，惯量是 kg·cm²。进入 Godot 时仍须显式
转换网格局部轴系、单位和惯量坐标系，包含质量坐标系旋转；只交换惯量三个数
不能代替正确的惯量张量变换。未宣称 Godot 求解器与 Chaos 逐帧数值相等。

## 可重复导出与构建

新增 `AlsGodotExport -PhysicsAssetOutput=<new absolute file>` 和普通 Editor 的
`tools/unreal/export_physics_assets.py`。Python 使用 `ALS_PHYSICS_OUTPUT`，
普通 Editor 通过 `-ExecutePythonScript` 调用并由引擎自动退出；脚本不直接
quit_editor。拒绝覆盖既有输出，不保存 UE 资产。

按 `ue-diagnosing-plugin-build-load` 技能运行完整项目 Editor Target 构建及
插件闭包审计（ALS / AlsGodotExporter / AutoTestTools / BlueprintLisp）。
没有执行过时的 leaf BuildPlugin 部署脚本。新增 PhysicsCore 模块依赖；没有新
插件所有权依赖。审计状态指纹：
`04D434666CE69AE72B99E7D47504C0DE60B370BADE4F85EAD6F6271FC1A713FB`。

最终 NullRHI 冷 commandlet 导出退出 0，0 error / 1 warning（源工程既有
PawnActionsComponent 缺失）。最终默认 D3D12 Editor 使用原生 runner 管理退出，
导出和进程退出均为 0；D3D11 的原生 runner 退出用例也通过。冷启动、
D3D12 Editor、D3D11 Editor 的各次输出与仓库文件 SHA256 全部一致：
`C5B51449FD46390524CA6C10ED95FB4DE9E6E8E0B1C61A3A8AADD4099F8AF847`。
新文件独立于既有 manifest / raw animation 哈希链，没有重写既有动画导出。

D3D12 Editor 导出成功且日志结束为 Exiting，但进程码为 `-1073741819`。
对照启动仅运行 quit_editor、不调用本批物理导出，同样返回该异常码；D3D11
首次正常退出，但最终重建后的 D3D11 立即退出用例也返回该异常码。此证据说明
异常不依赖本批导出调用，也不局限于 D3D12，尚未定位退出异常根因。
两种普通 Editor 初始化均可见既有 `LogAutomationTest: Error: Condition failed`；
不宣称 Editor 日志完全无错误，也不将改变 RHI 当成已修复退出问题。
检查本地 `EditorPythonExecuter.cpp::FExecuterTickable` 后确认，原生 runner
要求脚本返回并经过完整 Editor tick，随后自行延后 QUIT_EDITOR；脚本内直接
quit_editor 绕过了这个时序。最终脚本移除直接退出和冗余的等待回调，使用原生
runner；`editor-native-exit.log` 对应默认 D3D12 退出 0，数据不变。此验证证明
最终导出流程通过，尚不能证明所有历史访问异常都由同一处时序导致。
D3D11 日志另有旧导航网格、Engine LineSetComponentMaterial 的 SM5 编译警告等，
本批普通 Editor 启动不作为场景视觉验收。首次导出还发现本批临时世界缺少
World Context 的清理警告，已补创建/销毁 context 并重新完整构建和审计。
最终 `cold-final.log` / `editor-native-exit.log` 均未再出现该清理警告。

## 导入与验证

`AlsPhysicsAssetCompiler` 产出原生物理定义，检查 schema/单位、有限数字、
名称/索引、父先子后的骨架、刚体质量/惯量、形状尺寸/凸包索引、关节树、
端点引用、排除对范围、必需 pelvis / spine_03。原始驱动与配置仍完整保留，
后续物理消费者必须逐项解释；当前不等于所有 Chaos 设置均已映射到 Godot。
绑定目标骨架使用 FName 一致的不区分大小写名称，拒绝重名或缺失骨骼。

证据在 `artifacts/physics-assets-20260920/`：

- Godot 优化构建通过，0 警告 / 0 错误。
- 项目 DataValidation 退出 0，0 error / 3 warning，涉及既有
  PawnActionsComponent 和旧版 RecastNavMesh；最终记录 `data-validation-final.log`，
  未保存或改写这些资产。仓库没有本批必须执行 UE 打包的规则。
- Import Release 2303 通过 / 1 既有跳过，包括本批 17 项专项。
- 两套所有 body 的 UE-created transform 与独立组合参考骨骼姿势相符；质量、
  惯量、形状、软限制/驱动、不同目标骨架顺序/大小写绑定通过。
- 14 类损坏输入在创建物理体前拒绝，包括错误单位、零质量、负惯量、缺几何、
  未支持形状、非法缩放、缺骨骼、关节端点/自环、越界排除/凸包索引、缺配置
  和重复网格。
- 本批未修改 Core 动画运行算法，未重跑旧 Core 全集或宣称新的十分钟性能证书。

首轮 C++ 构建暴露 Unity Build 匿名命名空间函数重名及 TObjectPtr 类型推断错误，
已用独立命名空间与明确指针类型修正。首轮 Import 构建发生旧元数据定义类型
重名，已区分新 `AlsRagdollPhysicsDefinition`；专项初次拒绝真实足部凸包缩放，
已支持并原值保留。原失败日志/测试记录均保留，不放宽普通刚性 frame 限制。
技能引用的额外 systematic-debugging / verification-before-completion 技能在
本机目录未找到；使用完整构建日志、最小启动对照和运行结果完成直接诊断。

## 下一步

建立 Godot 物理骨架消费：以实际 Mannequin 网格骨架绑定原始局部几何，处理
多个 shape、质量中心/惯量、root kinematic、关节坐标系与自由度及碰撞排除。
随后将已经提交的动画姿势交给 Main 物理所有者、继承角色速度，并消费落地/
RollingInAir 的 Ragdoll 触发记录；再接骨盆追踪、Get-up / Pose Recovery、
道具清理重装备。Mantle、完整 Camera、复杂地形/起停/换髋观感和最终十分钟
性能验收继续保持完整范围。

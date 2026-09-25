# 世界接触注册与真实几何查询

本批在 `${env:GODOT_ALS_ROOT}` / `main` 把持续接触接入共享身体求解循环，
并新增 Godot Jolt 几何查询适配器。普通角色入口尚未切换。

## 实现范围

`AlsContactRegistry` 为固定身体拓扑分配形状槽、revision 和 body generation。
替换、移除后复用形状槽会使旧 handle/历史失效，身体复用须显式 RebindBody。
过滤包含 Enabled、两端均同意的 layer/mask、身体对禁碰，以及同身体排除。
这些是当前 Core 契约，不代表已移植完整 UE collision channel 矩阵或 Godot 原生过滤规则。
求解中锁住注册表，几何回调也不能改变拓扑。

`AlsWorldContacts` 在预测姿态上依固定槽顺序查询每个合法形状对，将持续历史、
Gather、位置和速度行串起来。两端都使用真实逆质量和惯量，与关节共用同一组
DP/DQ/速度缓冲。多个接触对影响同一个身体时，下一对可见前一对的修正。
动态身体将 actor-local shape 转成 COM 局部姿态；固定身体保留 actor frame。
查询空流形清空该对历史，过滤造成观测步间断时拒绝复用旧历史。

身体状态和所有接触历史先完成验证/Stage，随后仅通过无回调的交换与复制发布。
查询、求解、Stage 失败则 Abort，身体状态、已提交历史和世界 epoch 不前进，允许同一步重试。
`IAlsIslandContacts` 增加 StageCommit/Commit/Abort，旧无状态 provider 默认无需实现。
Commit 成功 Stage 后不得失败，Abort 须幂等且不抛异常。

`AlsGodotContactQuery` 使用单独、不进行动态积分的 PhysicsServer3D space。
空间中每次仅放入一个静态目标 shape，再调用 CollideShape 获得完整接触点对。
这是因为该 API 的点对不附带 collider identity；单目标查询保证每个点都属于已知注册对。
目标代理仅供几何查询，**不会把动态目标在 Core 中当作无限质量**。

支持球、旋转盒体、胶囊与凸包，先做变换后的 AABB 排除，再调用真实 Jolt 窄相。
Godot 米/坐标转换使用既有 Foot IK 对应关系，传给 Core 的几何为 native cm。
普通点由穿透点对差得到法向，退化点使用同一目标的 GetRestInfo 法向。
查询多请求一个点用于检测容量溢出，超过容量抛错，不截断流形。
Shape.Changed 或 revision 不匹配时拒绝旧绑定，要求 Replace 后重新 Bind。
形状资源借用，owner 负责释放；适配器释放自身查询参数、事件订阅、body/space RID。
所有 Godot 查询、绑定和释放要求主线程。

## Godot 实测

场景：`scenes/tests/physics_core_contact_smoke.tscn`。
使用 Godot 4.7.2 Mono `ed1daf0bf`，每种频率在真实 physics callback 中执行：

- 半径 50 cm 的球与固定盒体地面初始重叠，恢复到至少 49.9 cm 高度，固定地面不移动。
- 两个动态球相向运动，检查两端响应、至少 99.9 cm 分离距离和每步总动量。
- 两个动态球加固定地面，验证多接触对共享身体缓冲；下球恢复高度且上下球分离。

| Hz | 场景 × 每场步数 | 累计接触点 | 窄相查询 | 动态对最大动量差 |
| --- | --- | --- | --- | --- |
| 30 | 3 × 60 | 63 | 181 | 0 |
| 60 | 3 × 60 | 63 | 181 | 0 |
| 120 | 3 × 60 | 63 | 181 | 0 |

另有六项启动检查：四种形状与地面的真实点/法向及各自容量溢出、
凸包变更失效和重绑定、非主线程拒绝。旋转盒/凸包点仍落在目标面，目标局部法向朝上。
最终三个频率进程均退出 0，日志无 Godot ERROR。
命令模板（report 必须为新的绝对路径）：

```powershell
& 'Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe' --headless --path . res://scenes/tests/physics_core_contact_smoke.tscn -- --hz=60 --report=./artifacts/physics-contact-world-20260921/world-60-probe.json
```

原有 144 组原生关节对子 × 12 帧回放通过，最大位置差 1.020660e-6 cm、
旋转差 3.576279e-7 rad、线速度差 4.722374e-5 cm/s、角速度差 8.397188e-6 rad/s，
代理坐标转换差 3.586463e-7 m。这里只保护原有无接触原生关节结果，不声称新窄相等价。

## 回归与故障记录

新增六项 Core 测试：不同质量双向响应/动量、第二次查询失败回滚、
所有历史 Stage 后失败回滚、过滤/旧 handle/代次/重入修改拒绝、非零且旋转的 COM frame、
预热后连续 2048 个 Core world step 零托管分配。
Core 单测几何源为解析球；上面的 Godot 运行使用真实 Jolt 查询。

Release 固定 JIT（DOTNET_TieredCompilation=0、COMPlus_TieredCompilation=0）：
Core **2675 通过**，保留既有 P5A Golden/TraceSchema 过滤；
Import **2359 通过、1 既有条件跳过**（普通 Editor LayerBlending 环境变量）。
Godot 优化构建 0 warning / 0 error。本批未改 UE 插件或重新导出资产。

实现中三次编译失败分别暴露 Godot 泛型 Array 没有 Length、不能直接 using、
转非泛型 Array 必须显式转换；按安装的 API 修正为 Count 和显式拥有底层 wrapper。
首轮 60 Hz 数值探针虽通过，但清理时把 query.Shape 置 null 导致三条 Godot ERROR。
已改为直接 Dispose 自有查询参数，最终构建和三频率复跑均干净。保留失败日志，不用首轮作通过证据。

产物目录：`artifacts/physics-contact-world-20260921/`。
最终日志为 `godot-build-probe.log`、`world-60-probe.log/json`、`world-30.log/json`、
`world-120.log/json`、`godot-pairs.log/json`、`core-full.log/core.trx`、`import-full.log/import.trx`。
早期编译与清理失败日志也留在此目录。用户 P4 规划修改保持未提交，哈希未变。

## 未完成与下一步

当前固定容量、预分配全部形状对且 O(shape²) 遍历，是正确性基础实现；尚无空间索引/岛发现。
Core 热路径零分配不涵盖 Godot 返回数组产生的主线程分配，也不是十分钟性能验收。
当前世界使用统一已解析材质，没有材质组合规则；只处理显式注册且绑定的形状，
不会自动发现普通场景碰撞体、外部动态物体或完整角色 PhysicsAsset。
Jolt 提供窄相，因此不能称完整 Chaos 1:1 物理轨迹，已有原生参考只证明各 Core 阶段。

下一步外力/重力与资产整链接触绑定，然后睡眠/唤醒、运动 kinematic、CCD 及整链落地验收。
普通 Ragdoll、Get-up/Pose Recovery、Mantle、完整 Camera 和最终十分钟性能预算仍需推进。
本批没有普通画面验收，也没有关闭旧 Jolt 高速落地失败。

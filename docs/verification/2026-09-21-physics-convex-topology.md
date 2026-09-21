# 原生烘焙凸包与查询形状对齐

本批直接在 `D:/GodotALS` 的 main 实施，未创建新项目副本。普通 demo 尚未切换到 Core 物理后端。

## 发现与修改

- 原导出 `FKConvexElem.VertexData` 是源数据。AnimMan 两只脚分别有 225/227 个源顶点，而 `CreatePhysicsMeshes` 后 `GetChaosConvexMesh` 返回的原生凸包均为 **128 个顶点、215 个面**，最长面环 5 个顶点、margin 0。Mannequin 没有凸包。此前两端碰撞输入并不一致。
- 新增 `-PhysicsConvexTopologyOutput=<新绝对路径>` 冷导出，保存原生顶点、面法向/面上点、原次序面环、局部完整变换、源顶点/索引和身体/形状身份；不保存 UE 资产，不自行重建面。
- Import 校验 schema/单位、mesh/PhysicsAsset、body/bone/shape/convex index、完整局部变换、源几何逐值相等和完整覆盖。过期资产、重复/遗漏身份、坏面索引拒绝加载。
- Core 保存不可变拓扑，校验有限值、单位法向、范围、面绕序、支持平面和边配对状态。实际原生面环不是严格共面，最大偏离约 0.1 cm；原生 `Chaos/Convex.h` 构造和 SimplifyGeometry 的 MergeFaces 距离容差为 1 cm，因此保留原数据，不将顶点投影到平面。支持平面外侧仍使用 0.001 cm 校验。
- 两只脚各有六条非正常配对边（含单次与三次使用）；`HasClosedOrientedEdges=false` 明确记录，不凭空修补。未来要求闭合面图的算法必须检查该属性。现阶段只把原生烘焙顶点交给 Jolt 查询形状，**没有声称 Jolt 重建后的面图、首次接触次序或轨迹等价**。
- Core 查询绑定强制使用已验证 cooked 数据，禁止退回源顶点；按现有 native→Godot 轴变换和 cm→m 转换，烘焙局部缩放。非零原生 convex margin 尚不支持并明确拒绝。旧冻结、无碰撞 BodySet 可视代理仍使用源几何，本批没有改变旧 Jolt 模式。

## 数据与原生构建

`assets/config/v4_physics_convex_topology.json`：235286 字节，SHA256 `565DAEA6239F6DB216F43486E9B9858FA000D30E3225F50B6677CAB4019B13A5`；冷重导字节一致。主仓库与 UE 插件镜像的三份 C++ 文件哈希一致。

UE 5.9 完整 Editor target 构建和所有项目插件审计通过；最终 build-state fingerprint `5E3C64937B70E3CE3F7F4A2CD50000E9599E59448EE62356DEE78D4A370D4253`。DataValidation 退出 0，保留三条既有资产/导航警告。

普通 Editor 冷启动两次均成功加载 exporter class 并执行 Python 标记，但正常退出日志关闭后进程均返回 `-1073741819`（0xC0000005）；两条既有 `LogAutomationTest: Error: Condition failed` 仍出现。未发现本轮新的项目 Crash 目录或 Application 1000 事件，尚未定位退出异常。**普通 Editor 重启退出门禁未通过**，不能将本批 UE 验证称为全通过。保留 `editor-restart.log` 和 `editor-restart-repeat.log`，不继续重复启动以挑选成功结果。

Core Release 固定 JIT、集合串行回归 2750 通过（按仓库约定排除 AlsP5aGoldenTests/AlsP5aTraceSchemaTests）；新增 Import 定向 6 项通过，Import 全量串行 2377 通过、1 项既有跳过。均退出 0。

过程失败保留：首轮 C++ TObjectPtr 的 auto 指针推导失败，修正为 Get()；首轮导出错误使用仅 position/rotation 的序列化函数，缺失 scale，产物可恢复地移到 `initial-missing-scale.json`；修正为现有完整 transform 序列化后冷导。首次几何校验的共面/闭合假设也被真实资产拒绝，已根据原生源码与数据改为上述契约，而非修改原生资产。

全部日志：`artifacts/physics-convex-topology-20260921/`。

## Godot 整链回归

优化构建通过，零警告/错误。30/60/120 Hz 各 **545 精度、9 几何、5 流形、5 休眠、3 动态场景**通过；新增两只脚全部顶点逐点坐标转换验证，原捕获脚底接触六个姿态/次序变体继续通过。

保持原门槛与迭代数；落地 10 秒、平台 24 秒，要求各链休眠并保持至少 1 秒。整链仍为 **9/12**，本批未关闭剩余三项失败。

| 场景 | Hz | 结果 | Mannequin / AnimMan 休眠帧 |
|---|---:|---|---|
| 平移平台 | 30 | 通过 | 初始 91/260，停后 451/439 |
| 旋转平台 | 30 | 失败 | 初始 151/239，停后 459/未睡 |
| 平移平台 | 60 | 通过 | 初始 92/437，停后 877/865 |
| 旋转平台 | 60 | 通过 | 初始 91/462，停后 875/1058 |
| 平移平台 | 120 | 通过 | 初始 170/839，停后 1723/1720 |
| 旋转平台 | 120 | 通过 | 初始 170/839，停后 1742/1826 |
| 普通落地 | 30 | 失败 | 未睡/66 |
| 高速落地 | 30 | 通过 | 52/53 |
| 普通落地 | 60 | 通过 | 530/160 |
| 高速落地 | 60 | 通过 | 322/319 |
| 普通落地 | 120 | 通过 | 263/217 |
| 高速落地 | 120 | 失败，Mannequin 太迟 | 1173/1080 |

普通落地 30/60/120 Hz 最大锚点误差分别为 2.056136/0.927088/0.309230 cm；高速分别为 7.585929/2.134277/0.865801 cm。形状修正影响 AnimMan，但没有凸包的 Mannequin 普通 30 Hz 不睡和高速 120 Hz 太迟问题仍在。旋转 30 Hz 最后角速度 0.169697 rad/s，比上一批 0.126296 增大；不能把数据对齐当作稳定性全面改善。

## 后续

仍需原生凸包首次接触生成/次序、边缘裁剪、分离几何与 cull 激活/失效；然后关闭三项整链失败，再接普通 Ragdoll 的唯一 owner、pelvis/胶囊/相机跟随、Get-up/Pose Recovery。Mantle、完整 Camera 和最终十分钟性能预算等总清单仍未完成。

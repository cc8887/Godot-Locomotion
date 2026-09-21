# 原生面裁剪与接触缩减

在主目录 `D:/GodotALS`、main 实施。上批 `f0b724c` 修正了脚部 cooked 顶点，本批补齐 **GJK/特征选择之后** 的多边形接触处理。完整 GJK、首次选面、凸包运行时接管与原生完整轨迹仍未完成。

## 实现

`AlsConvexFaceManifold.Build` 接受已选定的 reference/incident 面环、局部相对变换、参考平面、shape1 法向和形状次序：

- 按原面环顺序逐边 Sutherland–Hodgman 裁剪；双缓冲 stackalloc，最多 32 点，保留原生容量截断与剩单点时停止规则。
- 使用原生 float `UE_SMALL_NUMBER` 提升为 double 的边界容差、交点公式、SafeNormalize 小边回退 X 轴及 1e-4f 长度平方阈值。
- 恰好四点交换 1/2；超过四点按最深点、投影最远点、最大绝对三角形面积、最大正面积扩张选择四点，保持原生严格比较与平局次序。按 `TRotation::FromRotatedVector` 原逻辑转到 Z 再转回，包括其平行分支。
- 生成局部投影点和 incident 局部点，保留输入的接触法向，支持 reference 为 shape0/shape1。入参检查在写出之前；无托管逐帧分配。
- 现有受保护 box-box 内部面路径复用此流程，但其 **未裁剪** 的 incident 点继续直接使用精确局部顶点，不做多余逆变换。该路径仍拒绝边缘/深穿透/选面平局，未扩大其支持区域。

本批没有用 Jolt 法向猜测原生 GJK 支持点，也没有把合成面参考的输出灌入运行时。脚部凸包目前仍由 Jolt 查询产生接触；新面流程将在原生特征选择完成后接管。

## 原生参考

新增 `-PhysicsFaceClipOutput=<新绝对路径>`，新进程中由 UE `UpdateConstraint` 原生生成 prism-box 初次接触，无预置接触/持续缓存。3/4/5/8/16/40 边棱柱 × 六个盒面 × 三个倾角 × 三个横向偏移 × 两个间隙，共 648 组，cull=3 cm。

输出同时提供棱柱 cooked 底面环与已定义盒面输入。`planeCase` 仅筛选原生实际返回 VertexPlane 且法向吻合的场景，不证明 GJK 选面等价。360 组参与逐点同序比较（六个方向各 60），288 组属于空/其他特征或法向场景而排除，**不计通过**。360 组中 144 组横向偏移、240 组超过四点缩减，最大裁剪点数 32；最终 point0/point1 最大 float 差 **0 cm**，正反形状身份传输均通过。正反验证复用同一已选特征，不是重新运行反序 GJK 的证明。

资产 `assets/config/v4_physics_face_clip_reference.json`：1922750 字节，SHA256 `D606B15F67036145CAB01A069CB66C838F2B3C0A8C029AD5A552277F50411F6B`；重复冷导字节一致。旧 432 组盒体参考继续通过。

## 过程失败与修复

- 原生首编误把 MakeImplicitObjectPtr 的基类指针当具体凸包使用，改为 GetObject；随后 TBox 内联 accessor 引用非导出私有静态表导致链接失败，改为显式描述测试输入盒面。参考输出始终来自原生求解器，未修改引擎。
- 首轮负 Z 场景错误依赖 FromRotatedVector 的反向平行分支，实际没有翻转到预期面；场景构造改用明确 X 轴 PI 旋转。旧产物移到 `initial-antiparallel-fixture.json`，首轮失败日志保留。Core 中该旋转函数的原生行为仍按源码保留。
- 最初接入盒体时，未裁剪顶点通过逆变换回局部造成 cm double→float 中点舍入噪声，30 Hz 平移/旋转初始休眠回归，首轮整链 8/12。恢复未裁剪点的精确局部坐标，新增 200 姿态 midpoint 回归后，最终整链回到 **9/12**。

最终整链文件为 `final-*.json/.log`，不能使用首轮同名无前缀产物代表最终结果。九份成功报告与上批凸包批次对应报告字节完全相同；三个旧失败仍为 30 Hz 旋转停后不睡、普通 30 Hz 不睡、高速 120 Hz 睡太迟。本批未改变阈值、迭代数或验收时长，普通 demo 未切换。

## 构建与验证证据

日志目录 `artifacts/physics-face-clipping-20260921/`。Godot 优化构建零错误/警告，三频率接触探针各 545 精度/9 几何/5 流形/5 休眠/3 动态场景通过。十二项整链及接触探针在最后一次修正后复跑。

UE 全 Editor target 构建、所有项目插件审计和 build-state 通过，最终 fingerprint `72B83D9E60362F3E805AA2790BBD235864BA8F325A05554BD3C5992FA0D52106`；主仓库与 UE 镜像三份文件哈希一致。DataValidation 退出 0，0 error/3 既有 warning。

Core Release 固定 JIT、集合串行 2754 项通过，按仓库规则排除 AlsP5aGoldenTests/AlsP5aTraceSchemaTests。新回归覆盖越界裁剪、空交集/无部分写出、多点缩减零分配及盒体 cm/float 中点。

Import Release 固定 JIT、集合串行全量 2378 项通过、1 项既有跳过；Core/Import 均退出 0。

普通 Editor 成功加载 exporter class 并执行 `ALS_FACE_CLIP_EDITOR_RESTART_OK`，但日志正常关闭后进程仍返回 `-1073741819`（0xC0000005），两条旧 Condition failed 继续出现。与上批同类退出异常，尚未定位；未反复启动挑选通过结果，普通重启退出门禁仍失败。

## 下一步

原生 GJK/EPA 支持点、`SelectContactPlane` 所需 vertex-plane 邻接和距离筛选仍缺失；不能仅取全局最对向面替代。应先补齐这些原生几何输入与选择，再将 cooked 脚凸包接入已验证的裁剪流程；继续分离几何/cull 和三项整链失败，然后普通 Ragdoll、Get-up/Pose Recovery、Mantle、完整 Camera 与最终十分钟性能验收。完整目标保持未完成。

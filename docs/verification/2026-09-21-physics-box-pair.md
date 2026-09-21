# 盒体与凸包完整初次流形

直接在 . 的 main 分支推进。本批把原始/缩放凸包和盒体统一到泛型多面体初次流形入口，并实现带 margin 的边投影与投影后 cull。没有接入运行时查询或普通 demo，未重跑十二项整链，最新仍 9/12。

## 实现

AlsPolygonManifold 使用受约束的值类型适配器，保留 GJK/EPA、选面、第二侧参考面偏置、裁剪、四点缩减与输出/缓存暂存发布。原来的 RawConvexManifold 和 ScaledConvexManifold 入口转发到此实现。

AlsBoxPolygonShape 使用 UE 的二进制顶点编号、六面顺序、面顶点绕序、缓存面顺序与 MaxAxis fallback 平局规则。盒体以中心为局部原点、Half 为 cm；偏移由相对变换承载。传入的 Margin 是已按碰撞双方状态解析的 pair margin，不自动读取 Godot Shape.Margin。

只要任意一侧 polygon margin 大于零，边接触就投影双方（包括零 margin 凸包）到选中面最近边；使用投影后的 Phi 再执行 cull。保留第二侧边点，第一侧沿 GJK 法向重新投影。凸包最近边使用 float，盒体使用 double；边遍历从末顶点到首顶点开始，严格小于更新最优边。

本批同时修正上一轮缩放精度边界：

- Chaos TVector 混合类型乘法的返回类型取左侧，而且先把右侧转换成左侧类型。
- wrapper GetVertex 使用 float MScale 在左，因此裁剪顶点应舍入至 float 再提升 double。
- wrapper 最近边使用 float MInvScale 在左，输入点应先舍入 float，再做 float 乘法。
- 前一处遗漏导致本批 12 组裁剪点误差超过既定 1e-5 cm；初次失败日志 reference.log 保留。没有放宽门槛。第二处遗漏修正后剩余边点差也降至零。

Core 测试覆盖选面缓存顺序与 fallback 平局差异、最近边顺序和端点钳制、零/非零 margin 的真实面位置、失败不发布、预热后的 1000 次盒体查询零分配。初次单位测试对首条边的预期写错（-X 面末顶点 2 到首顶点 0 位于 -Z 边），按 Box.cpp 编号修正，未修改算法；旧失败 unit.log 保留。

## 原生参考

新参数 -PhysicsBoxPairOutput=<新绝对路径>，通过真实原生约束执行 Collisions::UpdateConstraint。原始 feet、缩放 feet、零 margin box、0.2 margin box 组成五类有序组合，每类 1296 组：raw/box、scaled/box、box/raw、box/scaled、box/box，总计 6480。box Half=(8,5,3) cm；scaled feet 的 wrapper float Scale=(1.5,0.8,1.2)。记录实际 pair margin 和原生配置。

采用 GenericConvexConvex 分发。源代码中的 ConstructBoxBoxOneShotManifold 也调用同一个 ConstructConvexConvexOneShotManifold；本批不把 generic dispatch 声称为整个原生碰撞调度已移植。

TBox::SStructureData 不对 DLL 导出，初次直接读取 GetVertexPlanes3 链接失败。最终使用 Box.cpp 相同六面定义，调用原生 FConvexHalfEdgeStructureDataS16::MakePlaneVertices 并导出八个缓存。该部分验证的是同一原生构造函数的结果，不是读取私有静态对象；独立于此，全部流形期望均来自真实原生 box 实例。

所有行均参与比较，未按结果排除：

- 3751 空，370 边接触，632 第一侧参考面，1727 第二侧参考面。
- 点数、点序、接触类型一致，float 输出点与法向最大差均为 0。
- 219 组带非零 margin 的边接触、3 组边投影后二次 cull，均有计数断言。
- 从 float 点重算 Phi 最大差 1.0375976557952526e-6 cm，门槛仍为 1e-5 cm。
- 旧 raw 1296 和 scaled 5184 组继续通过，点和法向差均为 0；scaled 旧有微小点差归零，重算 Phi 最大差 2.010016023845651e-6 cm。

资产 assets/config/v4_physics_box_pair_reference.json：6790163 字节，SHA256 1C9E3CF6A0C37C00AF95933C4680E1CBC58585F236ABADF37DDA5FA4F08F0A8C；首次与重复冷导退出 0、字节一致。日志 artifacts/physics-box-pair-20260921/reference-final.log 为最终数值对照。

## 构建与 Editor 异常

沿用 ue-diagnosing-plugin-build-load 技能执行完整 Editor target 构建和插件审计。最终 fingerprint 2CA4FD2C1C80DC24565FE0E59010F5F1789C7A323D4DB03FB8F0096C55FAD67E，构建日志前缀 20260921T140248274Z-00e3caf620de471185fa83dea09736e1，三个原生文件与 UE 项目镜像哈希一致。

首轮 LNK1104 暴露上一批 Editor PID 3632 仍持有 exporter DLL。虽然此前 GetExitCodeProcess 返回 0，但 tasklist /m 仍显示模块、独占打开也失败；因此该退出码不能证明已释放资源。taskkill 未成功，核对命令行为本任务无保存诊断后，通过原生 TerminateProcess 清理，WaitForSingleObject=0，后续 DLL 链接恢复。随后遇到上文未导出静态符号 LNK2001，改用原生结构构造函数并再次完整构建后成功。未结束用户启动的其他 Editor。

本批普通 Editor 使用有界等待的诊断启动器：加载 marker ALS_BOX_PAIR_EDITOR_RESTART_OK 成功，但进程实际退出码 3221225477，即 0xC0000005。两条旧 Condition failed 仍存在；重启门禁失败，未称异常修复。退出后 tasklist /m 无 exporter 占用。本批没有把启动/退出问题归因于本次几何代码。

Core Release 固定 JIT、集合串行全量 2792 通过（排除既定 AlsP5aGoldenTests/AlsP5aTraceSchemaTests）。Godot 优化构建 0 error/0 warning，DataValidation 退出 0、0 error/3 既有 warning。日志在 artifacts/physics-box-pair-20260921/。

Import Release 固定 JIT、集合串行全量 2391 通过、1 既有跳过，退出 0。发现并修复缩放舍入遗漏后，三套完整流形参考的点/法向断言进一步收紧为精确相等，避免以后再把同类精度边界错误藏在容差内；重算 Phi 保留原门槛。

收紧断言后定向重跑三套参考全部通过，日志 reference-exact.log；其余代码未再改变。

## 未完成与下一步

这是 polygon 初次流形，不是完整运行时。凸包适配器仍只接受零 inner/pair margin；已覆盖的非零 margin 来自盒体一侧或双方盒体。非零 margin FConvex 的 SupportCoreScaled 尚未移植；capsule/sphere 不能直接套用此 polygon 接口。raw/scaled 混合凸包对的适配器可组合，但本批未增加该组合的原生参考。

下一步把真实 cooked 拓扑、shape-local cm、原始双方顺序、运动状态与 pair margin 接到查询 owner；补持久 GJK 缓存的 revision/失败回滚/生命周期和分离 cull，再重跑整链并修复剩余三个休眠失败。普通 Ragdoll/Get-up/Pose Recovery、Mantle、完整 Camera、十分钟性能预算、EPA 特殊退化和极端缩放原生对照仍未完成。

接入检查定位：AlsPhysicsContactShapes.Bind 已拿到 cooked 拓扑，但调用 query.Bind 时只传 Shape3D，需保留原生拓扑、源尺寸及缩放；不能再从 Godot float 几何反推。AlsGodotContactQuery.Query 目前会为旧 interior-face 路径交换双方，native polygon 路径必须在这个交换之前保持原始 pair 次序。IAlsContactGeometrySource 当前仅有 PrepareStep/Query，没有 StageCommit/Commit/Abort 配套回调；新增跨帧 GJK 缓存前应补齐并由 AlsWorldContacts 的事务统一管理。

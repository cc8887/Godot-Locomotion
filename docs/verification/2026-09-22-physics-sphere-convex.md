# Sphere–Convex 原生移植与运行时接入

本批在主目录main实现Core球体—凸包流形，替换显式原生sphere/convex绑定的Jolt查询回退。普通demo仍未接入实验物理，完整角色目标未完成。

## 实现与数据

- 依据UE本地 `SphereConvexContactPoint.cpp` 移植point-to-full-hull GJKDistance：epsilon .001、最多16轮、深接触按最大有符号平面距离回退，不用GJKPenetration/EPA或零长胶囊替代。
- 保留完整凸包支持点、忽略wrapper support margin；球比凸包包围盒大时按最反向面、面顶点步长和RaySphere距离补点，最多4点。结果在暂存后统一发布；参数、容量和非有限结果拒绝。
- 确认UE球心float加法边界：旋转后的偏移先转float再与float球心相加。原先double求和造成最多3.8147e-6cm点差；修正后原生对照点、法向、Phi全部精确一致。
- 新增原生凸包属性导出：质心、包围盒、完整有序顶点；Import逐顶点绑定现有cooked拓扑，不能把另一凸包属性混入。scaled质心float乘法和bounds double缩放也逐项对照。没有重写旧拓扑/资产文件。
- Godot绑定传入这些属性，正反序sphere-convex均走Core，按原有detector取cull；缺少属性拒绝，保留其他尚未支持的组合回退。

新UE命令参数为 `PhysicsSphereConvexOutput` 与 `PhysicsConvexPropertiesOutput`，输出必须为新绝对路径。参考包含两脚raw/instanced/scaled、实际微缩放、非均匀/反射缩放、大小球、正cull和远原点旋转。

扫描3240条记录，其中球体不受原先capsule tilt轴影响，同一1080组几何各重复3次；不将其称为3240种独立配置。1128无接触、1032单点、1080四点，点数/点序/点/法向/Phi全匹配。

## 运行结果：第二步差异尚未解决

原生几何对照通过不代表世界已一致。当前Godot与保存的同初态原生世界比较：

| AnimMan | 修复前最大线速度差 cm/s | 本批最大线速度差 cm/s |
| --- | --- | --- |
| 第1步 | 0.0001530494 | 0.02149901996 |
| 第2步 | 105.3558044 | 105.3521973 |
| 第3步 | 95.6177872 | 95.6140254 |

第二步大幅突变仍在，第一步差异增大；不能声称已解决root→foot_r问题，也不能把Sphere–Convex缺失认定为唯一原因。Mannequin的采样速度差与上批相同。普通30场景末秒V7.439918/W.428579未变，最大锚点1.884308cm、接触累计21134。

整链矩阵仍 **9/12**：60/120Hz四场景全部通过，30Hz高速通过，普通/平移/旋转失败。平台两项依旧运动开始前AnimMan未睡，Mannequin已睡；没有放宽阈值或新增场景失败，但不能宣称成功场景数值未变。

## 验证

- Core Release固定JIT串行2837通过；Import同配置2431通过、1既有跳过，均退出0。
- 新参考定向默认/LatestMajor roll-forward各通过，点/法向/Phi最大差均0。Godot优化构建0警告0错误，30/60/120Hz现有contact smoke全部通过；这些旧smoke不是新增球凸包专门查询验证，真实整链和导入参考承担本批覆盖。
- UE完整Editor目标构建审计通过，fingerprint `F308C962DA07F4A5A8BA8E67E3F8044007887080C968EE38C7DFE086155DF4E7`。首轮因FSphere名称歧义及float顶点显式转换编译失败，修正为FImplicitSphere3后完整重建；C#首轮Math命名空间冲突也已修正。失败日志保留。
- 首次和冷重复导出退出0且字节一致。参考4,942,580字节，SHA256 `4FBDCB036A1D1BA0229DED0DF1C4F78B55748773EF314903D74BA4E8157699F2`；属性24,639字节，SHA256 `A0F821641A05E41855B92C3BECF8224BB2207C850D82C81D3D2A8E25ABC1F766`。
- 既有输出负例退出52，未生成新的属性输出，不覆盖原参考。
- DataValidation退出0、3旧警告；普通Editor7208加载标记成功、原生退出0、DLL已释放。两旧Condition failed仍存在，既往间歇退出AV未修复。
- canonical和UE镜像源码一致；用户P4规划哈希保持 `78EEA1410FF3EBC93A5EAEAA224ABFD74565B54090DBA3670C08A37BED60B100`，不纳入提交。

日志、重复导出、十二矩阵与真实帧捕获位于 `artifacts/physics-sphere-convex-20260922/`。诊断capture和matrix使用不同日志名。

## 下一步

优先导出原生完整世界第1–3步实际参与求解的接触对、点和禁用状态，核对root/脚接触是否相同，以及原生过滤、历史复用与激活边界；现有world参考只有接触对数量，不能用同输入coupled通过代替实际接触集合验证。继续更长稳定性和三个30Hz门槛调查，再普通Ragdoll/Get-up/Pose Recovery、Mantle、完整相机与十分钟性能预算等总目标。

# 项目工作目录

- Godot 项目的主目录是 `D:\GodotALS`，主分支是 `main`。
- 按用户要求，日常修复和后续实现直接在此目录推进；不要为每次修改创建新的项目副本或 worktree。
- `D:\GodotALS-p3-direction-alignment`、`D:\GodotALS-p4-pose-foot-placement`、`D:\GodotALS-p5a-events-actions` 是历史工作目录，已完成内容应汇入主目录。保留其中尚未迁移的诊断产物，不擅自删除。
- 提交时只纳入本次工作，保留用户未提交的修改。当前本地资产在 `assets/generated/als_v4`，被 Git 忽略，不能把仅有代码的检出当成可运行交付。
- 导出 JSON 之间有文件字节级哈希依赖，遵循 `.gitattributes`，不要统一格式化这些资产。

# 运行和验证

- 最新平台原生基线：`docs/verification/2026-09-22-physics-platform-baseline.md`。setup支持平台前十秒/显式phase，保留场景两个kinematic，原生每帧验证ObjectState；平台未开始运动。两模型×平移/旋转30Hz原生全未睡（V8.45–8.97cm/s），Core则M已睡/A未睡；不能称轨迹等价，亦不能仅凭十秒休眠失败断言移植遗漏。新参考冷重导一致，旧普通30输入重导字节不变；旧输入无kinematic仍静态化远处平台，边界保留。三组参考默认/roll-forward各3通过，Godot构建/60smoke/UE全Editor审计/DataValidation过；Editor28676本次退出0/DLL释放，两旧Condition和间歇AV未修。未改Core/阈值，矩阵仍9/12，普通demo未接。下一步更长原生/Core稳定性和最早分歧，coupled自由关节适配；普通角色完整总目标仍未完成。

- 最新低频原生基线：`docs/verification/2026-09-22-physics-low-frequency.md`。相同实际初态/13静态盒体，原生普通30Hz也未达十秒休眠：AnimMan159睡/保持142，Mannequin300帧仍18动态身体醒着，末秒V6.98827/W.403521；Core同样M未睡，V7.43992/W.428579。冷重导字节一致，新fixture逐帧重算睡眠/速度并保留原生失败；新旧world两项默认/roll-forward各通过。未改生产/UE/门槛，未重跑全量或其他矩阵，仍9/12；不能仅以此睡眠失败判定移植遗漏，也不能宣称轨迹等价。下一步平台30启动前固定阶段原生同初态，再最早分歧/更长稳定性；普通角色总目标及旧Editor异常仍未完成。

- 最新完整原生世界：`docs/verification/2026-09-22-physics-native-world.md`。真实初态/13 静态盒体重建独立 Chaos，普通60/高速120两模型均满足休眠，冷重导字节一致；非 Core 轨迹等价。发现并补回两套 root→pelvis 全自由无驱动关节图边：ConnectivityOnly 不求解/不投影但参与排序，38连接。Core2837/Import2427+1旧skip、Godot优化构建/三频率smoke通过。整链最新 **9/12**：普通60/高速120恢复，普通30新增休眠回归，平台30两项启动前AnimMan未睡。UE全Editor审计/DataValidation过，Editor13304加载成功但退出0xC0000005，两旧Condition failed未修。普通demo未接。下一步失败30Hz完整原生初态基线与最早分歧，核对float dt/particle/岛调度；新38关节coupled捕获需先适配旧严格测试工具。再三失败及普通Ragdoll/Get-up/Pose Recovery、Mantle、相机、十分钟预算等总目标。所有最终代码留主目录。

- Actual history批次普通Editor16452加载成功/原生退出0/DLL释放，两旧Condition failed与间歇AV未修；本批详细范围见下条。

- 最新actual history：`docs/verification/2026-09-22-physics-actual-history.md`。主目录只读pending诊断补旧saved/检测点/assigned/求解后ratio，包含空活跃对；51帧573对1007点，UE独立Activate/SetSolverResults重导字节一致。.NET8/9锚点差0、410相邻发布一致，含57新点24滑动3空流形。Core相关30/Import相关10各运行时通过，Godot构建与60smoke通过，三失败采集诊断与上批一致；未重跑全量/十二矩阵，基线仍2835/2424+1skip/8of12。本批不改公式。原生资格判定和摩擦结果仍是捕获输入，非世界等价；下一步相同实际资产/初态完整UE落地休眠基线，核对四失败与岛/调度。UE全Editor审计与DataValidation通过；普通demo未切换，全部角色剩余目标保留。

- Raw Gather批次最终Import Release固定JIT串行全量2424通过/1既有跳过，退出0；其他本批范围及旧失败见下条。

- 最新raw Gather：`docs/verification/2026-09-22-physics-raw-gather.md`。六个失败静止帧156对369点（全已有锚点，27对共享initialPhi）捕获真实post-history/pre-Gather输入，UE独立Gather冷重导字节一致。查明.NET9 Vector3.Cross舍入；显式float叉积后.NET8/9及Godot三个频率各369点原生差0，保留旧捕获1.0662403e-6偏差。Core2835通过（并修上一批friend契约遗漏），.NET9相关8通过，Godot旧smoke全过。整链仍8/12，八成功报告均改变；普通60 M600才睡/高120 M881 A未睡/平台30两失败未关。全Editor审计与DataValidation过，普通Editor34788加载后0xC0000005/DLL释放，两旧Condition failed未修。下一步真实历史准备前/首次落地/平台低频帧间对照，普通demo未切换，全部角色总目标保留。Import全量结果见验证文档后续记录。

- Resting coupled批次Editor9288加载标记后原生退出0/DLL释放；两旧Condition failed和间歇AV未修。完整Editor审计与本批对照范围见下条。

- 最新resting coupled：`docs/verification/2026-09-22-physics-resting-coupled.md`。捕获高120 AnimMan1140–1142/普通60 Mannequin540–542六帧144共同阶段，原生冷重导一致。.NET8/9四套coupled各4通过；新实测.NET9/Godot最大DP1.1452e-7cm、V4.1587e-6cm/s、W5.1555e-7rad/s，非逐位相等。测试原先错误用严格手工行接口拒绝已Gather的NdotU残差，现内部GatherRows配合Import.Tests friend保留生产语义；公共Gather仍严格，测试验证失败不发布。Core相关87、优化构建、60Hz完整smoke通过；未重跑全量/矩阵，基线仍Core2834/Import2422+1skip/8of12。两失败采集诊断与上批一致，四失败未关。下一步补Gather前几何/锚点/initialPhi/shape姿态/速度快照，让UE重新Gather；本批对照不证明历史/Gather正确。普通demo未切换。

- Native mixed批次最终Import串行2422通过/1既有跳过；普通Editor28076加载后0xC0000005退出/DLL释放，两旧Condition failed与间歇AV未修，普通重启门禁失败。其余覆盖见下条。

- 最新native mixed trace与运行时修复：`docs/verification/2026-09-22-physics-native-capsule-mixed.md`。旧高120失败246capsule pair（30活跃36点）+114capsule-convex（全无点）原生重放，实际dynamic/凸包fingerprint-scale-margin严格匹配。查明Godot宿主.NET9.0.17与默认测试.NET8的Vector3.Cross差异；胶囊内显式float叉积后Core原生差0，保留修复前source偏差证据。三个频率Godot内各246实际输入精确回放，旧smoke全过；.NET9定向4及ReferenceTests41通过，Core2834。整链仍8/12，八成功报告都有数值变化，普通60 M595睡太迟/高120 A未睡 M869/平移30/旋转30失败未关。后续核对其他Gather/contact/linear浮点叉积与历史，再完整角色目标。普通demo未接。
- 诊断trace和整链matrix必须使用独立日志名；本批临时high-120 trace被后续矩阵同名日志覆盖，受检360条原始source在新参考JSON完整保留，详见验证文档。

- Native capsule trace批次UE最终全Editor审计/重导/DataValidation通过；Editor33244原生退出0/DLL释放，但两旧Condition failed及间歇AV未修，其他证据与范围见下条。

- 最新actual capsule trace：`docs/verification/2026-09-22-physics-native-capsule-trace.md`。trace补局部点/法向/NativePhi/实际cull；新exporter从真实身体精确匹配并复制FCapsule，避免重构轴浮点变化。高120失败1140–1145帧726查询中270capsule-box（24活跃54点）原始Godot输出及Core重放与UE均精确一致；456其他类型明确跳过，非完整轨迹证明。冷重导一致、两负例拒绝且无输出、定向4通过、Godot构建通过；Core/Import生产逻辑未改未重跑全量。高120仍失败且诊断指标不变，矩阵沿用8/12。继续同批246capsule-pair/114capsule-convex及历史/Gather，再四失败与完整角色目标，普通demo未切换。

- Capsule-convex批次最终Import Release固定JIT串行全量2419通过/1既有跳过/0失败，退出0；Core未修改未重跑，其他覆盖及边界见下条。

- 最新capsule-convex：`docs/verification/2026-09-22-physics-capsule-convex.md`。真实两脚raw/instanced/scaled及实际margin共4320原生样本，全部点数/点序/点/法向/Phi精确一致、冷重导字节一致；Core通用公式未改。显式capsule/cooked绑定接Core零support margin路径与实际detector、反序，每步查询不恢复polygon。优化构建/三频率新增各48及全部旧smoke通过。整链仍8/12，普通30仅接触累计11219→11220其余字段不变，另外七成功报告字节一致；普通60/高120/平移30/旋转30四失败未修。UE全Editor审计/重导/DataValidation通过，Editor33892本次原生退出0/DLL释放，但两旧Condition failed与间歇AV未修。普通demo未接，继续sphere混合/actual trace及四失败，再完整角色目标。

- Capsule退化批次最终Import Release固定JIT串行全量2418通过/1既有跳过/0失败，退出0；整链8/12及Editor旧退出异常边界见下条。

- Capsule退化批次普通Editor33572加载标记成功后0xC0000005退出，DLL释放，两旧Condition failed仍在；普通重启门禁失败。其他进展见下条。

- 最新capsule退化修复：`docs/verification/2026-09-22-physics-capsule-degenerate.md`。原生9072样本确认140组各一NaN补点；Core只省略distance恰0的无定义补点，保留最近点/其他有效点，邻居不放宽epsilon。全部有限点逐序/点/法向/Phi精确一致，旧7056精确且旧原生重导字节不变，新重导一致。关闭上一批原子拒绝导致整步中断的缺口；明确为原生未定义值稳定处理，不称复制NaN。Core2834、优化构建、三频率各120步动态/固定连续求解及旧smoke全过。整链仍8/12且八成功JSON字节一致，普通60/高120/平移30/旋转30四失败未修。UE全Editor审计/冷重导/DataValidation过；普通demo未接。继续capsule-convex/sphere混合、actual trace及四失败，再完整角色目标。

- Capsule pair批次最终Import Release固定JIT串行全量2417通过/1既有条件跳过，退出0；原子退化拒绝与8/12边界见下条。

- 最新capsule pair：`docs/verification/2026-09-22-physics-capsule-pair.md`。float相对空间、动态半径归属、同向化/最近点/深穿透和对齐补点；原生7056有序点数/点/法向/Phi精确一致、重导字节一致，修正共用reciprocal计算差。显式native pair必须PrepareStep提供动态归属，共用cull每帧查。Core2833+退化定向4、优化构建、三频率smoke新增各20及旧项过。整链 **8/12**：普通30恢复M82/A138睡；普通60（M596才睡，比上批574晚）、高120、平移30/旋转30四失败未修。已知补点恰落另一轴distance0：Core原子拒绝非有限法向，未有专门UE对照/未修，不能声称全输入稳定。UE全Editor审计/重导/DataValidation过，普通Editor2032本次退出0/DLL释放，两旧Condition failed/既往AV未修。普通demo未接。继续该退化、capsule-convex及sphere混合/actual trace，再四失败和完整角色总目标。

- 完整capsule-box批次最终Import Release固定JIT串行全量2416通过/1既有条件跳过，退出0；其他验证与剩余五失败见下条。

- 最新完整capsule-box：`docs/verification/2026-09-22-physics-full-capsule-box.md`。冷同空间box/segment GJK/EPA+半径+原生轴线裁剪/endcap/去重，显式native绑定接入原leaf/反序/实际detector；旧744（含原排除300）+新8640独立参考全部有序点/法向/Phi精确一致，冷重导字节一致。Core2829、优化构建、三频率smoke新增各24项及旧项过。整链 **7/12**：高60恢复（M161/A289睡，末秒V/W0，anchor1.798296cm），其他旧成功保留；普通30/60、高120、平移30、旋转30五失败未修。UE全Editor审计/导出/DataValidation过；普通Editor34476标记成功后0xC0000005，两旧Condition failed，普通重启门禁失败。普通demo未接；generic Core不代表capsule-convex已验证。继续混合对、原始native_capsule实际pair trace适配及五失败，再普通Ragdoll/Get-up等总目标。

- Sphere-box 批次最终 Import Release 固定JIT串行全量：2414通过/1既有条件跳过，退出0；其他验证和旧失败边界见下条。

- 最新 sphere-box 完整窄相：`docs/verification/2026-09-22-physics-sphere-box.md`。原生8064例/6516有接触，点数/点/法向/Phi精确一致、冷重导字节一致。保留内部平局/极近表面回退；double Phi严格<cull后存float，接触点乘法double。显式sphere/box绑定接Godot原leaf中心、反序和实际detector，每步查询不恢复polygon。Core2826、优化构建、三频率smoke新增各30项+旧项过；整链仍6/12，六成功JSON与上批字节一致，原六失败未关闭。UE全Editor审计/冷重导/DataValidation过，普通Editor33988加载标记成功但退出0xC0000005，两旧Condition failed仍在，DLL释放；普通重启门禁失败。普通demo未接。下一步capsule边缘/深穿透/混合及actual pair trace适配，再六失败、普通Ragdoll/Get-up等总目标。

- 最新primitive真实几何：`docs/verification/2026-09-22-physics-primitive-geometry.md`。原生32capsule/2sphere/7box的端点/轴/半径/box bounds导出并严格绑定旧runtime快照，重复字节一致且旧runtime重导不变。胶囊局部变换已烘入float端点；registry统一observed leaf，Core接原生起点/轴/高度/半径，Godot代理单独居中转向并回写原leaf点/法向，box用native half。trace类型native_capsule/native_sphere避免旧centered-Z导出器误读；旧capsule trace exporter未支持新类型。Core2823、Import2413+1旧跳过、优化构建及三频率smoke旧检查+2primitive通过；最后高30trace报告字节一致。整链仍 **6/12** 无新失败，高30M77/A88睡；普通30/60、高60/120、平移30、旋转30未过。UE全Editor审计/冷重导/DataValidation过，普通Editor20896本次退出0/DLL释放但两旧Condition failed和既往间歇AV未修。下一步sphere-box完整窄相、胶囊边缘/深穿透/混合与实际pair原生对照、六失败，再普通Ragdoll/Get-up等总目标；普通demo未接。

- 最新胶囊分离接入：`docs/verification/2026-09-22-physics-capsule-cull.md`。Godot已有guarded capsule-box算法由固定cull0改用实际detector/whole-body bounds/PreV；正反序、静止拒绝/速度扩展及quadratic每帧查询新增10检查，三频率smoke各通过（旧551/9/68/3/5/5保留）；既有744原生参考定向1测试通过（444支持/300排除）。未改Core/Import/UE，未重跑全量/UE门禁。整链最新 **6/12**：高30恢复（M79/A127睡，末秒V/W0，anchor5.01755cm）；普通60/M572、高60/M561睡太迟新增两回归，另普通30/高120/平移30/旋转30未过。日志final_speed是末秒最大值，不能误称睡后瞬时速度。下一步实际primitive尺寸/变换、sphere-box完整窄相和capsule边缘/混合分离、历史/tolerance及六失败，再普通Ragdoll/Get-up等完整目标。普通demo未接，旧Editor AV与两Condition failed未修。

- 最新实际接触设置：`docs/verification/2026-09-22-physics-contact-settings.md`。原生隔离世界观察 maxPushOut=1000/restitutionThreshold=1000（旧0/2000）；40实际身体overlap=-1，constraint Setup/Activate解析max(双方,0)=0（旧Gather禁用-1）。48动态/kinematic box组合精确对照，严格Import/WorldContacts身体数组副本接入；不把legacy solver depenetration=1e10误用。Core2822、Import2408+1旧跳过、Godot优化构建/60smoke通过。整链最新 **7/12**：高30不再第9帧穿地、跑完300帧但AnimMan未睡/anchor6.6665cm；高120改为AnimMan未睡；普通30/旋转30仍失败，平移30新增启动前Mannequin不睡回归。三种60及普通/平移/旋转120过。新trace/6-8帧六捕获在artifacts/physics-contact-settings-20260922。UE全Editor构建审计/冷重导字节一致/DataValidation过；普通Editor32060 marker成功后0xC0000005，DLL释放，两旧Condition failed未修。下一步五失败、quadratic分离/真实primitive/tolerance/历史，再普通Ragdoll/Get-up等完整目标；普通demo未切换。

- 最新 polygon 分离距离：`docs/verification/2026-09-21-physics-polygon-cull.md`。严格导入既有原生 detector 与完整 particle bounds，动态步前/kinematic当前/static零速度驱动 cull；显式 box/convex 查询和恢复共用距离，新接触以原始 NativePhi<=cull 激活并随事务提交。24624 原生 polygon case 新增 Phi 精确相等；Core2819、Import2406+1旧跳过、Godot优化构建及三频率 smoke（551/9/68polygon/3事务/5流形/5睡眠）通过。整链最新 **8/12**：高速120新增 Mannequin 休眠回归，另普通30/旋转30休眠和高速30第9帧穿地未修，不能沿用上批9/12。普通demo未接；quadratic分离/CCD/MACD/完整midphase退役/native tolerance未齐。下一步观察并传输实际 Gather 初始重叠/solver设置：UE constraint Setup 将双方初始穿透速度与0取max，当前默认-1禁用尚未对齐，勿用同Gather后输入对照证明完整链正确。再四失败/普通Ragdoll/Get-up等总目标。无UE改动/新导出/重启，本批未修旧Editor AV和两Condition failed。

- 最新非零凸包margin：`docs/verification/2026-09-21-physics-convex-margin.md`。原生8960支持点/编号/delta精确一致，11664非零instanced/scaled/box流形点/法向0差；GJK保留每侧delta跨fallback，polygon支持解析后margin。实验query绑定实际wrapper margin并按PrepareStep inverse mass解析动/静pair，ConvexZeroMargin观察值0，cull仍0。Core2816、Import全量2404+1旧跳过、后加高速coupled定向3通过，三频率smoke551/9/5/5/3/59通过。整链 **9/12**：高速120恢复，剩普通30/旋转30休眠及高速30第9帧穿地。高速30第7/8帧有正确上表面3点，6状态144共同求解阶段原生对照通过，不能据此证明Gather/发现/历史正确；捕获在artifacts/physics-convex-margin-20260921/high30-capture。下一步分离cull、历史/Gather初始输入及其他primitive实际几何，再三失败/普通Ragdoll等完整目标。UE完整构建审计/重导/DataValidation过，普通Editor marker成功但0xC0000005退出、两旧Condition failed未修复，DLL已释放。普通demo未接。

- 运行时形状批次最终回归：Import Release 固定JIT串行全量2402通过/1既有跳过；旧PhysicsAssetOutput冷重导与原始输入字节一致。整链仍8/12及高速30穿地回归，详见下一条。

- 最新运行时形状：`docs/verification/2026-09-21-physics-runtime-shapes.md`。真实外部particle导出43形状，按userdata绑定；两脚outer margin约.61782cm、inner0，左脚scaled Z=.9999998807907104，右脚instanced；leafLocal均identity。修正实验后端对已烘焙凸包重复应用FKConvexElem缩放/平移，改用实际wrapper scale/leafLocal，Import严格资产快照绑定与bounds验证。定向11、Godot构建、三频率smoke通过；整链仍8/12：30平移恢复，但30高速第9帧AnimMan身体19穿地新增失败，另普通30/旋转30/高速120未过。非零margin与cull仍未接，其他primitive实际几何传输待核对。UE全目标审计/冷重导/DataValidation通过，普通Editor本次退出0且DLL释放，两旧Condition failed和既往间歇AV未修复。普通demo未接，继续实际margin/FConvex支持/分离cull与四项整链失败，再完整ALS目标。

- 最新原生查询接入：`docs/verification/2026-09-21-physics-native-query.md`。实验后端 box/convex 接 Core GJK/EPA 和原始有序流形；源拓扑/尺寸/缩放绑定，精确单位缩放用 instanced 内层几何分支。新增事务式 GJK owner cache、revision/generation/margin 冷启动、失败拒绝提交、显式 Release；尚未接完整 native midphase 退役。Core2806、Import定向4、Godot构建、三频率smoke各551/9/5/5/3/4及60Hz场景检查通过。整链最新 **8/12**，替代旧9/12：新增30平移停止后休眠回归，30旋转提前到启动前失败；另普通30、高速120仍失败，未放宽门槛。当前查询代理margin0/cull0，cooked margin0不能证明实际UE wrapper margin0。下一步导出实际wrapper/margin、非零FConvex SupportCore、pair解析与分离cull，再四项整链失败及普通Ragdoll等完整目标。普通demo未接，无本批UE改动，旧Editor AV未解决。

- 最新几何事务前置：`docs/verification/2026-09-21-physics-geometry-transaction.md`。IAlsContactGeometrySource新增StageCommit/PublishCommit/Abort/Reset，WorldContacts统一驱动，Prepare失败/空步骤/恢复绕过Query均闭合事务；发布与Abort须无异常。修正AlsContactTrace漏转PrepareStep并转发所有生命周期。真实polygon/GJK缓存失败回滚与重试对照、reset/restore测试通过；Core2798、Godot优化构建、60Hz实际smoke（545精度/9几何/5流形/5睡眠/3新增事务）通过。未改UE、未重跑Import或整链；最新9/12，普通demo未接，旧Editor AV未解决。下一步保留query原生几何绑定，实现revision/generation和事务下的GJK缓存，再polygon接管/pair margin/分离cull及三旧失败/普通Ragdoll等完整目标。

- 最新盒体/凸包组合：`docs/verification/2026-09-21-physics-box-pair.md`。统一值类型polygon初次流形，box原生面/邻接顺序、非零box pair margin、双方边投影与二次cull。6480完整原生对照通过，219带margin边/3投影cull，点/法向0差；旧raw1296/scaled5184仍通过且均0差，修正float左乘的缩放顶点与最近边精度。三套点/法向断言收紧精确相等。Core2792、Import2391+1旧跳过、Godot/UE完整构建审计/DataValidation通过，重导一致。普通Editor marker成功但退出0xC0000005，两旧Condition failed仍在；上批PID3632残留DLL通过原生终止释放，不把此前exitcode0当资源已释放。本批退出后无DLL占用。未接运行时/未重跑整链，最新9/12。下一步保留query绑定的cooked拓扑与源尺寸/缩放、原始pair次序，补geometry提交回滚生命周期/pair margin/分离cull后整链；非零margin FConvex、quadratic完整路径、普通Ragdoll等仍缺。

- 最新缩放凸包：`docs/verification/2026-09-21-physics-scaled-convex.md`。新增双方零margin scaled FConvex流形，float缩放/逆缩放与候选面、double裁剪平面、负缩放绕序。5184真实两脚缩放组合全部对齐点数/序/类型，法向差0、点最大差3.8444e-6cm（非逐位一致）；重导字节一致，旧raw1296点/法向仍0且原生重导不变。Core2787、Import2390+1旧跳过、Godot/UE全目标审计/DataValidation通过。普通Editor标记成功、原生退出码查询0，但启动器WaitForExit卡住后仅结束启动器；两旧Condition failed/既往AV未解决，不称重启全门禁通过。未接运行时、未重跑整链，最新9/12。下一步mixed/box/非零margin边投影、查询owner缓存与分离cull，再三旧失败及普通Ragdoll等完整目标；极端缩放fallback/EPA退化原生覆盖仍缺。

- 原始凸包组合批次最终全量：Core串行2784通过，Import串行2389通过/1既有跳过，均退出0。普通Editor本次退出0，两旧Condition failed与既往间歇退出AV未解决；范围见下条。

- 最新原始凸包组合：`docs/verification/2026-09-21-physics-convex-pair.md`。Core新增unscaled原生候选面/fallback，RawConvexManifold串联GJK/EPA、cull、第二侧.002f偏置、边接触与面裁剪，整阶段暂存缓存/输出。1296真实两脚有序组合的原生UpdateConstraint初次流形全部对齐：853空/87边/94第一侧参考/262第二侧参考，点/法向差0，重算Phi最大1.1971e-6cm；重导一致。首轮错误ConvexConvex枚举触发原生ensure空输出，改GenericConvexConvex完整重建后通过，失败产物保留。Core2784、Godot构建、UE全目标审计/DataValidation通过。仅未包装零margin凸包冷初次流形，scaled/box/非零margin边投影/持久owner未接；未改运行时、未重跑整链，最新9/12。继续这些适配/分离cull/三旧失败，再普通Ragdoll等完整目标。

- 顶点邻接批次最终全量：Core串行2780通过，Import串行2388通过/1既有跳过，均退出0；普通Editor失败边界见下条。

- 顶点邻接批次普通Editor：exporter加载标记成功后退出0xC0000005，两旧Condition failed保留。旧间歇退出异常未修复，本批普通重启门禁失败；具体见下条文档。

- 最新原生顶点邻接：`docs/verification/2026-09-21-physics-vertex-planes.md`。cooked拓扑schema2新增GetVertexPlanes3计数/前三槽位，Core不可变保存、有效面关联验证、未用哨兵保留；Import拒绝旧schema/缺缓存。256顶点逐槽一致，7个原生计数不同于扫描面环，禁止反推代替。旧顶点/面/绑定逐字段未变，冷重导一致；576 simplex/1024支持/528 GJK/318 EPA旧对照仍通过。Core2780、Godot优化构建、UE完整构建审计/DataValidation通过。尚未实现选面算法或接入流形，未改变运行时查询/未重跑整链，最新9/12。下一步unscaled/scaled选面与完整流形原生对照，再查询接管/分离cull/三旧失败及普通Ragdoll等总目标。

- 碰撞margin批次最终全量：Core串行2773通过，Import串行2383通过/1既有跳过，均退出0；运行时未接入与旧异常边界见下条。

- 最新碰撞margin：`docs/verification/2026-09-21-physics-margins.md`。Core按双方quadratic/运动状态解析float pair margin，Sleeping仍Dynamic，保留双方零margin时只给第二侧最小值的原生次序。1568实际constraint Setup对照逐值精确相等（含真实两脚），最小margin原生观察0，0/.05进程内扫值、重导一致。Core2773、Godot构建通过；UE完整Editor构建/插件审计/DataValidation/普通重启退出0，两旧Condition failed与既往间歇退出AV未解决。CollisionTolerance未纳入本批，owner参数映射未接，未改运行时/未重跑整链，最新9/12。下一步原生GetVertexPlanes3邻接/选面与GJK/EPA/margin/裁剪衔接，再分离cull/退化对照/三旧失败及普通Ragdoll等完整目标。

- EPA 批次最终全量：Core 串行2767通过，Import串行2382通过/1既有跳过，均退出0；Godot优化构建通过。边界见下条。

- 最新 EPA：`docs/verification/2026-09-21-physics-epa.md`。Core新增原生indexed EPA初始化/面邻接/可见边界扩展/收敛与GJK统一入口，缓存跨GJK+EPA暂存发布，异常不发布可重试，预热盒体零分配。旧原生参考528帧完整contact/身份/缓存对照通过，其中318 EPA帧深度/点/法向差0，最大队列12；256组同MSVC实际STL排序参考覆盖0–1024长度/重复值/分区/堆回退，逐索引一致且重导一致。特殊Degenerate/MaxIterations尚无专门原生对照；点触碰fallback有Core测试。Godot优化构建通过，未改UE插件/未重跑普通重启，上一批0xC0000005及两旧Condition failed保留。尚未接运行时/未重跑整链，最新9/12。下一步退化原生参考、实际pair margin、顶点原生邻接/选面，组合凸包流形并接分离cull，再三旧失败及普通Ragdoll等完整目标。

- GJK 搜索批次最终全量：Core 串行2762通过，Import串行2381通过/1既有跳过，均退出0；普通Editor退出异常与整链9/12边界见下条。

- 最新 GJK 搜索：`docs/verification/2026-09-21-physics-gjk-search.md`。Core 新增 indexed warm-startable 搜索、当前相对姿态缓存恢复、局部 witness 持久化及失败不发布/暂存复制；凸包零 margin、盒体显式 margin。528 原生连续帧缓存点/点序一致，187 恢复，210 无 EPA 帧距离/法向/点差 0、编号一致；318 需 EPA 帧仅验 GJK 缓存，穿透未完成。倒数乘法修正当前 UE /fp:fast 对照的数值分歧，旧576 simplex点差降0，1024支持点/编号仍一致。冷重导字节一致；Core2762、Godot优化构建通过。UE全目标审计/DataValidation通过，普通Editor加载标记成功后仍0xC0000005退出，两旧Condition failed未修复，不能称全门禁通过。未接运行时、未重跑整链，最新仍9/12。下一步EPA、实际pair margin、原生vertex-plane邻接/选面，再凸包接管/分离cull/三旧失败及普通Ragdoll等完整目标。

- 最新 GJK 底层：`docs/verification/2026-09-21-physics-gjk-primitives.md`。Core移植indexed double线段/三角形/四面体约简和witness同步压紧，支持真实cooked凸包零margin支持点/float顶点身份。576原生simplex有效点/身份一致，最近点最大差9.60e-10 cm，权重3.33e-16；1024两脚支持点/编号精确一致，重导一致。冷导UseGJK2=false、GJK/EPA epsilon均float1e-6。Core2757/Import2380+1旧跳过、Godot优化构建通过。UE全目标审计/重导/DataValidation/普通重启退出0；两条旧Condition failed和既往间歇退出访问冲突未解决。未接运行时、未重跑整链，最新仍上批9/12；下一步完整warm-startable GJK循环/缓存恢复、EPA、实际pair margin、vertex-plane邻接/选面，再凸包接管/分离cull/三旧失败/普通Ragdoll等完整目标。

- 面裁剪批次最终全量：Core串行2754通过，Import串行2378通过/1既有跳过，退出0；整链9/12和普通Editor退出异常边界见下条。

- 最新面裁剪：`docs/verification/2026-09-21-physics-face-clipping.md`。Core新增已选reference/incident面后的逐边裁剪、32点缓冲、原生四点缩减与对角次序，盒体内部面复用。648原生prism-box场景，360已选面组逐点同序差0（240缩减，最大32点），288其他GJK结果明确排除；六方向均覆盖，重导一致。盒体未裁剪局部点保留精确值，修复逆变换舍入造成的本批30平台初始休眠回归。最终整链仍9/12，九成功报告与上批字节一致；三频率接触各545/9/5/5/3通过，Core2754通过。UE全目标审计/DataValidation通过，但普通Editor加载标记成功后仍0xC0000005退出，两条旧Condition failed保留。脚凸包仍Jolt查询，GJK/EPA支持点、vertex-plane邻接/选面、分离cull待补，再三旧失败及普通Ragdoll/Get-up等总清单。不得把本批面处理对照当完整凸包或轨迹等价。

- 凸包批次最终计数：Core串行2750通过，Import串行2377通过/1既有跳过，均退出0；普通Editor退出门禁失败与整链9/12边界见下条和对应文档。

- 最新原生烘焙凸包：`docs/verification/2026-09-21-physics-convex-topology.md`。AnimMan 两脚原先误用225/227源顶点；原生cooked均128顶点/215面/margin0。现导出原生面环与完整绑定、Core不可变拓扑、Import严格源资产匹配，Core查询改用cooked顶点，旧冻结代理不变。原生面环存在近似共面和未配对边，显式HasClosedOrientedEdges=false，不能当严格闭合面图。冷重导一致；三频率各545精度/9几何/5流形/5睡眠/3动态通过。整链仍9/12，三旧失败未关闭，30旋转末角速度增大。Core2750通过；Godot优化构建、UE全目标审计和DataValidation通过，但普通Editor两次加载标记成功后退出均0xC0000005，未解决，不能称全门禁通过。普通demo未接新后端。下一步原生凸包首次点序/裁剪、分离几何与cull，再剩余整链失败和普通Ragdoll/Get-up等完整目标。

- 盒体初次流形批次最终全量计数：Core串行2743通过，Import串行2371通过/1既有跳过，均退出0；详细证据与9/12整链结果见下条。本批没有关闭剩余三项休眠失败。

- 最新盒体初次流形：`docs/verification/2026-09-21-physics-box-manifold.md`。Core接管完整位于另一盒面内的box-box四点/原生对角次序，边缘/深穿透/incident face平局回退。432原生场景中216支持、144有点，逐点同序最大点差0；冷重导一致。Godot144规范化正反传输通过，三频率各545精度/7几何/5流形/5睡眠/3动态通过。整链现9/12（平台5/6、落地4/6）：30平移回归与普通120旧失败关闭；剩30旋转停后AnimMan不睡、普通30 Mannequin不睡、高速120睡太迟。cull仍0，凸包面拓扑/边缘裁剪/原始反序reference bias/分离发现未齐。Core串行2743通过，UE全目标审计/冷重导/普通重启/DataValidation通过，重启两条旧Condition failed未解决；完整记录见文档。普通demo仍未切换，后续继续分离几何/三项失败，再普通Ragdoll/Get-up/Pose Recovery及完整目标。

- 当前验证计数（原生检测距离批次）：Core 串行2739通过；Import串行2370通过/1既有跳过；Godot优化构建通过。日志在 `artifacts/physics-cull-reference-20260921/`。本批未重跑整链，不将这些结果当五项休眠失败已关闭。

- 最新原生检测距离：`docs/verification/2026-09-21-physics-cull-reference.md`。新增原生 FParticlePairMidPhase 观察导出，324组scale/distance与Core逐值精确相等；两次冷导字节一致。项目隔离世界推进后实际detector为基础3 cm、速度倍率1、额外最多3 cm，allowMACD=true但对照明确非MACD。既有40身体完整原生bounds最大边长均<100 cm，scale=1。源码继续确认动态PreV取上帧，kinematic在ApplyKinematicTargets后PreV等于本帧目标速度，静态为0；新增选择helper，尚未用于Godot query。UE全目标构建/插件审计/重导/DataValidation通过；普通Editor重启退出0但两条旧Condition failed仍保留。十二项整链未重跑，最新仍7/12；普通demo未切换。下一步接入分离几何、距离驱动的激活/失效，并补盒/凸包首次点序，再关闭五项整链失败及继续Ragdoll/Get-up等完整目标。

- 最新分离检测前置接入：`docs/verification/2026-09-21-physics-contact-cull-context.md`。Island→WorldContacts→Geometry PrepareStep 传递已提交身体状态，不用本帧重力后的 V 代替 PreV；旧 Gather 缺失 previous 时明确为空，依赖该输入的 provider 应拒绝。失败注入包装器同步转发。新增 whole-particle bounds 缩放/非MACD速度扩展 helper，尚未接 Godot query、尚无新 UE 运行时参考。Core 串行全量2738通过；并行旧流形零分配断言1656 bytes失败原因仍未定位。Godot构建和实际世界60 Hz三场景/13几何/9生命周期通过。最新整链仍上批7/12，五项失败未关闭；下一步导出实际detector参数、完整body bounds与kinematic PreV映射，接分离几何及缓存激活/失效，再整链验收。普通demo未切换，完整目标仍未完成。

- 最新接触 shock 与盒面修复：`docs/verification/2026-09-21-physics-contact-shock.md`。按原生图层级在末 3 次位置/末 2 次速度迭代缩放低层动态端质量/惯量（0.77），只刷新 normal mass，保留切线缓存与累计量；不修改共享刚体质量。648 组原生接触和 5 状态/120 阶段整链对照通过，重导字节一致。实际小腿盒体误选地板底面及微分离跳过近面已复现并修复；三频率各 401 精度/7 几何/5 流形/5 睡眠/3 动态检查通过。最终整链 7/12：平台 4/6，落地 3/6；30/60 Hz 高速落地旧失败关闭，但 30 Hz 平移休眠回归，30 Hz 旋转及普通30/120、高速120仍失败。最终产物 current-*，普通120用separator-normal-120，不能用过程结果替代。Core2736通过；Import串行2369通过/1旧跳过，并行零分配偶发失败仍未定位。Godot/UE构建审计、重导、普通重启、DataValidation通过，重启两条旧Condition failed仍保留。下一步优先30 Hz平台回归、原生分离发现/cull与首次盒/凸包点序；普通demo未接新后端，Ragdoll/Get-up/Pose Recovery、Mantle、完整Camera及十分钟性能预算未完成。

- 最新流形保留：`docs/verification/2026-09-21-physics-manifold-restore.md`。盒/凸包按原生相对位姿/横向漂移/至少4支持点规则恢复，保留最近窄相基准；球/胶囊排除。几何与摩擦历史共同提交/回滚，身份/漏帧/dirty/cull退出失效。72组576帧UE原生恢复对照全过，289恢复/287重建/28停用点，点与phi差0，重导一致。Godot60新增5恢复生命周期+旧367精度/7几何/5睡眠/3动态通过；整链仍平台5/6、落地1/6，总6/12，六旧失败未关闭，普通120末秒V增至2.532747cm/s。Core2730、Import2367/1旧跳过、Godot/UE构建审计/普通重启/DataValidation通过。查询仍cull0，完整窄相初次点序/分离发现/动态shock未齐；普通demo仍未接新后端。下一步上述接触缺项，再普通Ragdoll owner、pelvis胶囊相机、Get-up/Pose Recovery。

- 最新胶囊几何：`docs/verification/2026-09-21-physics-capsule-manifold.md`。实际前臂UE对照证实点位置/次序差异，非少点；Core接管保守盒面内部capsule流形，0/1/2/3点及原生cylinder/end-cap规则，边角/深穿透/零长度仍旧查询。744参考中444支持区域逐点同序通过，重复导出一致；Godot336正反传输、三频率367精度/6几何/5睡眠/3动态通过，最终60加零长度共7几何。平台现5/6：30平移由失败转通过，30旋转前段睡但停后不睡；60/120平移旋转过。落地仅普通60过，30/120四项仍失败；高速60回归M592睡太迟未保持一秒。Core2719、Import2366/1旧跳过、Godot/UE构建审计重启DataValidation通过。查询仍重叠cull0，未接原生分离接触/完整窄相/保留/shock，不能称轨迹等价；普通demo仍未切换。下一步盒/凸包接触点次序与跨帧保留、cull及动态质量缩放，再普通Ragdoll/Get-up。

- 最新整链同输入对照：`docs/verification/2026-09-21-physics-coupled-step.md`。生产路径增加只读、可选逐阶段采样；六个30/120 Hz实际状态、144阶段的接触/关节/投影原生容器对照通过，含休眠失败前帧。UE使用捕获的Gather后输入重新计算，不覆盖窄相/持续接触/积分/休眠/shock；不得称完整轨迹等价。重复导出一致，Core2712、Import2365/1旧跳过、Godot构建/旧144对子/60 Hz接触探针通过，UE全目标/审计/普通重启/DataValidation通过。30平台和120实际普通采样仍失败，前批四落地/两平台未关闭；未改生产公式或阈值。下一步真实形状原生接触生成/保留及动态接触层级质量缩放，再普通Ragdoll owner、pelvis胶囊相机、Get-up/Pose Recovery。普通demo仍未接新后端。

- 最新约束排序：`docs/verification/2026-09-21-physics-constraint-order.md`。Core 图快照分层与持续加入 key 排序完成，16 组 UE 原生岛图对照通过且重导一致；完整链诊断接入事务式接触/关节排序，位置/速度/投影同序。平台60/120 Hz平移/旋转四项通过（60 Hz A77/M88自然睡眠），30 Hz M左臂仍不睡。重要回归：实际World普通/高速30和120 Hz四项未过休眠，只有60 Hz两项过；120高速M1182才睡未保持一秒。前批十二项全过不再代表当前诊断后端。3 cm查询距离候选使残余速度增大，代码撤回、distance日志保留。Core2706、Import2364/1旧跳过、Godot构建、旧144对子与60 Hz接触探针通过。UE全目标构建/审计、重导、DataValidation通过；普通重启首轮关闭日志后访问冲突，复跑退出0，未定位首轮异常。下一步同一整链状态的UE接触/关节共同迭代对照，原生接触生成/保留与动态接触质量缩放，然后普通Ragdoll世界owner、pelvis胶囊相机、Get-up/Pose Recovery。普通demo未接此后端，不得把图规则对照当完整物理轨迹或稳定性通过。

- 最新接触选面修复：`docs/verification/2026-09-21-physics-box-face.md`。实际地板 margin 0.04 m 下，AnimMan foot_l 第53帧 Jolt 返回底面和向下法向，造成第54帧 6.709 cm 锚点异常。只对保守凸包包围盒完全位于盒面内部、远离圆角且有退出距离余量的接触使用该局部平面；有限边界/边角继续原查询，不改场景资源、Core 或睡眠参数。六项真实资产姿态/次序及八项盒面边界新检查通过；三频率各31精度/6几何/5睡眠/3动态，真实场景13几何/9生命周期/3场景通过。实际World与厚地板普通/高速三频率十二项均自然睡眠通过；实际普通120最大锚点由6.709降至0.376757 cm。完整链平台仍仅120 Hz平移/旋转通过，30/60 Hz四项前段休眠失败保留；右臂残余角运动未修复。Godot构建通过，本批未改Core/Import且未重跑全量。下一步右臂接触/关节耦合与必要原生整链对照，再普通动画到物理owner、pelvis胶囊相机、Get-up/Pose Recovery；普通demo未切换。

- 最新世界姿态：`docs/verification/2026-09-21-physics-world-pose.md`。`AlsCorePhysicsPose` 在已提交 FBX local pose 与 native world 身体间双向转换，包含正确局部/世界基区别、COM 速度、轴向角速度、非物理骨骼保持和失败不发布；两模型四姿态 160 身体初始化/192 回写/32 拒绝通过。scene-world 的冻结代理改用显式 worldSpace，旧 native-pair 保留诊断模式。新增完整链平台 24 s 生命周期，六项只有 120 Hz 平移/旋转两项通过（最大中心滞后 0.008858/0.001524 m）；30/60 Hz 在静止十秒阶段未全部入睡，60 Hz AnimMan 右臂平滑角速度 0.067–0.071 > 原生 0.05，未调阈值或延长等待。普通 120 Hz 瞬时锚点 6.709 cm 已定位 AnimMan foot_l 第54帧，根因未解。Godot 构建、旧144对子/BodySet生命周期、实际场景60/120普通回归通过；本批未改Core/Import且未重跑全量。下一步接触/关节阶段定位和原生整链对照，普通动画提交到物理owner、pelvis胶囊相机跟随；普通Ragdoll/Get-up仍未接通。所有失败保留在本批 artifacts，不能宣称平台完整通过。

- 最新场景接入：`docs/verification/2026-09-21-physics-scene-kinematics.md`。Core 支持外部零质量姿态/速度目标、停止清速、运动唤醒和失败重试；Main 绑定实际 scene shape-owner，支持局部偏移、过滤/禁用/资源变更/移除及显式 teleport。Capture→Step→CommitCapture；资源借用不释放，新增拓扑/缩放/动态 owner 拒绝。真实 World 13 身体/13 形状，球体地面/平移/旋转三场景、13 几何和 9 生命周期在三频率通过；两资产实际场景普通/高速三频率六项十秒自然睡眠通过，共 66 身体/36 关节/69 形状。普通 120 Hz 瞬时锚点 6.709 cm，虽 <10 cm 仍待定位/视觉验收。Core 2700、Import 2363/1 旧跳过、Godot 构建、旧 144 原生对子/厚地板六项/接触三频率回归通过。高速以 `high-verified-*` 为准，首轮条件参数未传入产物不算高速。环境材质仍显式统一，无原生 kinematic/整链新轨迹对照；无关平台运动也保守阻止整组休眠，尚无接触图/局部休眠。下一步完整链移动平台、普通 Ragdoll 世界姿态/动画接入及胶囊相机跟随，再 Get-up/Pose Recovery；普通 demo 与旧 Jolt 失败未关闭。禁止把诊断 FBX 代理直接当普通世界姿态使用。

- 最新接触稳定性：`docs/verification/2026-09-21-physics-contact-normal.md`。真实 Godot 红测确认旧点差法向在分离的流形点上翻转，以及大世界 float 坐标导致点数改变；单目标凸对统一读取 GetRestInfo 法向，double 相对原点后再查询。17 项精度、旧六几何/三场景/五睡眠生命周期在三频率通过。两模型普通/高速 30/60/120 Hz 十秒睡眠六项全部通过，原生阈值/迭代次数未变，睡后 pose/epoch 不变；关闭睡眠的六项粗略落地也过。Core 验收锁定轴改测原生 R01 分量的角残差，Limited 保留 pyramid/twist，0.1 rad 门槛不变；报告保留旧 pyramid 指标与原先 60 Hz 0.1055 失败，不能据此重判旧 Jolt。Core 2693、Import 2363/1 旧跳过、Godot 构建及旧 144 对子通过。额外法向查询有 Main 分配/成本，未做最终性能预算。下一步完整原生整链重力/接触对照、运动 kinematic/真实场景接入，再普通 Ragdoll/Get-up/Pose Recovery；普通 demo 与旧 Jolt 失败仍未关闭。

- 最新睡眠进度：`docs/verification/2026-09-21-physics-sleep.md`。Core 可选整组睡眠、显式/外力/接触变更唤醒、失败事务与睡眠期间接触历史保持完成；实际材质阈值来自 40 个原生身体。144 组×60 步原生对子逐帧睡眠/计数一致（3300 睡眠样本、85 次休眠冲量唤醒），重复导出一致，旧参考重导不变。Godot 球体五项睡眠生命周期及旧三场景/六几何检查在 30/60/120 Hz 通过；Core 2689、Import 2363/1 旧跳过、Godot 构建及旧 144 组对子通过。完整角色十秒睡眠门槛三轮均失败：30 Hz Mannequin 左手仍运动，60/120 Hz AnimMan 腿/躯干仍超原生阈值；另一角色入睡后的姿态/epoch 保持通过。不提高阈值或强制定时睡眠。下一步定位接触/流形/共同迭代稳定性并补完整原生重力接触对照与场景接入；普通 Ragdoll/Get-up/Pose Recovery 未完成，普通 demo 未切换，旧 Jolt 失败未关闭。

- 最新重力整链进度：`docs/verification/2026-09-21-physics-gravity-chain.md`。Core Step 接显式重力、加速度/角加速度、冲量速度和原生 drag 顺序，保留 StepForceFree；Godot 从两套实际 PhysicsAsset 绑定 43 个形状，加两地面共 45 形状、42 身体/36 关节，通过冻结代理回写骨架。近法向速度下原生 float 切线残差触发过严断言：仅内部 Gather 接触行允许原生非严格正交结果，直接 row API 仍严格检查。两套角色普通/高速落地 30/60/120 Hz 十秒共六项通过；普通最大锚点 1.485/0.930/0.377 cm，高速 4.841/1.740/0.915 cm，末秒线速度均 <20 cm/s、限位超出 <0.1 rad。仍有角速度、无睡眠/CCD，不是观感或 Chaos 完整轨迹等价；地面厚 1 m，旧 Jolt 失败未关闭。Core 首轮旧 Montage 零分配测试出现 6216 字节，独立与全量复跑通过（2683），原因未定位；Import 2359/1 旧跳过、Godot 构建、144 组原生对子及旧接触探针通过。下一步睡眠/唤醒、持续历史生命周期、完整原生重力/接触对照与场景接入，再普通 Ragdoll/Get-up/Pose Recovery。普通 demo 本批未切换。

- 最新世界接触进度：`docs/verification/2026-09-21-physics-contact-world.md`。固定拓扑 registry 分配 body generation/shape revision，双向 layer/mask 与身体对禁碰；Godot 独立单目标 Jolt query space 提供球/盒/胶囊/凸包真实几何，Core 接管两端动态响应及多个接触对共同迭代。整步历史先 Stage 再无回调发布，失败不推进身体/历史。30/60/120 Hz 各三场景×60 步通过，双动态总动量误差 0；六项几何/容量/失效/主线程检查通过。Core 2675、Import 2359/1 旧跳过、Godot 构建及原有 144 组关节回放通过。此实现为 O(shape²) 预分配与遍历、统一已解析材质、独立查询世界；不是 Chaos 窄相等价或普通场景自动发现。尚无重力、运动 kinematic、CCD、睡眠、完整资产身体接触绑定与整链落地；普通 demo 未切换，旧 Jolt 失败未关闭。下一步外力/重力与资产整链接触，再睡眠及普通 Ragdoll/Get-up/Pose Recovery。

- 最新持续接触进度：`docs/verification/2026-09-21-physics-contact-history.md`。`AlsContactHistory` 完成默认多点复用/可选唯一匹配、quadratic 距离规则、静止/滑动/零摩擦锚点回存、初始深度、禁用点和事务式提交；有序 body generation/shape revision key 隔离历史，标识仍由未来 world owner 分配。`AlsPersistentContactPair` 串联 history→Gather→行求解→回存，维护禁用点索引。192 组×6 帧原生历史阶段对照通过（1715 恢复/2445 新点，最大锚点差 0）；重复导出一致。Core 2669、Import 2359/1 旧跳过，Godot 构建通过。UE 普通重启首轮日志关闭后访问冲突，未定位；相同参数复跑退出 0，详见记录，勿称已修复。下一步世界身体/形状注册与过滤、真实碰撞查询和多个接触对共同迭代，再重力、睡眠、整链落地、普通 Ragdoll；本批未改变 demo 或关闭旧 Jolt 失败。

- 最新 Gather 进度：`docs/verification/2026-09-21-physics-contact-gather.md`。`AlsContactGather` 从 shape-local 几何/姿态/COM/速度生成接触臂、切线、摩擦误差、恢复目标和初始重叠更新；`GatherGeometry` 事务式缓存并供共享关节迭代使用。432 组原始几何→Gather→完整接触阶段对照通过，本机 Gather 差值 0、最大 DP 4.486e-8 cm、线速度 1.222e-5 cm/s；新旧参考重导一致。Core 首轮既有足部零分配失败（2448 字节，原因未定位），独立与全量复跑通过：Core 2655，Import 2358/1 旧跳过，Godot 构建通过。尚未完成稳定 body/shape 身份、持续锚点匹配与真实碰撞 provider；下步先这些，再重力/动态双向响应/运动 kinematic/睡眠/整链落地/Ragdoll。普通 demo 未切换，旧 Jolt 失败未关闭。

- 最新接触进度：`docs/verification/2026-09-21-physics-contact-rows.md`。Core 法向/二维摩擦/速度行与预分配流形完成；288 组 UE 原生 8 轮位置/隐式速度/2 轮速度对照通过，最大 DP 2.261e-8 cm、线速度 9.345e-6 cm/s；重复导出字节一致。`IAlsIslandContacts` 让接触与关节在每轮共用 DP/DQ/速度，解析耦合、失败恢复和重入拒绝通过。Core 2647、Import 2357 通过/1 旧跳过，Godot 构建和原有 144 组关节回放通过。仅行求解及共同迭代接口：接触几何 Gather、body/shape 身份、持续摩擦锚点、真实世界碰撞、重力/运动 kinematic/睡眠仍未接通；普通入口未切换，旧 Jolt 失败未关闭。下一步几何与持续流形，再重力、睡眠、整链落地和 Ragdoll。UE 仅离线参考，继续 C# Core 架构，无引擎 fork。此前关节接入与其他未完成项见下列记录。

- 最新进度：`docs/verification/2026-09-21-physics-core-island.md`。`AlsJointIsland` 完成多关节共享 DP/DQ/速度、整组阶段迭代与统一发布；通过冻结且无碰撞的 Godot 身体代理接入独立验证场景。144 组 × 12 帧原生对子在 Godot 回调中通过，最大位置 1.021e-6 cm、角度 3.577e-7 rad、速度 4.723e-5 cm/s / 8.398e-6 rad/s。两模型 40 身体/36 关节，30/60/120 Hz 无接触十秒通过，最大锚点 0.279537/0.270647/0.250296 cm；整链无 UE golden，不声明原生等价。修复 connector 舍入 scale 的刚体边界与 BodySet 自有 shape/material wrapper 延迟释放；退出 shape RID 残留修复后复跑通过，旧身体生命周期探针通过。Core 2638、Import 2356 通过/1 旧跳过，Godot 构建通过。此接口仅 force-free/awake，不包含接触、重力、移动 kinematic、睡眠、岛发现，普通入口未切换；原 Jolt 九项失败仍保留。下一步接触身份/流形与原生接触行对照，共享约束迭代后接外力、睡眠与 Ragdoll。坚持原批准的 C# Core 架构，无 Godot 引擎 fork 或 UE 运行时依赖。
- 前批记录：`docs/verification/2026-09-21-physics-joint-step.md`。Core 三轴锁定线性位置/速度约束、硬角速度行、组合关节步、drag/COM 积分和 float particle 存储边界完成。288 组原生组合阶段对照通过；144 组保持唤醒的真实资产连续 12 帧通过，最大位置 1.019e-6 cm、旋转 9.425e-8 rad、线速度 5.451e-6 cm/s、角速度 1.908e-6 rad/s。两套新参考重复导出一致，旧带睡眠参考重导字节不变（84 个休眠样本）；没有用参考 awake 标志驱动输出。睡眠/唤醒尚未移植。Core 2631、Import 2337 通过/1 旧条件跳过，Godot 构建通过。新 Core 关节尚未接 Godot 世界；旧 Jolt 九项整链失败仍保留。下一步物理后端共享 DP/DQ 与接触共同迭代接入、物理岛睡眠，再整链/Ragdoll 验收。原生模块与构建入口仍须放主仓库。不得把无接触/保持唤醒轨迹通过当完整物理世界或角色完成。
- 前批记录：`docs/verification/2026-09-21-physics-angular-rows.md`。新增 Core cached 角限位/驱动求解行，限位完整张量响应、驱动轴向响应、独立 lambda、SIMD 同步/逐轴顺序、质量调节和每步重置。UE 原生 516 组 × 8 迭代及重置对照通过，本机 DQ 最大差 0、姿态 dot 偏差 6.67e-16；重复导出字节一致。Core 2626 通过；Import 固定 JIT 全量 2334 通过/1 既有跳过（首轮 Aim 零分配失败、独立及固定 JIT 复跑通过，日志保留）；Godot 构建通过。UE 全目标构建/审计、普通重启及 DataValidation 通过（3 旧警告）。新行尚未接 Godot 世界，九项整链与 144 组 Jolt 旧失败未关闭。下一步线性锚点位置/速度约束、预测/回写及原生 144 组完整无接触步，然后接触共同迭代接口。不可把 Core 独立对照当 Ragdoll 完成；所有普通角色缺项仍保留。
- 前批记录：`docs/verification/2026-09-21-physics-angular-mass.md`。修复停用电机目标更新与全睡眠集合投影写回导致的唤醒，后端睡眠/再激活探针通过。新增可选 `--native-angular-mass`：只把预测驱动轴/pyramid 限位轴和关节局部惯量用于系数计算，未替换 Jolt 内部响应，默认关闭。与 `--native-projection` 同开时九项七过：30 Hz 普通/高速通过（高速锚点 0.095589 m、末秒速度 0.180139）；60 Hz 普通/高速速度分别 0.443923/0.333732 失败；30 Hz 无接触末秒角误差退化到 0.039790 rad。默认九项仍八过，144 组结果与旧默认字节一致。组合 144 组相对单投影 5 改善/4 退化/135 近似不变，最大旋转仍 0.449575 rad。18 次单关节、跨零、通道和纯阻尼拒绝通过。下一步约束行接口/逐端局部惯量响应与接触共同求解，不再把仅改系数当完整原生求解；普通 Ragdoll 仍未接入。
- 前一批投影：`docs/verification/2026-09-21-physics-linear-projection.md`。原生 cached 线性投影 262 组 delta/速度/姿态对照通过；全链先缓存再投影，静态子级跳过。Godot 仅诊断场景 `--native-projection` 可选开启，默认保留上一批实现。投影开启后九项锚点全过，但整链仅六项通过：30 Hz 普通角超限、30/60 Hz 高速末秒速度失败。144 组位置最大偏差降至 0.001189 m，旋转最大偏差增至 0.449575 rad，93 组旋转退化；parity_asserted=false。18 次单关节、跨零释放通过。下一步对齐角限位/驱动轴、关节局部惯量及投影接触阶段；不得将独立公式通过当作完整物理等价。默认实现与剩余总清单见下条。
- 默认物理实现：`docs/verification/2026-09-21-physics-independent-channels.md`。软限制/硬锚点与姿态驱动分成两个 Jolt 约束，各自目标、系数和累计冲量；40 刚体/36 逻辑关节/72 后端约束。独立通道探针、18 次单关节、软限制第 3 帧跨零、三个频率无接触与普通落地、60/120 Hz 高速落地均通过。剩余 30 Hz 高速落地第 21 帧锚点 0.290387 m 失败，九项仅八项通过；30 Hz 普通末秒角超限 0.096568 rad 接近门槛。60 Hz 普通/高速瞬态角超限仍约 0.88/0.93 rad，不能称观感通过。144 组对照最大旋转偏差降至 0.394426 rad，位置偏差 0.011906 m，parity_asserted=false。当前电机仍用 Jolt 误差/轴与 warm start，不等于完整原生约束行。下一步投影/接触、关节局部质量与限位轴；检查停用限位通道的目标更新是否造成多余唤醒。零刚度 6DOF 位置电机会停用（不是硬化），纯阻尼逐通道拒绝。启用条件见 `2026-09-21-physics-joint-activation.md`，原生惯量见 `2026-09-21-physics-body-inertia.md`，原生轨迹见 `2026-09-21-physics-joint-solver-reference.md`，绑定见 `2026-09-21-physics-joint-transport.md`，912 组角度数学见 `2026-09-20-physics-joint-reference.md`。cached solver 使用 Pyramid；不得硬编码孤立对子比例或将关节局部惯量写回共享刚体。无接触隔离须同时清空 collision layer 和 mask。项目采用 Jolt Physics、2 mm penetration slop；实验关节尚未接入普通角色。先完成整链稳定性，再接 Ragdoll/Get-up/Pose Recovery。普通入口已有地面与中等落差自动 Roll；高落差/翻滚离地的 Ragdoll 仅接通触发判定。`--action-preview` 保留旧预览；P5C/P6 未整体验收。

- 正常入口是 `scenes/demo/als_demo.tscn`，在实例化角色之前配置完整动画链路。旧 `p4_locomotion_demo.tscn` 仍供诊断场景复用。
- 验证必须在主目录执行，并记录失败和覆盖范围；旧阶段证书不自动适用于新的主分支。
- 引擎构建使用 `dotnet build GodotALS.csproj -p:Optimize=true`；包含大型值类型及零分配断言的 Core/Import 回归使用 Release。
- 最近实现和未完成项见 `docs/verification/2026-09-20-overlay-props.md`；故障恢复见 `docs/verification/2026-09-20-animation-failure-recovery.md`，停用恢复见 `docs/verification/2026-09-20-animation-deactivation.md`，永久退役见 `docs/verification/2026-09-20-animation-retirement.md`，动作输入见 `docs/verification/2026-09-20-action-input.md`，GroundedEntry 见 `docs/verification/2026-09-20-grounded-entry-notify.md`，动作摘要见 `docs/verification/2026-09-20-action-playback-summary.md`，冻结回归见 `docs/verification/2026-09-20-frozen-reference-replay.md`，入口与资产整合见 `docs/verification/2026-09-20-main-consolidation-and-demo-entry.md`。

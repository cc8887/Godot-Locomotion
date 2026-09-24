# Mantle 原始曲线与姿态联合采样

日期：2026-09-24。主目录 `.`，分支 `main`。

## 改动

T3D 中曲线时间只保留六位小数，因此本批没有用文本时间重建原始 float 键。
新增 UE `ReadSourceFloatCurves` 只读导出原始 `FRichCurveKey` 的时间、值、
双侧切线/权重、插值与 tangent/weight mode，以及 infinity/defaultValue。
`export_mantle_curves.py` 分开保存生产输入和原生 GetBonePose 的独立曲线参考，
不保存任何 UE 资产，不更改已有姿态/根轨道 JSON。

生产文件 `assets/config/refactored_mantle_curves.json`：3 个序列、6 条曲线，
保留 PoseStanding/PoseGrounded 的原始键与曲线身份，并用 SHA256 绑定完整动画输入。
参考文件 `assets/config/refactored_mantle_curve_reference.json`：每序列 121 个时间点，
共 363 次原生 raw GetBonePose 求值，包含 726 个曲线值。

`AlsMantlingCurveCompiler` 编译不可变曲线资源，严格检查来源、名称覆盖、有限值、
键顺序和已支持的求值策略。支持非空原始键、constant infinity 及
constant/linear/cubic 插值；weighted、其他 infinity 和空键策略明确拒绝，
没有悄悄降级为默认曲线。

`AlsMantlingPoseSource.CreateSampler(curves)` 校验资产路径及完整输入 digest，
防止不同动作或不同版本的姿态/曲线混用。联合 Sample 使用姿态取键返回的
SampleTimeSeconds，再转换 float 调用已有 `AlsCurveRuntime`，并保留曲线 presence。
本机 UE `AnimDataModel.cpp` 712/741 行同样用 SampleFrameRate.AsSeconds(SampleTime)
传给 EvaluateFloatCurvesFromModel；FFloatCurve::Evaluate 接收 float 并调用 RichCurve.Eval。
原来的纯姿态接口仍可独立使用。未接入场景播放图或 Montage slot。

## 验证

- Release Import Mantling 定向回归 47 项通过，0 失败/跳过。
- 726 曲线值与原生对照，最大误差 `5.9604645e-8`，保持预设 `2e-6` 容差。
  原生值处于 0..1 内部的样本计数 231；该计数也包含浮点舍入接近端点的值，
  不据此宣称 231 个互不重复的过渡时刻。
- 8 项非法输入拒绝及跨资产/跨版本绑定拒绝通过；此前完整姿态/设置/根轨道/
  运动源/重定向回归均在本批 47 项内。
- Godot 优化构建 0 警告、0 错误；未新增 Godot 场景或视觉测试、未跑全量测试。

首轮 `curves-first.trx` 为 9 过/1 失败：测试把 exporter 的 timeSeconds
（原始请求时间）错当成 DataModel 的量化取键时间。读取 exporter 和引擎实现后，
修正该元数据断言，保留实际曲线对照和容差；生产采样算法没有为此改变。
最终日志 `artifacts/mantle-curves/mantle-curves-final.trx`。

## UE 构建和导出

使用 ue-diagnosing-plugin-build-load 工作流完成全项目 Editor 目标和所有插件审计，
未使用叶插件构建、Live Coding、复制 DLL 或改 BuildId。
日志前缀 `20260924T142515825Z-9d3a1a06480347d5b2df5da349470394`；
BuildId `10fe1ab8-6888-437e-a5a8-0b6e73b8ae05`，fingerprint
`3A2435A0BA821769E64D0EE19C4E2A206EB0B4EAD76F08BE5BBA40D922CABECB`。

冷导出退出 0、0 errors/0 warnings。普通 Editor PID 28072 启动后完成相同导出，
延迟三个 Slate tick 退出；持有实际进程句柄等待得到 exit 0。两次输出字节一致：

- curves SHA256 `87441D4792EC835F58823A02D1D6AE5C9698B542ED1807C64CD2DA8296D20B0F`。
- reference SHA256 `656F7198C73684DFB94966F513C617D29F1715E69CD928E4A439351195665E2E`。

DataValidation 退出 0，0 errors/3 个既有 warnings。普通 Editor 的两条既有
LogAutomationTest Condition failed、旧 PawnActionsComponent/navmesh 警告及渲染警告
仍存在，未宣称已修复。完整日志均在 `artifacts/mantle-curves/`。
本次插件为 Editor-only，无游戏打包产物验证。

## 后续

继续完整 manifest/场景骨架适配、PostLocomotion slot 和 Notify/Notify State，
再串接 Mantle 探测与具身份的运动源/动作生命周期。当前 Mantle 尚未在普通 Demo
形成完整可触发玩法；本批不是最终观感验收。
非恒等 OrientAndScale 的独立原生覆盖、旧移动 oracle 闭包不匹配、旧物理9/12、
Flail0/3、复杂相机、最终视觉/十分钟性能预算仍保留。
头颈、道具物理及音频继续暂缓。用户 plan、project.godot、头颈文件和 .cs.uid
改动均未触碰、未提交。

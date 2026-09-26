# 原 Locomotion 姿态、Parent 与普通 Demo

基线 `main / 758e080`，在统一主目录实施。本批不改变输入和相机控制，不回退用户的漫游角色、HUD、场景及 LayerBlending 修改。

## 实现范围

- 原 Locomotion 非规则姿态节点闭包、Grounded 缓存、嵌套 Jump、两套独立 Air Lean evaluator、13 个独立播放器、四个同步组、四个落地预测固定帧 evaluator、各节点 alpha 历史和 node4/node39 惯性。
- Fall、Jump、Land、Land Movement 使用原始动画。移动落地按原图混入 Grounded 缓存和网格空间加法；瞄准影响加法权重；预测落地同时参与姿态混合和脚部 IK 曲线。
- 共享角色事务串起 Standing/Crouching → Grounded → Transition → Locomotion。Parent 保留起跳请求锁存、JumpPlayRate、落地前垂直速度和地面/空中共用 Lean；回调在原图遍历位置执行。
- Grounded 退出停止请求进入共享队列；LandToGrounded 在 PostUpdate 后按原蓝图参数播放右侧过渡（站立/蹲伏各自资源，rate 1.4、blend 0.1/0.2、start 0、无站立待机门控）。
- 普通 Demo 默认输出新 Locomotion 姿态。旧移动链目前只更新兼容源时钟/通知，不再求值移动姿态，也不再转发旧移动状态机的惯性请求。已有外层动作、上身、最终脚部、物理恢复与相机继续使用现有入口。
- 适配器在 Main 构建期绑定外层完整曲线布局，保留原始曲线和旧名字别名；旧 Grounded 缓存布局不能直接索引 Locomotion 输出。

更新但不求值、隐藏后重入、重复求值、故障丢弃、取消后同帧重试均保留；成功提交后才发布 Parent、播放器、动作和曲线历史。

## 原始数据与 UE 检查

新增只读 `ReadAirSettings` 和 `export_refactored_air_settings.py`，取得原 `AIS_Als_Default` 的 Lean half-life、五个完整精度曲线键及 246 个原生参考采样，未保存 UE 资产。不要从六位小数 T3D 文本重建这条曲线。

`assets/config/refactored_air_settings.json` 冷启动和普通 Editor 导出 SHA256 均为 `AE99F5D166DCF5BCA32FFEF9126ACAF445AA95D660D2EF046E9F471BCB09AB74`。

- 完整 Editor target 构建 10 actions 通过，包含 UBT 所需 NetCore 重编；四个项目插件审计通过。BuildId `2192dbcd-0924-430b-9a3d-1daff6c15a77`，构建状态 fingerprint `5CD2EF696AD2602C95CF6A8D3A16FE8409A1E45CD9E50AB8DF4ADCCB33142ADE`。
- 首次构建调用因系统默认 .NET 不含 10.0 失败；改用 UE 自带 runtime 后通过，没有安装或修改系统运行库。原失败日志保留。编译期间 UBA 因内存压力重试一个单元，最终构建成功。
- 冷启动 PID5348、普通 Editor PID26456、DataValidation PID39012 均退出 0。普通 Editor 两条既有 `Condition failed` 和旧资产警告仍在，不能写成所有 UE 问题已修复；DataValidation 0 errors、3 条旧警告。
- 新 C++ 源及反射声明与本机 UE 项目镜像哈希一致；没有运行打包验证。

## 验证与失败记录

日志位于 `artifacts/tests/refactored-locomotion-pose/`。

- `air-regression.trx`：Import 77 通过，包含新姿态六项、角色宿主五项、原状态机组件、Grounded、Parent、共享角色，以及 Standing 独立/共享原生六组。Standing 原门槛位置 2e-5 cm、旋转/尺度/曲线 2e-6 未改变。
- `core-air.trx`：Core 59 通过。Godot Optimize 构建 0 警告、0 错误。
- 新姿态测试在 30/60/120 Hz 各运行 16 秒，共 3360 帧，用真实 Grounded/Transition、实际 13 源采样、Sync 和惯性，逐帧取消重试；另验证固定预测帧的精确输出及 180 帧稀疏求值/隐藏/晚期故障。
- 新角色宿主三频率各 12 秒，共 2520 帧，真实 Parent/动作与逐帧重试；隐藏起跳锁存、清除、落地过渡播放和故障恢复通过。输入运动/地面预测受控，**不是新的 UE 连续整图 oracle**。
- 修正后的普通 Demo 在 30/60/120 Hz 分别提交 410/820/1640 帧，Locomotion 状态掩码均为 31（五个实际姿态状态），Grounded 均为 30；每帧脚锁原曲线/兼容别名检查通过。最终日志为 `demo-air-final-{30,120}.log` 和 `demo-air-lock-fixed.log`（60 Hz）。
- 实际渲染 60 Hz 同样通过 820 帧，`visual-final-60/` 保存 17 张 PNG。抽查第255/350/495/670/710/750/790帧，包含蹲伏移动、腾空、落地缓冲和恢复站立，角色结构连贯；这不是连续视觉/原生相似度验收，也未关闭头颈专项。
- 普通键鼠 `demo-air-final-keyboard.log` 360 帧通过，Alt/A/D、四次鼠标事件、松开 Alt 后移动、脚趾接触和上一提交反馈有效；保留用户新增的十个漫游角色，没有为通过测试关闭它们。
- 相机 `demo-air-final-camera.log` 480 帧通过，包含第一人称、Ragdoll/Get-up 交接。翻滚 `demo-air-final-roll.log` 使用单角色诊断模式，420 帧、接受2次、忙碌拒绝2次、完成2次、翻滚114帧、转向57帧通过。
- 十角色 single/parallel 各 3621 个角色帧通过，两次取消重试、一次延迟提交、air520/crouch600/locked961、events120/rays6544 一致。姿态摘要 `F0E4D3B5562FBE84`、根运动摘要 `2E26DB87948B57C5`、结果摘要 `5F1919BCFEADFBE3` 两模式完全相同。脚部完整参数开启，没有用关闭 IK 或简化图通过。
- 最终上述 Godot 日志无 `ERROR`、`WARNING` 或 `BodyHistory` 失败；没有执行全仓测试、十分钟性能预算或人工签收。提交前本批文件 `git diff --check` 通过。

保留的失败：首姿态导入把嵌套状态图绑定误算到外层；修为只读取节点自身绑定。首宿主 Stop 回调查找同时匹配 function entry 与 call，改为限定 CallFunction。编译时的内部数学访问、构造重载推断、测试字段命名问题也已修正。

首次普通 Demo 帧率/画面检查未断言脚锁曲线别名，之后键鼠专项发现首帧错误。追加诊断确认原始输出已有错位：新适配边界误用了 Grounded 曲线索引。修为显式绑定 Locomotion 完整输出布局，并在普通 Demo smoke 每个有效提交帧检查别名一致。修正前 `demo-air-first`、`demo-air-{30,120}`、`demo-air-render-60`、`demo-air-keyboard*` 和 `visual-60/` 都保留，不能用它们代替最终复测。

## 仍未关闭

没有宣称完整 ALS 或视觉/性能验收。本批后仍需：清除旧通知/时钟兼容；真实源 Notify→Pivot；最终足锁/脚目标→Rest 动态过渡；原 MovingSmooth 设置；移动平台相对位置输入（当前 Demo 接口仍 false）；真实组件变换惯性（桥内仍 Identity，桥外保留已有世界/teleport 惯性）；Crouching/Grounded/Locomotion 与 Parent 的连续 UE oracle；原生上身/Aim/Overlay/最终脚部整链；完整共享动作资源与 Mantle/Roll/Root Motion、物理长期失败及完整相机；十分钟性能预算和可复现资产交付。

音频、道具物理和头颈拉长专项继续按用户要求暂缓。没有 push；用户已有未提交文件不纳入本批提交。

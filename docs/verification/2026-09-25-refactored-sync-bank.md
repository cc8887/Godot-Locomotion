# 原动画同步资源表

主目录 main 新增完整原始同步元数据导出与 `AlsRefactoredSyncBank`。覆盖127 Sequence和9 BlendSpace，共136资源；60个 authored sync marker，名称为Left/Right。包括Overlay使用的原序列，不再只局限于当前移动样本。

## 来源与运行时绑定

UE helper直接读取Sequence的GetPlayLength、RateScale、AuthoredSyncMarkers（原索引、名称、时间、轨道）和GetUniqueMarkerNames；BlendSpace读取真实样本、速率、mirror/single-frame、effective marker names、legacy length/phase/marker/notify政策。没有保存UE资产。

编译器绑定完整catalog哈希，要求资源闭包相同。Sequence时长与现有raw policy一致；速率和逐标记的名字、顺序、轨道、时间与原nativeText交叉检查。nativeText仅六位小数，所以时间交叉检查预算0.00000051秒；运行时保留独立导出的完整float，不从文本恢复时间。

127个Sequence按路径排序分配0..126；随后9个BlendSpace分配127..135。标记符号表共享，0为None。返回immutable Core Sync sequence/marker数组及按路径绑定，JSON行序变化不改变这些编号。此为新的同步资源作用域，**不等于已经重映射所有PoseSource内部AnimationId或普通宿主的旧V4资源表**；播放实例、epoch、图节点与资源编号仍是不同身份，后续宿主须显式绑定。

原9个BlendSpace全部为legacyLength=false、matchPhases=false、allowMarkers=true、HighestWeightedAnimation。样本rate=1；编译器拒绝变化后的旧长度策略、phase、mirror、single-frame和不兼容marker pattern。`BindSamples`把已求得的有序权重映射到正确Core sequence索引和调用方指定的sample ID作用域，不改变权重/顺序；校验全部输入后再发布输出。

## 检查与范围

- 完整136资源、127sequence、9blend、唯一连续ID、反转导出行序后编号/marker数据一致。
- 所有BlendSpace的样本都绑定到Core Timing，实际新长度模式有效时长大于0；错误样本不改调用方buffer。
- 原Forward Walk实际样本在Core marker sync group推进240帧，每帧从独立自身history求值和重试，marker状态有效、重试输出相同。这是实际资源接入和确定性测试，**没有新增UE原生连续tick oracle，不证明完整Refactored同步轨迹等价**。
- 六项非法资源/目录hash/速率/标记/时长/legacy政策拒绝。新增8测试，相关Import23通过，Optimize构建0错误0警告。首轮7测试通过，增加legacy门禁后8测试随回归通过，无失败或预算调整。
- UE完整Editor构建5actions及审计通过，fingerprint `6FF235AC7D846676531BCA495DE034FD2A4920DD88BFED5C11967D21FE3ADF37`。
- 冷导出和普通Editor PID30504实际重启/导出/退出均0，数据字节相同，SHA256 `CF0B5EB311F895E9DD4794D342A2C33F8E87FE252831322D3823A4E2E5A9DD32`。普通Editor仍有两条旧Condition failed和五条旧警告，未修复。
- 无Godot运行场景、全量回归或打包完成声明；UE项目无现成打包流水线。
- 本批DataValidation实际退出0，三项既有加载/导航警告保留。

日志/TRX在 `artifacts/refactored-sync-bank/`。

## 接下来

使用这些原资源做实际UE FAnimSync连续tick导出，并让C#自主推进滤波、三角形、marker/history和新长度模式，逐帧比较不同领导者/方向/起停/恢复；再把每样本时间接入姿态采样（当前BlendPose仍是normalized evaluator）。之后完整Locomotion/Overlay图及普通共享宿主。普通Demo尚未切换，全部旧Mantle/Ragdoll/Get-up/Pose Recovery/Camera/性能缺口及用户暂缓项保留。

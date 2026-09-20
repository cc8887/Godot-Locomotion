# 统一来源批次与 Main 来源收集（第五十五批）

## 完成范围

本批消除站姿分支必须在内部自行同步的结构限制，为 Main/BaseLayer 统一接线
提供实际生产阶段接口。尚未完成 Main/BaseLayer 的全局缓存调度、Slot/惯性化
owner 或完整站蹲 Demo；基础移动、交错步、滑步和上身验收均不关闭。

原生产 `AlsStandingCycleGraph.Prepare` 现在依次调用：

1. `PrepareUpdate`：状态、方向、Stride/滤波等更新，不推进来源时间。
2. `CollectSources`：向整帧缓冲追加来源/样本/活跃性；不推进同步。
3. `AlsSharedSourceBatch.Evaluate`：所有来源一次共享 Sync、通知映射和候选结果。
4. `CompleteSources`：消费已完成同步的采样秒数与同步组相位。
5. `EvaluatePose`：实际 Standing/Stop/Detail 姿势与曲线求值。

Controller 仍调用同一个完整入口，未切换成旧蹲姿近似路径。生产来源初始化仅
覆盖 Cycle/Detail/Standing，不再预先写入未访问 Main/Crouching 的时间及 epoch。
这不是 Standing 全部初始化语义已闭环；旧位掩码消费者及缓存生命周期仍待补齐。

## 统一所有权

`AlsSharedSourceBatch` 校验来源快照、容量和候选时间/epoch 与贡献一致，使用
既有 Core 绑定、Sync 和 P5 通知实现，不新增时钟。来源活跃性由图 owner 提供，
按 player/sample identity 回填，不能按同步组重排后的数组位置套用。失败不返回
部分结果，空批次退休来源/样本历史并清除同步组和旧 selected-group 别名的 leader。

`AlsMainGroundedSourceCollector` 负责 Main 状态 3、4、7 的三个资产来源。采用
有序初始化和真实 GetUpdate 权重；两条站蹲转换保持 DoNotSync 与原 1.2 常量速率，
From Roll 保持显式采样、不计时、不产生来源时间线通知。自动退出读取前帧权重、
源时间和 delta history。缓存和 Slot 不由这个收集器擅自模拟。

源图站蹲 Rotate 读取同一组角色变量，因此继续共享 RotateRate/Left/Right 输入，
独立的是播放器身份与 epoch。没有以“独立身份”为由发明两套角色输入。

`CrouchingStateRuntimeSmoke` 的实际主状态/Cycles owner 也改用同一批次实现，
不再保留另一段批次包装代码。

## 检查结果

| 检查 | 结果 |
| --- | --- |
| 最终构建 | 零警告、零错误 |
| Core 常规 | 1891/1891；按既有命令排除 P5A Golden/TraceSchema 两类 |
| Import 全套 | 1101/1101 |
| 新生产分阶段检查 | 840 帧；时间、历史、状态、最终 Standing 骨骼及曲线与完整入口一致 |
| 新混合批次 | 1260 帧、13522 次来源贡献、38 个事件、27 次非法批次拒绝、活动 0 B |
| Main 来源/姿势 | 1260 帧，覆盖 Standing/Crouching/下蹲转换/起立转换/From Roll；独立 epoch 和常量速率核查 |
| Main 片尾 | 6 组合法 float 末尾与实际末帧一致；下一 ULP 拒绝；未发现需修改采样器的问题 |
| 蹲姿主状态来源 owner | 1671 帧、29 事件、104 双读取、6 缓存初始化/6 重入、15 拒绝、0 B，结果保持 |
| Standing/Detail/Pivot | 5040/1890/5040 帧；Sprint 1260 帧，原检查保持 |
| Worker single/parallel | 各 180 帧、10 事件，结果/骨骼/根摘要保持 |
| 并行晚期事件失败 | 来源状态、事件、随机状态、Controller、姿势、交换回滚通过；回调泄漏零 |
| P4 pose 脚本 | 通过；动画图、骨骼修改、双模式脚部约束及晚期事务回滚 |
| P4 matrix 脚本 | 1/10 角色 × single/parallel 四格通过；每格 120 帧热身、600 帧采样 |
| Demo 300 帧路线 | 失败，保持已知平台脚锁问题，见下节 |

生产摘要保持：result `A9DF0647AFC3574C`、full pose `04D4A5651B87E0E4`、
pose `2DED5435A66BCAEC`、root `309E8D0E0BEEB2CB`。

证据文件：`artifacts/test-results/shared-source-batch/*.trx`；
`artifacts/shared-source-all-final.log`、`shared-source-standing-final.log`、
`shared-source-crouching-complete.log`、`shared-source-worker-single.log`、
`shared-source-worker-parallel.log`、`shared-source-late-event-parallel.log`。
P4 pose/matrix 脚本的本轮输出见执行记录，不能把这些短时检查当成 P7 十分钟预算。

新混合 smoke 使用受控重叠权重，并主动更新多个蹲姿来源进行批次压力验证。
Main 姿势使用实际 Standing 输出及真实蹲姿 Idle 缓存样本，尚非完整 Crouching
缓存求值；它证明来源批次和 Main 源时间可组合，不证明完整原生 AnimBP 等价。

## 保留失败

新增测试首次直接从 record 属性中的 InlineArray 获取 Span，触发 CS9165；
改为局部 frame 副本后构建通过。首次构建失败后曾继续启动旧程序集，导致
找不到新脚本；已用该会话的中断停止进程，成功构建后重跑。记录未覆盖或伪装成功。

最终 Demo 路线 `artifacts/shared-source-demo-complete.log` 仍报告：

- 平台窗口 85--96 帧，观测平台并确认其移动，但脚锁样本为零。
- 窗口 IK/FootLock 为 `(1,1,0,0)`；后续转身窗口的 `(1,1)` 不能替代它。
- sprintEnd `(-3.999,1.0001398,-7.932024)`，brakeEnd
  `(-3.972002,1.00092,-9.286164)`，与第五十三批一致。

未修改路线、阈值、曲线或强制锁脚。仍须核对源状态、最终曲线与路线停止时机。

## 下一步

下一批应直接组织 Main/BaseLayer 共享缓存 owner，复用本批的来源阶段，不再
实现另一段同步。原生 compiled 顺序和生产归属如下，必须按正式缓存表解析：

| 顺序 | 内容 |
| --- | --- |
| 100 | Main Grounded/Slot |
| 99 | Standing States |
| 33 | Standing Detail |
| 32 | Standing Cycles |
| 30 | Crouching States |
| 31 | Crouching Cycles |

现有 StandingCachedGraph 仍内部排空，需移交全局 owner；Main 的状态权重、
缓存 Initialize/CacheBones/Evaluate、Slot/惯性化请求与最终提交还未统一。
完成后继续最终曲线、动态 Layering/Add/LS/Lean/IK mask、完整脚锁/骨盆约束，
再按原 P5A/P5B/P5C/P6/P7 继续。原目标和音频暂缓约定不变。

本批未修改 UE 插件/源资产/键鼠，未提交或回滚。没有新 UE 全图探针、移动截图、
新的 Release 专项或完整 P4 聚合脚本；不宣布 Demo 视觉修复或最终性能验收。

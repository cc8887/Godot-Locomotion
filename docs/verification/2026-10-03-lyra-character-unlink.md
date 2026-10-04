# Lyra 普通角色 Unlink 与重新绑定

2026-10-03。直接在主目录实施，保留 ALS68 蒙皮、raw69/logical81 和现有 Lyra 管线。新增真实角色解绑入口和完整默认输出分支，不创建替代 Linked Provider。整个移植目标仍 active。

## 实现边界

`LyraCharacterAnimation.Unlink()` 在实际角色的空闲物理边界解绑当前 Provider。Profile 作为装备身份保留，binding epoch 增加；同类重新 Link 从零实例恢复原 Provider。同类重连保留实际武器对象，换类沿用原装备替换。未完成的物理/动画候选禁止改变绑定，已提交通知回调仍可按原边界操作。

`LyraLinkedLayerGraphSet` 接受完整 self 目标集合，实例列表为空，外部 Source Scope getter 明确拒绝；十四个调用点仍以真实 Main owner 和已提交目标建立路由。旧图和路由退役。这里只增加全 self 分支，没有实现任意部分覆盖或不同 Provider 混合。

Main 持有 Profile、epoch 和持续状态，停止依赖第一个 Linked 实例。重新绑定预览以 GraphSet 身份拒绝过期计划，沿用同一 Main owner。旧 BlendOut 请求按实际前任类读取：从 self 重连的前任是 Main，不能沿用此前 Provider 未消费的请求。Main 元数据来自完整编译 catalog，外部 Provider registry 不包含 Main；读取和校验发生在发布绑定之前。

`LyraMainPoseCandidate` 显式区分外部图与默认帧，后者没有虚假 Locomotion/Source 候选。默认分支更新 Main worker，执行 self SkeletalControls 空根，停止上游状态机、Lean、cache、Slot 和惯性求值；对应诊断访问拒绝旧输出。完整输出仍进入同一 ALS final Rig73，再由原模型 writer 发布。

空源仍经过原共同 Sync 算法，提交时交换双缓冲、保留各 map 的组键并清空活动 player/sample 记录。Montage 沿用实际角色物理 bank 和时钟；Slot owner 以未访问批次完成事务，保留上一帧相关性的既有处理。Source 通知桥以真实默认帧身份捕获零 tick，继续与 Montage 汇入原角色队列。Named receiver 退役实际 Linked 接收者，Main receiver 保留。最终曲线反馈、Rig、模型和通知继续统一验证、取消、提交。

## 验证

最终候选为 `unlink-production-v9`。Debug/实际 ExportRelease Optimize 构建均零错误零警告；Core Linked 相关59项通过。两种构建各17个、合计34个最终 Godot 进程均通过，日志退出0且无 ERROR/WARNING。普通十角色/Emote完整报告在两种构建间、与前批逐字段相同。

新增场景使用原 `LyraSceneCharacter`、真实 Jolt 世界、原 ALS 模型和完整角色事务。三组角色分别使用 Unarmed/Pistol/Rifle，每组的 subject 求值后取消并重试，control 只提交一次。六秒轨迹包括两次 Unlink、两次同类重连、ADS/移动/停止/蹲伏、解绑期间原 Reload bank、退休路由与候选拒绝。

空中覆盖来自明确的场景扰动：在空闲物理边界把胶囊抬高2米，之后使用原场景移动服务实际下落。不能把该夹具写成 Shooter 自然 Jump 行为等价。最终 Rig 的完成次数和输出改变次数分别记录；允许最终结果等于 ALS reference，角色提交仍必须验证真实 Rig 求值。姿态、曲线、属性和 root 的 retry/control 比较保持精确相等。

修正版30Hz记录为1080角色提交、360默认帧、540 retry；Rig完成360、姿态改变354、默认空中54帧。六种解绑配置在每种构建中合计14040角色提交、4680默认帧、7020 retry、744默认空中帧；所有默认帧均完成最终 Rig。两种构建的逐用例计数完全相同。矩阵包含30/60/120Hz和60Hz three-groups/mixed/per-call，并完成现有默认 worker/native参考、路由、named/weapon/live通知、普通十角色/Emote及四布局完整外部 Main 原生最终 Rig 对照；外部整链每种构建4320帧，逐帧 retry。

独立审计通过：870份资源 JSON、710个原包与9项项目/配置哈希保持；冻结的运行时、夹具和驱动源码保持，三轮各六个 Debug 程序集/符号恢复逐文件 SHA256 相同。三份原 UE 源码与逐字副本再次匹配。最终证据为 `artifacts/lyra-analysis/unlink-production-v9-integrity.json`。

## 过程证据

早期两处编译类型错误及一次诊断参数类型错误保留各自日志。v4地面用例通过；v6旧程序覆盖六种解绑配置，随后因确认前任类 BlendOut 缺口主动结束已启动的旧十角色进程，保留 `unlink-production-v6-interruption.txt`。该运行不作为最终验收。

v7发现 Main 不在外部 Provider registry，原 lookup 抛错；改读完整编译 catalog 并前移到 binding commit 之前。v8及其独立诊断完成1080帧但收尾覆盖失败：360默认帧只有354改变姿态，默认空中帧为0。补真实下落夹具，并将必须完成 Rig 的检查与姿态改变统计分开，没有修改运行姿态算法或原生误差门槛。

首次最终审计因 PowerShell SHA256 大写与 Python 小写字符串直接比较而失败。逐项核对字节哈希相同后，仅修正审计器的十六进制大小写比较；原冻结清单保留，审计器旧/新 SHA 和原因另存 `unlink-production-v9-audit-correction.json`。运行时代码、资源、测试输入和误差门槛未改变，修正后完整审计通过。

## 仍开放

本批未新增 UE 运行或原生轨迹。沿用上一批完整默认 Main 的2520帧原生参考、Core绑定矩阵以及已有外部 Main 原生资源；三份原 UE 源码另行逐字复制并校验。新 ALS81 默认最终姿态的连续 native oracle、完整 Initialize/CacheBones、初始 self 角色、默认 scalar 参数、任意非空默认图和部分绑定仍开放。

原 Linked 字段34/47、完整物理314/1680差异、近景握持与脊柱、其它 Provider/通用图、复杂地形和性能继续开放。音频、道具物理与头颈专项保持暂缓。本批没有原 UE 资产保存/重导、GPU默认姿态观感、全量 managed、十分钟或性能验收；没有提交或推送。

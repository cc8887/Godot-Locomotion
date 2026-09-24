# Refactored 生产源播放器调度

在主目录 main 新增 `AlsRefactoredSourcePlayerRuntime`，将此前测试夹具中的filter→triangle→Sync→sample time→pose/curve流程移入可复用生产owner。当前没有修改普通Demo入口，也没有把实际动画图替换为硬编码测试状态机。

## 资源和生命周期

构造时冻结每个播放器的source、group、start position与loop配置；播放器ID是owner内连续编号，资源ID来自完整Sync bank，sample ID有独立播放器作用域，epoch在首次激活/显式重置时推进。同一资源可由多个节点独立播放，不共用时钟或采样scratch。

支持原absolute/local/mesh additive Sequence和已编译二维BlendSpace；Look的一维evaluator继续原Head路径，当前不是此player owner的输入。最大128播放器、64组，单组上限遵循Core。每帧按调用方注册顺序提供活跃请求，固定组ID可包含多个named组和独立组(-1)。当前role为CanBeLeader；图侧其他角色/动态group政策仍需显式扩展与验证。

Prepare把滤波、triangle cache、epoch和time写候选，再调用Core Sync batch；输出group/player/sample history和tick context供后续阶段使用。Evaluate按各样本已同步的秒数求姿态/曲线，重复求值不推进时钟。ValidateCommit/Commit统一保存历史；Cancel丢弃候选。迟到的非法请求和Sync失败不改提交状态；姿态采样失败阻止本候选提交。

允许更新但不求值的节点提交时间，这对应图中更新相关但姿态被完全覆盖的情况。姿态/曲线只在当前候选已求值时可读；提交/撤销后不可当作当前帧输出。每个owner独占使用，不支持并发调用同一owner。数组和采样scratch在构造时分配，尚未做本模块专门零分配或最终性能验收。

对外暴露resolved samples、player ticks、tick contexts、Notify模式及各源curve names；Notify提取/队列提交、root motion、graph相关性和最终骨布局转换仍由后续frame stage接入，不在此实现中暗自执行。

## 证据

- 扩展已有Refactored native trace测试，使用生产owner自主Prepare/Commit。全部1344帧、13283sample状态、282次leader切换仍通过；每帧生产owner与既有独立对照算法的历史严格一致，Cancel后重新Prepare相同。
- 原384native姿态/30336骨关键帧全部经生产Evaluate，重复Evaluate相同；结果与已验证sample-time pipeline逐值相同。native参考沿用上一批，无重新导出或新增UE运行。
- 新90帧同源双独立实例+local additive Sequence：资源ID共享、播放/sample身份分开、时间不合并，三个序列pose/curve与直接资源采样一致。
- 新120帧多named组+独立序列，故意在已准备前两个输入后提供NaN的最后输入，提交状态不变；恢复、撤销重试后全部group/player/sample及pose/curve一致。
- 旧frame、外来frame提交拒绝，未求值节点可以按设计提交时钟；提交后不能读取陈旧pose。
- 新增2项并扩展1项原生回放，相关Import20项通过；Godot Optimize构建0错误0警告。没有测试失败、预算调整、Core同步算法改动。
- 本批无UE代码更改/新构建/重启/DataValidation，也没有Godot运行场景、全量或打包验收。

日志/TRX：`artifacts/refactored-source-player/`。

## 下一步

把真实Locomotion/Overlay图的初始化、更新相关性和播放参数绑定到此owner，再让图按节点求值并统一curve布局，接PostLocomotion/Layering/Head/ControlRig/Ragdoll最终链。随后Notify/root motion和普通宿主整体验证。普通Demo未切换；用户原有修改、暂缓项及所有Mantle/Ragdoll/Get-up/Pose Recovery/Camera/十分钟预算未完成项仍保留。

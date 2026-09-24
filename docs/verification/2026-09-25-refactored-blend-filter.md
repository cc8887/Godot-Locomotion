# 原二维输入滤波与候选帧

本批在主目录 main 新增 Refactored 专用 cubic 输入滤波及其与二维姿态 evaluator 的事务式组合。旧 V4 路径不变，普通 Demo 尚未切换。

## 原生规则

本地 UE AnimInterpFilter.cpp 的 FFIRFilterTimeBased 使用独立轴缓冲区：初始10槽、用尽后每次加5槽，按槽位索引求和，不是按历史时间排序。过期槽的 Time 清零，从 CurrentStackIndex 向后寻找空槽；计算后下一槽索引绕回。正窗口在 delta <=1e-4 时返回上次输出，零窗口即使 delta=0 也直接输出本次输入。Cubic 不在滤波阶段 clamp 原输入，坐标范围由后续 BlendSpace 处理。

新 Core AlsRefactoredBlendFilter 保留这些顺序和 float 运算。历史为值类型，按提交状态生成候选；固定260槽上限，超限显式拒绝且不改提交值，不丢弃有效历史。实际 .2/.4秒窗口在本批30/60/120Hz范围内不触及上限；没有宣称任意高频/任意窗口无限容量。

Import AlsRefactoredBlendEvaluatorRuntime 组合 filter→triangle cache→真实pose/curve。Prepare 指定单调frame id、输入、delta、显式normalized time，可重置；Evaluate可重复；ValidateCommit后Commit一次性保存滤波和cache；Cancel丢弃候选。尚未求值、外来frame、旧frame不能提交；失败输入不推进状态。每个运行时要求独占owner。此对象没有播放器时钟和Notify，不能冒充完整Sync播放器。

## 验证

- 原生导出直接执行实际8个BlendSpace的FilterInput，再GetSamplesFromBlendInput。30/60/120Hz各4秒，每Hz中间重置，混合delta=0、5e-5和普通步长，输入包含轴外和方向突变，共6720帧。
- C#独立推进filter/cache。最大滤波差0，采样数量/顺序/索引/cache逐帧相同；最大权重差5.9604645e-8。初始预算filter和weight均1e-6，首轮通过，未调整。
- 同时每帧求值实际pose/curve：clean owner与撤销重试owner结果一致，重复Evaluate一致，frame/未求值提交门禁通过。这里的连续pose只证明事务一致性，没有新增6720帧UE完整pose oracle；完整pose独立400样本对照见上一批。
- 260槽溢出不改提交输出，default重置恢复；零窗口delta0透传通过。新2项，相关Import20项通过，Godot Optimize构建0错误0警告；无全量或Godot场景测试。
- UE完整Editor构建4actions成功，插件审计fingerprint `458725D1DF02D6B08243F2EA1BF48C046C8B97FF15E9B53E88C99E28141F4DE4`。按既定构建技能运行完整目标，未复制DLL。
- 冷启动与普通Editor PID36172真正重启/导出/退出均0；参考逐字节一致，SHA256 `D349A311C6C80BB484FF16628D46870DBFE1F92D25B7B3D2E4A15EB0867C1BA0`。导出未保存UE资产。
- 普通Editor仍有两条旧Condition failed和五条旧警告，未修复；无现成项目打包流水线，不声明打包完成。
- 本批DataValidation实际退出0，三项既有加载/导航警告保留。

日志和TRX在 `artifacts/refactored-blend-filter/`。

## 下一步

提取并绑定原动画marker、播放速率与sample时钟，接现有Sync Runtime，随后完整Locomotion/Overlay图与普通宿主。当前explicit time只用于evaluator，不等于marker同步。总目标的Mantle gameplay、Ragdoll/Get-up/Pose Recovery、Camera和十分钟性能验收仍保留，旧物理失败未关闭；用户修改和暂缓项保持。

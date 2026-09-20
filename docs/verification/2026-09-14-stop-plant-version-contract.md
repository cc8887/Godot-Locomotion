# 停止选脚与 Plant 来源的版本核对（第一百八十四批）

## 本批完成与边界

继续 P3/P4 完整性修复，新增两版停止 Plant 的原生采样导出、配对检查，
并把 Refactored 停止状态的真实连线、条件输入和编译后顺序加入可复查合同。
这批推进的是上游来源核对，没有修改生产脚锁算法，也没有关闭第 616 帧
41.10° 的视觉失败。单独函数一致仍不能证明整个角色已经 1:1 移植。

## 来源、条件与采样

来源是第 162 批保存的原生 AB_Als_Standing 图/编译状态机、正式 V4 Stop
和 Grounded 图，以及当前 UE 项目的实际 AnimSequence。没有根据节点名称
猜测左右脚，也没有按编译后数组下标直接匹配编辑器节点后缀。

`prepare_stop_plant_contract.mjs` 现在按 In/Out 连线解析状态，沿
TransitionResult → 比较节点 → PropertyAccess 核对输入，最后按起止状态
匹配 baked edge。Refactored Entry 实际顺序为：

| 编译后顺序 | 编辑器转换节点 | 目标 | 比较函数 | B 的导出字面量 | 淡入 |
| --- | --- | --- | --- | --- | --- |
| 0 | _20 | Lock Right Foot | Greater | 0.500000 | 0 |
| 1 | _19 | Lock Left Foot | LessEqual | -0.500000 | 0 |
| 2 | _22 | Plant Right Foot | Greater | 未序列化 | 0.1 秒 |
| 3 | _21 | Plant Left Foot | LessEqual | 未序列化 | 0.1 秒 |

四条输入均是 `GetParent.FeetState.FootPlantedAmount`。未序列化的数值 pin
在合同中保留 null，不能把它伪装成读取到的作者字面量。完整执行默认值仍
由 UE 语义决定。左右方向并未接反。

V4 先经过 Foot Down/Up conduit：`abs(Feet_Position) >= 0.5` / `< 0.5`，
再按 `< 0` / `> 0` 选脚。与 Refactored 至少在精确 +0.5 边界不同；
不能仅改曲线别名便宣称整个选脚合同相同。没有证据表明该精确边界导致
第 616 帧问题，故未直接替换生产判断。

两版 Lock/Plant 都由 ModifyCurve 给选中脚写 1；Refactored 节点序列化
CurveValues=0 不能覆盖其实际输入 pin=1。Plant 都是 0.1 秒混合。
停止 Transition 播放参数也相同：淡入/淡出各 0.2 秒、倍率 1.5、从 0.4 秒
开始。Refactored 使用 A_Als_Stop_Left/Right，V4 使用旧停止片段；同参数
不能消除第 162 批已经测出的资产曲线差异。

两版左右 Plant 各六方向，共 24 个采样点直接经 UE RAW 求值。V4 以显式
秒数取样，Refactored 以帧号除以资产实际帧率取样。例如 0.1330000013 秒
与 4/30 秒并非相等。采集全部骨骼 local/component 姿势和实际存在的曲线，
配对报告比较 pelvis、左右 thigh/foot/ik_foot 七骨：

- 12 对样本最大位置差 0.2609657571 cm，最大旋转差 0.1088603469°。
- 比较器同时核对实际资产的六方向语义，不只依赖复用的节点名。
- 脚锁曲线在这些源片段中均缺失，由图内写入；缺失保留 null。
- Refactored Plant 的分层分支额外包含 VB foot_l/r；V4 导出分支为 thigh
  和 ik_foot。该差异需要在完整姿势/虚拟骨处理顺序中继续验证。

这些数值不是整图一致性通过，也不能排除小位置差在约束角度边界被放大。
它们只排除了“源 Plant 固定姿势自身已经有约 41° 版本旋转差”的解释。

## 原生验证

使用前批未改变的完整 Editor 构建，四插件审计通过；未修改插件源码或
UE 资产。Commandlet 与普通 Editor 均退出 0，后者延迟关闭。

`artifacts/stop-plant-184-native.json` 和 `stop-plant-184-editor.json` 字节一致，
SHA-256 为 `83DCB784A3077DF8809F978C3AD6DFD91EA4CE9D7566F5FFC6D9268153BF9A96`。
正常 Editor 仍记录两条已有 AutomationTest Condition failed，不能称日志
完全无错误；本次 24 样本导出及关闭成功，未保存资产。

最终合同为 `artifacts/stop-plant-184-final.request.json`，最终比较为
`artifacts/stop-plant-184-final-comparison.json`。增加转换信息不改变原始
24 个采样请求，比较器逐项验证了这一点。Node 语法、导出 Python 语法与
差异空白检查通过；本批没有生产 C# 改动，不重复无关的角色/全套回归。

## 接下来的修复工作

1. 将停止入口、父状态混合、Detail/Plant、实际 Stop Montage、最终锁曲线
   和上一 Final 捕获放入同一连续 UE/Godot 对照；记录最早分歧，不能每帧
   都重置 UE 为 Godot 历史后把函数通过称为整个状态机通过。
2. 对已经核实的版本差异建立明确适配：状态条件/曲线语义、停止资产、
   virtual bone 和运行顺序一起验证。保留原 V4 资源目标，不能悄悄替换
   若干资产或关闭 Refactored 约束来使单个截图通过。
3. 修复后重新跑起步、停步、两向换髋、上身分层、平台、Alt 观察和不同
   帧率；完成整角色及人工验收后再切换默认完整入口。
4. 随后按原范围继续 P5A 通用事件/同步/动作、P5B Overlay/道具玩法、
   P5C Mantle/Roll/Root Motion、P6 Ragdoll/Get-up/恢复/完整相机、P7
   十角色十分钟性能预算。音频暂缓；这些阶段均未取消。

既有 Core 23 失败、Import 1 跳过与未归因栈溢出仍需处理；本批不更改
这些验收状态，也不提交或回退用户现有工作区变更。

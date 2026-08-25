# P0 确定性核心实现记录

## 工具链

- Godot：`4.7.2.stable.mono.official.ed1daf0bf`
- Godot C# SDK：`Godot.NET.Sdk/4.7.2`
- .NET SDK：`8.0.100`，由仓库根目录 `global.json` 固定
- 目标框架：`net8.0`
- C#：`12.0`
- 测试：xUnit `2.9.3`、xunit.runner.visualstudio `3.1.5`、Microsoft.NET.Test.Sdk `18.9.0`
- Physics：`60 Hz`

## 工程边界

`GodotALS.csproj` 是 Godot 主工程，只编译 `src/Als.Godot` 下的适配层并引用 `Als.Core`。`src/Als.Core/Als.Core.csproj` 是普通 `net8.0` 类库，不引用 Godot SDK、GodotSharp、Node 或 Resource。

`ContractLayoutTests.CoreAssemblyDoesNotReferenceGodot` 检查核心程序集的引用列表；`FrameContractsContainOnlyUnmanagedData` 使用 `RuntimeHelpers.IsReferenceOrContainsReferences<T>()` 检查核心帧结构。两项检查均已通过。

## 帧合同

当前定义了以下固定布局值类型：

- `AlsFrameIdentity`：`FrameId`、`CharacterId`、`SlotGeneration`；
- `AlsFrameInput`：帧时间、角色运动、视角、地面、双脚、Mantle 探测和请求状态；
- `AlsRuntimeState`：worker 跨帧保存的平滑值、脚锁、动作相位和恢复状态；
- `AlsFrameResult`：解析状态、Root Motion、骨盆/双脚目标、运动意图、事件和诊断；
- `AlsRootMotionDelta`：平移和旋转值；
- `AlsFloorSample`、`AlsFootHit`、`AlsMantleProbeResult`：Gather 阶段产生的空间采样。

所有运行时 enum 显式使用 `byte`。帧合同只使用整数、浮点数、`System.Numerics` 值类型、enum 和其他 unmanaged 结构；不包含字符串、数组、集合、delegate 或 Godot 对象。

## 类型化事件缓冲

`AlsEventBuffer` 使用 C# 12 `InlineArray` 内联存储 16 个 `AlsAnimationEvent`，缓冲自身是 `AlsFrameResult` 的值字段。

- 插入保持确定顺序；
- 容量满后 `TryAdd()` 返回 `false`；
- 不自动覆盖旧事件；
- `Clear()` 只把 `Count` 归零并复用原存储；
- 不在热路径创建托管数组或集合。

P1 的 worker 如果遇到溢出，Debug 模式应立即失败；Release 模式应将溢出写入稳定错误码并拒绝静默丢弃。该策略将在 P1 结果提交层实现。

## 双缓冲发布与消费

每个 `AlsFrameExchange` 在构造时预分配两组 `AlsFrameInput` 和 `AlsFrameResult`。槽位使用 `FrameId & 1` 选择。

输入发布顺序：

1. 使用 `Volatile.Write` 将槽位标为未发布；
2. 将完整输入值复制到槽位；
3. 使用 `Volatile.Write` 发布对应 `FrameId`。

结果发布使用相同顺序。读取端先通过 `Volatile.Read` 检查发布帧号，再复制值并校验完整 `AlsFrameIdentity`。Commit 通过已消费帧号保证同一结果只能成功消费一次。

当前设计依赖 P1 的 Gather/Worker/Commit barrier，保证同一 parity 槽位不会在 reader 复制过程中被跨两帧并发覆盖。P1 必须为这个时序建立 Godot 集成压力测试。

## 槽位与 Generation

`AlsSlotRegistry` 在初始化时分配固定容量数组。`Acquire()` 返回 `CharacterId + Generation`，每次槽位重新获取时 generation 递增；`Release()` 只接受当前有效 handle。

因此：

- 已释放 handle 不能再次释放；
- 已释放 handle 不能通过 `IsCurrent()`；
- 同一 CharacterId 被复用后，旧 generation 永久失效；
- 容量耗尽时显式抛出 `InvalidOperationException`；
- generation 到达 `uint.MaxValue` 后回绕到 1，0 永远表示无效 generation。

P1 创建 `AlsFrameIdentity` 时必须使用 registry 当前 handle，角色销毁只能在 Commit barrier 后释放 handle。

## 确定性数学

`AlsMath.NormalizeAngleRadians()` 使用 `MathF.IEEERemainder` 将角度归一化到 `[-pi, pi]` 语义，并把边界 `-pi` 统一为 `pi`。

`AlsMath.DamperExact()` 使用解析指数衰减：

```text
target + (current - target) * exp(-smoothing * deltaTime)
```

该形式在相同总时间下对 30 Hz 单步和 60 Hz 双步产生相同结果，适合作为后续 locomotion 平滑的基础。

## 验证结果

统一入口：

```powershell
.\scripts\verify-p0.ps1 -GodotExecutable <Godot 4.7.2 Mono console executable>
```

验证内容：

1. solution restore；
2. solution build；
3. 全部 `Als.Core.Tests`；
4. Godot headless 加载 `headless_smoke.tscn`；
5. Godot 主程序集引用并实例化 `AlsFrameIdentity`；
6. 检查 `GODOT_ALS_P0_OK` 标记。

完成时共有 16 项核心测试，构建为 0 warning、0 error。零分配测试在 100 次热身后执行 10,000 次事件与帧交换循环，`GC.GetAllocatedBytesForCurrentThread()` 的差值为 `0 B`。

## P1 前提

P1 多线程分发 Harness 必须在不改变本阶段公共合同的前提下完成：

- 主线程 Gather 负责构造和发布 `AlsFrameInput`；
- 每个角色 worker 只读取匹配身份的输入并发布结果；
- 主线程 Commit 只消费匹配身份且未消费过的结果；
- process group barrier 必须保护双缓冲覆盖时序；
- worker 活跃时反复创建、移除和复用槽位；
- 单线程与多线程回放比较状态、事件和 Root Motion；
- Debug 开启线程访问检查；
- 热身后继续保持 0 B/frame 托管分配。

# 原地旋转播放器与动态循环

接续 `68ed48a`。开始核对原 Standing/Crouching Locomotion 图，发现两图共 34 个 Sequence/BlendSpace 玩家节点中，四个 RotateInPlace SequencePlayer 都动态绑定 `PlayRate` 与各自的 `bRotatingLeft`/`bRotatingRight` 循环标志。其他移动播放器、状态图和回调本批尚未编译。

## 实现

- `AlsRefactoredSourcePlayerInput` 增加可选 `bool? Looping`。每次 Prepare 把非空输入传给共享 Sync/时间运行时；空值保留资源定义行为。循环标志不成为持久历史，也不触发初始化或创建第二时钟。
- `AlsRefactoredRotatePlayers` 编译四个原播放器：Standing 左/右 property 12/9，Crouching 左/右 20/17；精确原动画路径、完整嵌套图、无 SyncGroup、起点 0、原播放率 scale/bias 政策、无 callback，动态绑定来自 Parent.RotateInPlaceState。
- 提供显式 host player ID 绑定及逐帧输入构造，保留独立左右身份。状态相关性、权重和重入请求仍由未来 stance 图提供；没有直接驱动普通 Demo。

初步全文正则摘要漏掉同一行后续绑定，误认为只有 Crouching 动态循环。实际完整嵌套图解析与首轮测试证明 Standing 同样绑定左右循环标志。已纠正生产编译器预期，没有修改资源。

## 验证

`artifacts/refactored-rotate-players/rotate.trx`：初轮 3 通过、1 失败；`binding.trx` 保留 Standing 实际绑定的诊断失败。最终 `related.trx`：157 通过、0 失败，包含新增六项以及共享播放器和四武器原生回归。

新增测试覆盖四个实际旋转动画的正/反播放、跨末尾循环、关闭循环后端点保持、重新打开循环、逐帧取消重试；另校验两个原图的 property ID/左右动态绑定及八种政策/回调/绑定变异拒绝。没有新 UE 连续旋转 oracle，端点预期依据现有共享时间运行时和实际资产长度；不能据此声称整个 RotateInPlace 状态机已完成。

`dotnet build GodotALS.csproj -p:Optimize=true --no-restore -v quiet`：0 warning、0 error。没有 UE/Godot 场景、全量、性能或打包验证。

## 后续

继续编译其余 30 个 Standing/Crouching 播放身份、动态输入与 state/cache/callback 图，接入真实移动更新和 pose，再形成统一宿主。13/13 Overlay 图仅已有受控输入连续证据；普通 Demo 仍未切完整 Refactored 链。Ragdoll/Get-up/Pose Recovery、Mantle gameplay、Turn/dynamic transition 调用端、完整相机、十分钟性能等所有旧缺口保留。用户改动未动；音频、道具物理及头颈诊断仍暂缓。

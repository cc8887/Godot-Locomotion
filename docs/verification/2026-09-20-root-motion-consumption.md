# 同帧 Root Motion 与碰撞移动

本批在 `.` / `main` 接通共同 Montage 的同帧准备、Motor 消费和
之后的脚部查询。没有新增项目副本。上一批 `4071505` 只提取运动；本批在
实际 CharacterBody3D 中消费，并用原始 Roll、真实场景碰撞、多角色与故障边界验证。

底层准备阶段已用于普通完整图；**碰撞消费目前由 `--montage-root-motion`
启用**，尚未加入普通 Demo 的默认配置。普通 R 仍为原地预览；启用该参数后
R 为带位移的翻滚测试。完整 Refactored Roll 的门控、目标转向、落地触发与
空中 Ragdoll 规则尚未完成，不能把测试开关视为完整玩法交付。

## 执行顺序

完整分层/Refactored 脚部模式使用下列 process-group 顺序；旧诊断管线不改序。

| 顺序 | 线程 | 工作 |
| --- | --- | --- |
| -1 | Main | 普通 Demo 捕获本帧输入 |
| 0 | Worker（Single 模式为 Main） | 推进共同 Montage，提取并缓存本帧根位移 |
| 1 | Main | Motor 消费位移、碰撞移动、采集最终物理输入 |
| 2 | Worker | 完整图准备；复用同一次 Montage tick；产生脚部查询 |
| 3 | Main | 查询当前物理世界 |
| 4 | Worker | 完成脚部/最终姿势、结果发布 |
| 5 | Main | 提交诊断、道具、事件和 Motor 检查点 |

生命周期与观测阶段相应顺延到 6 / 7。准备阶段只读写值状态，不访问输入
设备、Skeleton 或物理世界。Root Motion 与动作摘要、Notify、姿势共享同一
物理实例和区间；不增加独立时钟，不重复采样已缓存的根运动。

尚未完成完整动画的同一输入重试时保持原 delta，Motor 不再次积分。
暂停准备阶段会阻止新 Motor 帧，而不是套用上一帧位移。语义停用清除准备
候选；换代时若已经有物理输入，则直接发布该输入，避免等待第二次推进。
物理仍归 Main 所有：动画求值失败会保持已完成的 Motor 输入并重试动画，
不是回滚整个物理世界。

## 消费算法和边界

对照本机 UE 5.9 `USkeletalMeshComponent::ConvertLocalRootMotionToWorld`：
先使用实际导入骨架相对角色的变换与轴系逆转换得到 component-to-character，
再计算新 component / actor 变换。保留 mesh pivot 偏移引起的平移，不把
mesh-local 分量直接当作角色世界前方。Core 用例覆盖旋转、尺度和偏移 pivot。

有根运动时跳过普通水平速度积分，但保留输入加速度、模拟量和 Character
参数更新；运动位移除以本帧 delta 后送入 `MoveAndSlide`。下落保留重力速度；
角色旋转模型收到真实 HasRootMotion。物理输入及结果明确标记 AnimationDriven，
停止提取后回到 MotorDriven。旋转在平移碰撞后应用，当前直立胶囊拒绝倾斜
根旋转；本次原始 Roll 没有非零根旋转，未认证任意六自由度动作。

Godot 碰撞不是 UE CMC 的完整替代：台阶、坡面、移动平台、离地继承速度及
Mantle 的模式切换仍需后续原生行为对照。现有性能采集尚不能代表新增准备
阶段加完整图的最终十分钟预算，本批不签发性能证书。

## 验证

证据根目录：`artifacts/root-motion-consumption-20260920`。

- Godot 构建 0 warning / 0 error。Core Release 2564 项通过，按现有规则排除
  两组独立历史 P5A fixture（`core-final.trx`）。Import Release 2286 通过、
  1 项既有跳过（`import-final.trx`）。
- 30 / 60 / 120 Hz 真实 R/X 输入分别运行 120 / 240 / 480 帧，覆盖接受、
  替换、取消与完成。空地每个根运动帧的实际水平位移与同帧世界 delta 之差
  小于 0.0002 m，前进方向正确。完整输入序列最终前进 3.55915 / 3.59291 /
  3.60574 m，包含停止后普通运动的减速；不是单次 Roll 的距离证书。
  日志 `free-30.log`、`free-60.log`、`free-120.log`。
- 60 Hz 墙体测试：同样输入最终前进 0.649892 m，77 帧受阻，未越过固定
  胶囊与墙面允许的 0.66 m 上界，见 `wall-60.log`。
- 实际运动中暂停 `MontageMotionPrepare` 三帧，Motor 积分计数不变，恢复后
  位移及动作区间一致，见 `motion-stage-hold.log`。
- 连续两次 BeforePublish 失败，同时替换动作：Single / Parallel 都保持物理
  输入与坐标、不二次积分，见 `failure-final.log` / `moving-failure.log`。
- 带运动的 pending / held 停用、generation 回调退役、完整请求换代与 Commit
  等待通过，见 `deactivation-pending.log`、`deactivation-held.log`、
  `generation-callback.log`、`action-lifecycle-final.log`。
- 原键鼠回归 360 帧通过（`keyboard.log`）。原地配置十角色 Single / Parallel
  各 3621 帧，摘要与上一批完全相同：pose `CEC4EC705E945A65`、root
  `E030B6049AEDDCE1`、result `687F8C31B8CE2B4C`，见 `ten-staged-*.log`。
- 开启实际碰撞消费后，十角色两种模式仍各通过 3621 帧，包含两次调度取消、
  一次提交等待及 Overlay 切换；三份摘要一致：pose `B4508FA2A28E2C6C`、
  root `81A75CF9FB938426`、result `73C8E7996429124D`，见 `ten-moving-*.log`。

有图运行保存 `roll-0120.png` 至 `roll-0204.png` 八张画面，检查俯身、翻滚、
起身阶段。测试使用跟随近景相机与补光，不修改正式相机；截图为采样时最近
已渲染画面，不用于证明逐帧原生姿势误差。`render-final.log` 同时通过位移断言。

## 首错与下一步

首次“自由位移”测试在普通出生点前进至约 0.63 m 被场景坡道边缘挡住，
不是区间或轴向错误。保留 `moving-input-60.log` / `moving-input-diagnostic.log`；
随后将该测试角色移到同一场景空地，自由测试通过，墙体作为单独障碍测试。

首次完整换代测试等待超时（`action-lifecycle.log`）：准备就绪检查误拦截了
换代过程中已完成物理计算、只等待重新发布的输入。现仅对新物理积分要求
准备结果，保留换代的原输入转交，重跑通过。没有放宽其计数或身份断言。

下一项实现 Refactored Roll 玩法：地面/同 Montage 忙碌门控、目标朝向、
半衰期转向、动作姿态、落地触发，并接普通入口；随后继续 Mantle、
Ragdoll/Get-up/Pose Recovery、完整 Camera、全地形观感和十分钟预算。
空中打断必须与真正 Ragdoll 联动，不能以普通 Cancel 替代后宣称完成。

手动检查消费原型：

```powershell
& '<Godot-4.7.2-console.exe>' --path . -- --montage-root-motion
```

仍从 `scenes/demo/als_demo.tscn` 启动；WASD / 鼠标控制，R 翻滚测试，X 取消。

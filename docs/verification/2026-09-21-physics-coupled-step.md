# 整链接触、关节和投影的同输入原生对照

在 `.` 的 `main` 推进，承接约束排序后的休眠失败。本批完成求解阶段对照及采样入口，没有修改物理公式、休眠阈值、迭代次数，也没有接通普通 Ragdoll。

## 结果及边界

六个实际 Godot 状态、每个 24 阶段，共 144 阶段，在相同预测 COM 姿态、速度、惯量、接触行和约束顺序下，与 UE 5.9 原生接触/关节容器对照通过。

| 场景 | Hz | 模型/帧 | 身体数（含环境） | 关节数 | 接触对数 |
| --- | ---: | --- | ---: | ---: | ---: |
| 平台平移前静止 | 30 | AnimMan / 33 | 34 | 19 | 13 |
| 平台平移前静止 | 30 | Mannequin / 33 | 32 | 17 | 12 |
| 平台平移前静止 | 30 | Mannequin / 294 | 32 | 17 | 12 |
| 实际 World 普通落地 | 120 | AnimMan / 33 | 34 | 19 | 0 |
| 实际 World 普通落地 | 120 | Mannequin / 33 | 32 | 17 | 0 |
| 实际 World 普通落地 | 120 | Mannequin / 1194 | 32 | 17 | 13 |

24 阶段：8 轮接触/关节位置修正、隐式速度、2 轮接触/关节速度修正、提交第一次修正、统一缓存后投影、提交最终修正。

Import 回归从输入重新创建 Core 求解器，只以 `nativeSamples` 为预期值。最大差值：DP `1.392008e-7 cm`，DQ `2.383986e-8 rad`，线速度 `8.067572e-6 cm/s`，角速度 `8.515854e-7 rad/s`，预测位置 `1.744236e-7 cm`，归一化旋转 dot 偏差 `5.551115e-16`。
另检查真实 Godot 采样输出与 UE 输出：最大 DP `1.796539e-7 cm`、DQ `1.757633e-8 rad`、线速度 `4.221279e-5 cm/s`、角速度 `5.822279e-7 rad/s`，也在既有阶段容差内。

旋转比较先归一化两端：源 particle 的 float 存储有长度误差，未归一化的 `1-|dot(q,q)|` 会把相同旋转误报为差异。首轮断言失败保留；修复角度度量，没有放宽容差或改写物理输入。

这些样本未发现足以解释休眠失败的共同求解偏差。此对照固定 Gather 后输入，不覆盖原生窄相、积分、接触保留、冲击传播、休眠或实际岛生命周期；shock 保持输入契约的关闭状态。不能称完整轨迹等价或全部状态无偏差。

## 实现与复现

- `IAlsIslandStepObserver` 借用只读 span，接收实际迭代次数和顺序；关闭时无每步分配。异常阻止身体/历史发布并允许重试，步内禁止更换 observer 或重入。
- `AlsWorldContacts` 在待提交阶段暴露已 Gather 接触行的只读副本和实际求解次序。
- `AlsIslandStepCapture` 仅在诊断 Main 路径按指定帧写新文件，记录 COM 输入、原生有效关节设置和结果，不覆盖已有文件。`Complete` 代表求解完成，不保证之后历史提交成功。
- UE 导出器创建独立 native particles、接触和 cached joint 容器，共享原生 solver bodies。原生 Gather 分配接触行后，只替换捕获的输入，原生重新计算质量和所有输出；检查接触顺序未变。不复制求解公式，也不读取旧 Core 输出作为求解结果。

Godot 场景 `res://scenes/tests/physics_core_joint_replay.tscn`，参数：

```text
--chains --drop --sleep --scene-world --platform=translate --hz=30
--capture-step=33,294 --capture-directory=<新的绝对目录>
```

120 Hz 实际 World 去掉平台参数，改为 `--hz=120 --capture-step=33,1194`。目录须预先存在；休眠角色跳过物理步时不生成采样。

完整 UE Editor target 构建及审计后，commandlet 参数：

```text
-run=AlsGodotExport
-PhysicsCoupledInputs=<平台采样绝对目录>|<实际World采样绝对目录>
-PhysicsCoupledOutput=<新的绝对JSON路径>
-unattended -NullRHI -nosplash -stdout -FullStdOutLogOutput
```

inputs 整体作为一个进程参数，避免 shell 解释竖线。最多 64 个采样文件，排序输出。
Import 回归类：`AlsPhysicsCoupledStepReferenceTests`。

原生参考 `assets/config/v4_physics_coupled_step_reference.json`，7,165,017 bytes；两次冷导出 SHA256 一致：
`0EE024BDE36FB542E47A19BB69C94A4E01F6764EB26325C45B7BFC897AEA8620`。
文件保留源采样和旧 Core 结果供诊断，断言预期值来自 UE 原生计算。

## 验证和保留失败

- Core Release 2712 通过，沿用两个旧 golden/schema 类排除；新增 6 项 observer 契约检查。
- Import Release 2365 通过、1 项既有条件跳过。
- Godot 优化构建、旧 144 对 × 12 帧、60 Hz 接触探针通过（31 精度、6 几何、5 休眠生命周期、3 动态场景）。
- UE 完整 Editor target、插件审计、两次最终冷导出、普通 Editor 冷重启均退出 0；脚本确认 exporter 类加载。DataValidation 0 error、3 个已有警告。三个导出器源文件主仓库/UE 本地副本哈希一致。
- 开发中四元数构造编译失败、临时接触所有权导致次序检查失败、目录枚举覆盖前批导致只导出 3 项，均已修复；失败日志及不完整文件保留，不作成功证据。
- 30 Hz 平台采样运行仍在第 300 帧因前段未全部休眠失败；120 Hz 普通落地仍因未休眠失败。未改变生产求解公式，不重判前批四落地/两平台失败，未重复未改动的全部频率场景。
- 用户的 `2026-08-28-p4-aim-layering-foot-placement.md` 修改保持不动。

产物：`artifacts/physics-coupled-step-20260921/`。前批偶发 UE 关闭后访问冲突尚未定位，本次退出正常不能宣称已修复。

## 下一步

保留现有休眠失败门槛，补真实形状的原生接触生成/保留对照及动态接触层级质量缩放。当前 Jolt 几何查询与原生 cull distance、持续流形、接触位置并不等价；不能靠扩大 query margin 代替。
在共同求解输入已对齐的基础上定位首个接触输入差异，再验证其对连续轨迹的影响。
随后继续普通角色动画到物理 owner、pelvis/胶囊/相机交接、Get-up/Pose Recovery；Mantle、完整 Camera 和最终十分钟性能预算仍未完成。

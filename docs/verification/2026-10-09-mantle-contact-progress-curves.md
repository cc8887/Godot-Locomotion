# Mantle 专用曲线与固定支撑验证（2026-10-09）

本批延续用户指出的抬升节奏、滑手和穿模问题，实现专用烘焙和真实平台局部支撑。Root 关闭下的身体前移／抬升不再使用同一线性进度；支撑 wrist 位置和 palm 朝向通过，但候选手指蒙皮仍穿模，完整动作质量未通过。

## 实现与资产边界

纯脚本 `mantle_contacts.py` 对最终 pose／Root 输入烘焙每个实际 sequence 的前移、抬升、负剩余高度、左右手权重、固定 wrist／palm 锚点。Import 绑定输入字节摘要、完整 sequence 清单及原始关键帧时间。Core 复用已有 Distance Matching 反查起始高度、组件姿态和双骨 IK；Godot 只查询并运输 ledge 平台局部基底，最终姿态与既有角色事务共同提交／取消。详见[稳定合同](../architecture/als-mantle-contact-control.md)。

当前原版和活动 Mantle v4 分别补烘焙三项曲线到 `assets/generated/mantle_contacts/`，未来 `publish stage` 自动登记包配置 payload，正式激活验证对应的最终输入。默认完整 pipeline 在存在生成 Mantle 时，还会在普通 E2E 后执行静态接触／蒙皮矩阵和平台／高度／取消矩阵，失败纳入 `runtime_failed`，且继续执行独立场景。

本批未重新扩散生成动画、重写 FBX／原版 raw／既有包或切换活动指针。包 SHA256 保持 `cdb8302940e41603c326eca5feca547c53f92a05b25a76e31c8a31207f2b1906`，829 个载荷不变。仍是四个 Low 替换身份、High 原版 fallback 的诊断包，`productionReady=false`。自动 pipeline 的新增门禁分派做了负向单元回归；本批没有重新执行整链生成／重定向／导入／71 项验收。

## 最终受控检查

证据根目录：[mantle-progress-20261009](../../artifacts/kimodo_als/mantle-progress-20261009/)。

| 检查 | 结果 | 证据 |
| --- | --- | --- |
| Optimize 构建 | 0 warning／error | `build-contact-final-v2.log` |
| Release Mantling Core＋帧交换／热路径零分配／双骨 IK | 61/61，含 40 项 Mantling 与新增 1,000 次零分配手锁定 | `core-final-v2.log` |
| Release Import motion／contact | 7/7，保留原 4,536 项 native motion 比较 | `import-contact-v1.log` |
| Release Import Mantling 完整相关集 | 94/99，5 个原有 HostPose DefinitionDigest 不匹配失败 | `import-final-v2.log` |
| Release 原有 Import hand IK 原生／零分配相关集 | 16/16 | `import-hand-ik.log` |
| Python 管线／独立 FBX 回读／Root 所有权／Mantle／曲线及新分派 | 62/62 | `python-final-v4.log` |
| 原普通 Demo、无环境包覆盖 | exit 0，有限 180 帧启动，活动 v4 被选中且无 ERROR | `demo-default.log` |

没有跳过失败、修改原 native 比较阈值或更改 2 cm 蒙皮门禁。首次 Python 扩展检查因没有加载隔离 `ufbx` 而 3 项报错，保留 `python-final.log`；配置既有 `artifacts/kimodo_als/offline-python-deps` 的进程级 PYTHONPATH 后再跑通过，未修改 Kimodo 虚拟环境。

## 实际 Godot 场景

复用 `res://scenes/tests/mantling_demo_smoke.tscn`，不用 computer-use／UE。最终静态矩阵 `scene-v3` 运行九项：候选 Root 关闭的 30／60／120 Hz、Root 开启 60 Hz、单线程 60 Hz、候选／原版两项 GPU、候选／原版旧线性负对照。3,420 帧均观察实际提交和发布身份，120 张 PNG。实际 actor yaw 九项均为 0；六项启用接触控制的完整支撑检查均通过，全部九项仍因完整蒙皮而失败。

| 候选 Low 60 Hz 策略 | 支撑手高度变化 | 前后滑动／漂移 | 全动作最大穿模 |
| --- | --- | --- | --- |
| 旧统一线性移动 | 46.109 cm | 原负对照保留 | 19.627 cm |
| 分别使用前移／抬升曲线，关闭手锁定 | 约 2.24 cm | 前后约 6.60 cm | 9.077 cm |
| 曲线＋固定支撑 IK | 0.0003 mm | 三轴约 0.0004 mm；绝对锚点误差 ≤0.000384 mm | 9.077 cm |

最终六项支撑门禁：锚点位置 ≤1 mm、掌向误差 ≤0.1°、可达残差 ≤1 mm；实际位置误差最大约 `3.84e-7 m`、旋转约 `3.1e-5°`、残差 0。Root 开启仍使用原路径且未施加新手锁定，支撑高度变化约 2.45 cm；它不属于六项接触通过的范围。单线程与并行 60 Hz 的逐帧报告字节一致。

额外最终矩阵 `contact-cases-v2` 运行七项、2,520 帧：0.75 m Low、移动／旋转 Low、原版 fallback High、移动／旋转 High、支撑中取消、支撑中销毁、仅曲线不锁手负对照。运行与接触／生命周期策略 7/7；其中四项是完整支撑验证，另外三项验证解除和禁用策略。移动 Low／High 的最大平台局部漂移约 0.0457／0.1272 mm。取消／销毁使用 0.75 m 动作进入已抓沿时刻后触发，各有 20 帧实际施加支撑、9 帧完整支撑，解除后 0 帧继续施加锚定。两份最终报告保存的包／载荷／配置／程序集在各自执行期间未变化；最终汇总见 `comparison-summary.json`。随后给额外 runner 增加可传入 Godot 可执行路径的 CLI 适配，分派单元回归验证了该调用；没有重做其全量游戏矩阵。

## 保留的失败与定位

候选完整支撑期间的 `middle_03_l` 蒙皮仍有约 5.114 cm 穿模；此时 wrist 和 palm 锚定通过，问题属于手指局部姿态／接触几何。释放后约 montage 0.8 s 同一指节达到 9.077 cm。原版新控制的全动作最大约 3.787 cm，支撑期间约 0.600 cm。High 的自由／已释放手等也未达到整个蒙皮的 2 cm 门禁。不能将 wrist 固定当成完整手部无穿模，也不能仅延长手锁定曲线隐藏错误指节姿态。

已有限查看最终 GPU 第 60 帧抓沿和第 78 帧释放；不是全动画人工验收。下一步针对生成动作的手指接触姿态、释放／提腿阶段避障继续修复并重新验收。未执行新整链、完整 71 项、长时间、性能或实体键鼠人工验收。

初始 `contact-candidate-60-v1` 只使用模型根作为 IK 组件，导致约 20 cm 手部漂移；修正为捕获 FBX 骨架相对模型的静态变换并组合本帧变换，`v2` 确认固定支撑。`build-contact-v1.log` 保留捕获 `in` 参数的编译错误，修正局部值快照后构建通过。`scene-v1` 因泛用 montage 名称未解析实际 Low_Right sequence 漏掉时序观察；`scene-v2` 的掌向诊断遗漏骨轴→FBX 转换，产生错误的朝向失败；最终 `scene-v3` 修正诊断，保留真实蒙皮失败及先前证据。

## 复现

```powershell
& C:/Users/yuchencui/.dotnet/dotnet.exe build GodotALS.csproj -p:Optimize=true
& G:/Github/kimodo/.venv/Scripts/python.exe -m tools.kimodo_pipeline.mantle_contacts --config assets/config
& G:/Github/kimodo/.venv/Scripts/python.exe -m tools.kimodo_pipeline.mantle_contacts --config assets/generated/als_nonprop_20261009_mantle_v4/config
& G:/Github/kimodo/.venv/Scripts/python.exe scripts/verify-kimodo-mantle-scene.py --pack res://assets/generated/als_nonprop_20261009_mantle_v4/resource-pack.json --output artifacts/kimodo_als/<全新静态证据目录> --include-legacy
& G:/Github/kimodo/.venv/Scripts/python.exe scripts/verify-mantle-contact-cases.py --pack res://assets/generated/als_nonprop_20261009_mantle_v4/resource-pack.json --output artifacts/kimodo_als/<全新平台证据目录>
```

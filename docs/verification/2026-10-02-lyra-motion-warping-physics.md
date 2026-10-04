# Lyra Align/SkewWarp 真实 ALS 角色移动

2026-10-02，主目录实施。四个原 MotionWarping 模板已接 `LyraCharacterAnimation.MoveCapsule`，使用实际 ALS component、当前胶囊 feet 和实际 Montage bank context。人物仍为 ALS 68 skin / 81 logical，14 个 Linked Layer 入口继续共享角色内组实例，最终骨架只发布一次。

本批关闭指定模板的显式组件→真实胶囊接线及下述门禁。原 GA/Pawn/ShooterCore 已读配置没有添加 MotionWarping 或提供 Align，因此默认 Shooter 入口不补组件/目标。原 GA_Emote 激活/移动取消没有在本批实现或执行，整个 Lyra 目标继续开放。

## 执行语义

`LyraMotionWarpingProfile` 共享不可变原窗口，加载时校验依赖字节哈希、四 Montage、原 SkewWarp 类及当前 Align/Linear/IgnoreZ/Feet/Default-Slerp 等配置。独立原生 root-at-end 数据参与 Static warp-point offset，不能从 warped 输出反推输入。

`LyraMontageMovementReader.WarpContext` 使用 Advance 后的真实 root owner、DeltaTimeRecord.PreviousPosition、当前 Position、CurrentWeight 和 EffectivePlayRate。提取区间可能属于已清除的旧 owner，姿态时间也可能有末端偏移，二者均不能替代 context。

组件只允许实际 CharacterBody3D 在主线程空闲物理边界显式挂载。typed Set/Remove/Disable 请求进入候选；可跟随实际 Node3D 的世界变换，删除/离树/排队删除时移除目标。当前 shape 适配要求单个居中直立 CapsuleShape3D。Bone/socket offset 跟随及跨世界语义仍待。

当前 visual root 为 actor 位置减 actor up 乘当前胶囊缩放后半高；初始 base visual offset 在挂载时缓存，蹲伏/换类不重写。实际 root 存在时扫描原窗口并更新 Warp；无 root 时接受目标命令、保留 modifier，不制造额外窗口 tick。

Warp 位于局部 root 转世界之前，结果进入原 motor 唯一一次 MoveAndSlide 和其后的 root rotation。物理移动前预校验，成功后提交目标/modifier 历史；移动前失败取消候选。Main 后续取消保留已发布移动和 Warp receipt，同物理帧重试只重新准备动画与同一原 Montage tick。目标、胶囊和该历史属于角色物理服务，Linked Layer 不新增角色时钟或移动发布点。

## 原生与实际运行

加强 [前批组件 fixture](2026-10-02-lyra-motion-warping-component.md)：除 Seek 外，108 轨迹 / 15,120 帧由实际 Godot Montage bank 接受原 play/stop/rate 请求后自行 Advance。context、root presence/local root 逐值同原 UE，实际 context 再驱动组件。两构建全部 120 轨迹 / 16,800 帧 / 3,030 非零 Warp、modifier 历史及取消重试仍通过；最大 P/Q/S 差为 `5.859285502108464e-14 cm / 4.577566798522237e-16 / 0`，原门槛不变。

上述参考的 actor/component 仍使用原记录输入，剩余 1,680 Seek 帧也使用记录 context。不能据此称整个 Main/UE 世界物理原生等价。当前 Montage seek API 专用于 Mantle，Lyra 通用 Seek 接入仍待。

新 `lyra_motion_warping_physics_smoke.tscn` 使用六个实际 LyraSceneCharacter、ALS 模型、完整 Main/最终 Rig/五 Slot bank、共享源资源和 Jolt。四阶段分别播放四个原 MW Montage；authored root 为 identity，非零移动来自新 Warp，未注入人工非零 root。

角色覆盖静态目标、移动/旋转目标、缺失后迟到目标、目标销毁、实体墙、root pause、蹲姿半高，以及停止/倒放/重播/DisableExisting/三 Provider 重绑。静态和移动目标在四窗口的开放空间均到达；缺失或销毁禁用窗口，迟到目标不复活原已禁用窗口。

每角色每帧在 Warp Prepare 后、MoveAndSlide 前注入非有限速度失败，检查 body/目标/modifier 未发布，再重新移动。移动后取消完整动画，检查 actor、motor 次数、Warp receipt/历史保持，随后重新求值提交。拒绝旧候选、重复移动与物理进行中的目标命令。

| Hz | 唯一移动 / 每种 retry | 非零 Warp | 实体阻挡 | 禁用 modifier | root pause | 开放到达 |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 30 | 1,440 | 264 | 44 | 294 | 12 | 8 |
| 60 | 2,880 | 522 | 85 | 582 | 24 | 8 |
| 120 | 5,760 | 1,034 | 165 | 1,160 | 48 | 8 |

每构建 10,080 移动前失败 retry、10,080 动画取消 retry、40,320 拒绝；每频率四次目标变化/四次目标销毁。Debug/实际 ExportRelease Optimize 的完整报告及 actor/context/modifier/world/collision 摘要逐频率同。角色在单物理线程交错执行，不是动画 worker 并行验收。

两构建各七 Godot 进程退出 0、无 ERROR/WARNING；含上述原生/三Hz、既有六角色 root/Jolt 五受控案例/迟到帧、武器原生/六角色、普通十角色。普通十角色每构建 4,800 移动/发布的完整报告与 root-final6 冻结基线同，默认 `motionWarpingConsumer=false`。显式挂载场景确已消费组件；false 只描述默认入口。

Core Release 相关 193 项通过、0 失败/跳过；两最终构建 0 错误/警告，Optimize 后六 Debug DLL/PDB 逐 SHA 恢复。额外实际 OpenGL/RTX 5080 进程完成 60Hz/480 帧、退出 0、无错误警告，完整结果与 headless Debug 60Hz 同。三个 FramePostDraw 截图已抽查：六人物和墙体可见、姿态变化；远景不承担接触/握持/材质近景验收，文件名是截图请求帧。

851 旧 JSON、709 原 UE 包、项目/Config 哈希保持，当前仍为 852 JSON。本批无 UE 启动/构建/导出/资产保存，临时探针未重新安装。较窄的首轮 60Hz 结果保留为 `motion-warping-physics-first.log`；后来加强四窗口到达断言，最终新日志/报告未覆盖早期证据。

汇总：`artifacts/lyra-analysis/warp-physical-verification.json`。矩阵日志 `warp-physical-{debug,optimize}-final.log` 记录实际 DLL SHA 和逐进程退出码；资源仍在 ignored 目录，只有代码的检出不能运行完整场景。

完成对应构建后复跑：

```powershell
.\scripts\verify-lyra-motion-warping-physics.ps1 -Configuration Debug -RunTag newtag
.\scripts\verify-lyra-motion-warping-physics.ps1 -Configuration Optimize -RunTag newtag
```

脚本保护已有证据，Optimize 暂换程序集后恢复 SHA；`tools/verify_lyra_motion_warping_physics.py` 审计本次 final 结果。

## 下一阶段

原 GA_Emote 激活/移动与蹲伏取消、完整 NotifyState/命名事件及自定义 MotionWarping delegate、Bone/follow offset、通用 SkewWarp/Seek、整个 Main 连续原生对照、Shotgun/Feminine 完整 Main、通用多 Group/self/Unlink、复杂地形/移动平台、近景握持/材质/性能继续开放。动画线程并行及跨帧不可恢复失败不由同帧 retry 证明。音频、道具物理与头颈专项继续暂缓；整个目标不标为完成。

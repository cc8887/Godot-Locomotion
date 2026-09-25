# Rest Parent 原生连续刷新对照

新增 `ExportRestParentTrace` 和只读Python场景生成器，在独立临时GamePreview世界创建原Parent类，向真实字段写入受控输入，并由真实线程池worker调用原InitializeTurnInPlace、RefreshRotateInPlace、RefreshTurnInPlace、RefreshDynamicTransitions。主线程等待worker结束后读取真实状态。无Montage创建或候选请求消费，没有保存场景/资产。

每帧显式清除原三个UpdatedThisFrame标记，保持与外层NativeUpdate的帧边界相同；原函数自行执行计时、阻尼、阈值、去重、两次后续刷新延迟及候选选择。非游戏世界分支仅切换这个临时世界的类型，执行后恢复，不改Editor主世界。

30/60/120 Hz三条16秒轨迹，共3360帧。前八段覆盖Standing/Crouching的90/180左右转身，中间覆盖Rotate Aiming/第一人称阈值、速度范围和Moving门控，后段覆盖脚部锁定/距离/缩放、双脚平距、未知stance、未调用Dynamic刷新、重复调用及非游戏世界退出。

## 结果

- 原生三组测试全部通过，无首轮失败；TRX `artifacts/tests/rest-parent/rest-parent-native-initial.trx`。
- 30Hz480帧、60Hz960帧、120Hz1920帧：Rotate速率最大差0，Turn等待时间最大差0。左右旋转布尔值、Dynamic延迟、八个Turn候选和四个Dynamic候选、Slot、播放参数逐项相同。
- 每帧取消并重复计算，候选完全相同。首次输入后保留的排队请求与后续覆盖也参与连续比较；没有把输入不满足条件误当成清除旧队列。
- Godot Optimize构建0警告0错误；测试项目Release构建0警告0错误；diff whitespace检查通过。

## UE 证据

- 完整Editor构建前缀 `20260925T081733829Z-82cb4ab6cd854656bcdaef1e68c7e4e2`，11actions退出0。UBT还按其依赖状态重建NetCore并更新引擎版本产物；随后四项目插件完整审计全部通过，未手改BuildId或复制DLL。
- 最终BuildId `b4127720-ddfd-475f-a955-59a24fb7ace6`，fingerprint `D949664C590C5B7035C5B14BE317133D029C6B6F2B6DD488CFDCE3695F1C5A55`。
- 冷导出PID28316、普通Editor PID10132，原Process对象读取实际退出码均0。日志 `refactored-rest-parent-cold.log`、`refactored-rest-parent-normal.log`有3360帧成功标记。
- 两份2814734字节输出SHA256均 `93F2E838D97A8949BBD9E03D653D2D8DD63377AB001AFE4F104C3F49133B37BD`。
- 新C++源与项目镜像SHA均 `1C45F098C519B40D9196F071E6A9602D7D1DE28CA584A0BEE2DCBA62D80F9FA3`。
- DataValidation PID43092实际退出0，0 errors、3条既有警告；新增反射声明头文件和项目镜像SHA也一致。
- 普通Editor保留两条旧Condition failed和PawnActionsComponent/Navmesh/LineSet/MotionVector/Crowd警告。本批没有称其修复，没有打包。

## 证据边界

本批验证原函数输入→连续Parent状态→未消费的候选请求，TurnPlayRate仍为未消费状态；没有执行NativePostUpdate后的Turn接受/惯性退出、完整Standing图、控制角色输入、物理或Godot渲染。旧共享队列/Slot本地测试和QuickStop原生对照继续有效，但不能用这些分段证据宣称完整角色1:1。

下一步把Standing图、Parent更新和动作/Slot消费放入连续统一宿主验证，并继续Crouching、普通Demo和最终视觉/性能。Ragdoll/Get-up等全部既定目标不缩减；用户未提交修改及音频/道具物理/头颈暂缓项保持不动。

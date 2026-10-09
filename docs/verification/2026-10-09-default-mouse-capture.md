# 默认鼠标捕获修复（2026-10-09）

## 原因与行为

`AlsOrbitCamera._Ready()` 原来在非 headless 启动时直接设置 `Input.MouseMode=Captured`，隐藏并限制指针。失焦时只清除相机的逻辑标记，未主动将实际模式改为 Visible；保存的捕获请求还会在再次获得焦点时重新应用。这能解释启动及回到窗口时出现鼠标被限制的情况。未通过桌面事件追踪确认用户所述的单纯悬停是否伴随系统焦点变化。

现在启动设置 Visible；Esc 保留主动捕获／释放开关。失焦同时释放逻辑状态与实际指针模式，删除焦点恢复时的自动捕获。不新增鼠标进入窗口／点击捕获路径。ALS 与 Lyra 共用相机，因此适用同一行为。README、相机帮助和主控切换提示同步更新。

## 验证

在主目录构建与运行，不改生成资产或活动指针。

- `dotnet build GodotALS.csproj -p:Optimize=true` 因机器只安装 SDK 10.0.300、项目锁定 8.0.100 而不能启动。保留 global.json，使用 `dotnet exec "C:/Program Files/dotnet/sdk/10.0.300/MSBuild.dll" GodotALS.csproj -p:Optimize=true -p:Restore=false -verbosity:minimal` 构建成功，无编译警告／错误。
- Godot 4.7.2 mono headless，`p4_keyboard_mouse_smoke.tscn`、fixed-fps 60：退出码 0，`P4_KEYBOARD_MOUSE_OK frames=360`。新增断言覆盖普通入口默认 Visible、主动捕获后模拟失焦释放实际模式、模拟焦点返回不捕获、显式 Esc 动作仍可捕获；随后四个实际引擎鼠标事件及 Alt/A/D 移动回归通过。当前活动 Mantle v4 诊断包替换四个身份。
- 同引擎 headless `p3_demo_input_smoke.tscn`：退出码 0，`GODOT_ALS_P3_DEMO_INPUT_OK actions=11 directions=12 camera_basis=1 pitch=1 aiming=1 cleared=1 hud=1`。既有两次 Esc 切换、鼠标相机方向和输入清理检查通过。
- 日志：`artifacts/mouse-capture-20261009/keyboard-mouse.log`、`artifacts/mouse-capture-20261009/demo-input.log`。

焦点通知在回归中受控注入，未执行图形窗口的人工失焦、悬停、锁鼠标或完整 Lyra／动画质量验收。此次无窗口验证不会夺取桌面鼠标。已运行的旧游戏进程需要重新启动才能应用新版程序集。

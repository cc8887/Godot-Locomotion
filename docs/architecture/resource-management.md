# 通用资源管理

资源身份、缓存、依赖与生命周期由纯 .NET 的 `src/Als.Core/Resources` 管理。资源包只组织和定位文件，不参与缓存身份；ALS、Lyra、Unreal 导出格式及 Godot 路径规则属于使用者或适配层。普通配置、任意不可变对象和派生数据使用同一套接口。

## 身份与加载

`ResourceId` 是稳定的逻辑 ID，不要求文件路径、包名或特定格式。每个 manager 内一个 ID 注册一种返回类型和一个 loader；编译选项或输出形式不同的派生资源使用不同 ID。版本号由 manager 在成功加载时分配，属于单个资源的内存版本，不是资源包版本。

`Register<T>(id, factory)` 注册不可变数据或派生资源。`IResourceProvider` 读取字节，`IResourceLoader<T>` 将其转换为类型化数据；另一个 Register 重载组合两者。内置 `FileResourceProvider` 和 `DelegateResourceProvider` 支持文件及宿主注入的读取接口。Godot 的资源操作仍遵守引擎线程要求。

`Acquire<T>` 返回 `ResourceLease<T>`，固定一个资源版本。manager 保持一份缓存引用；lease 和派生资源也持有引用。最后一个引用释放时调用注册的清理函数。资源应当不可变；角色播放状态、sampler、时钟和提交/取消事务由角色自行持有。

同步构建在 manager 锁内串行进行，同一资源的并发请求复用一次成功加载。首次加载失败不缓存异常，后续请求可以重试。当前没有异步加载、后台流式读取或跨进程磁盘缓存。

## 依赖与替换

factory 内通过 `ResourceLoadContext.Read<T>` 读取依赖，manager 自动记录依赖边并固定依赖版本。返回后的 context 不能继续使用，也不能跨线程传递。loader 内禁止修改注册表；循环依赖，包括指向已缓存派生资源的间接循环，均拒绝。

`Replace<T>` 先加载和验证候选，成功后替换单个 ID 的定义，移除它及传递依赖者的缓存。候选加载失败保持旧定义和缓存；成功后的新请求取得新版本。旧 lease 和旧派生结果继续持有原依赖，直到使用者释放。无关资源保留缓存。

`Invalidate(id)` 用于外部变化通知，移除这个 ID 及依赖闭包的缓存，下一次请求重新加载；它不预先验证候选。需要失败后继续使用现有缓存时，调用 Replace。覆盖相同路径、相同文件长度和时间戳也可通过显式通知处理；管理器不将修改时间/大小当作强一致的内容标识。

清理回调在所有权更新后执行；回调失败会汇总为 `AggregateException`，仍释放其余引用。Replace 的候选已成功发布后，如果旧版本清理失败，异常不撤销新版本。清理函数不应承担候选验证。

调用者可在现有角色提交边界安装新资源 lease；本次未为普通 Demo 增加自动文件监视、运行中角色重建或另一套提交链路。

## 普通配置示例

```csharp
using var resources = new ResourceManager("ui");
var sourceId = new ResourceId("ui/settings-json");
var themeId = new ResourceId("ui/theme");
resources.Register(sourceId, _ => ReadSettingsFromDisk());
resources.Register(themeId, context => CompileTheme(context.Read<Settings>(sourceId)));
using var currentTheme = resources.Acquire<Theme>(themeId);
resources.Replace(sourceId, _ => validatedSettings);
using var nextTheme = resources.Acquire<Theme>(themeId);
// currentTheme still pins the previous settings/theme versions.
```

此示例不依赖动画、ALS、Lyra 或 Unreal；需要复用字节加载时可注册 Provider/Loader。Core 单元测试另有真实文件 Provider 的同路径替换与 JSON 配置并发加载案例。

## 当前 Import 接入

`ImportResourceScope` 是同步编译操作的有界适配层。顶层 scope 拥有 manager；嵌套 scope 默认共用资源与修订表，必要时可指定 `shareWithParent: false` 隔离。兼容现有纯文本编译入口的当前 scope 仅保存在线程局部状态中，不跨 await 或 worker 传递。

普通入口 `AlsDemoEntry`、直接 P4 Demo、context 构建及 worker 图构建均建立或加入 scope。启动完成后释放缓存、注册 factory 和原文引用，长期保存的配置只引用已编译的不可变数据。worker 运行阶段不依赖启动 scope。

Refactored source player 原先在第一个 worker 帧内延迟创建，错过启动缓存并再次读取资源。现在在 production movement 构造完成、notify 绑定后创建，仍在角色移交 worker 之前；`animation.character_sources` 记录这个阶段。只提前构造，不推进动画帧、时钟、通知或提交/取消事务；每个角色仍独立持有 sampler 和播放状态。

`AlsRefactoredAnimationCatalog` 按单个 source ID 和已声明的文件摘要共享经过既有校验的 JSON。骨骼元数据是独立依赖；absolute/additive/带曲线 pose 以输出形式及 Root Motion 采样选项区分派生资源，复用编译结果，sampler 每个使用者单独创建。脱离 scope 的既有接口继续按请求读取和校验，不建立全局原文缓存。

T3D 适配器先构建完整 outer-path 索引，保留 declaration/definition 双树、foreign root、重复路径、完整平衡和缩进检查；按路径生成并缓存所需切片。已提供的原文发生变化时通过资源替换使索引和切片失效，比较的是实际字符串内容，不重读文件或全量计算哈希。T3D 规则只位于 Import，Core 不认识 Blueprint。

本次没有把 Godot 场景、模型、贴图或全部 Lyra loader 迁入这个 manager；它们继续使用已有加载能力，之后可以通过 Provider/Loader 接入。

## 诊断与验证

`GetStatistics` 提供请求、命中、成功加载、失败、失效、缓存项、存活版本及 `RetainedBytes`。该字节数是仍被引用资源所报告的字节量，当前 JSON 报告输入载荷长度，不是托管堆或进程驻留内存测量。

既有 EventSource 增加 Event 5 `ResourceCache`，记录 manager、资源 ID、load/hit/invalidate/replace/release/failure、版本号和载荷字节数；实际构建记录 `resource.load`。CLI 将事件写入 `summary.json.resourceCacheEvents` 和 `resource-cache.csv`，AI 可以按单个资源统计重复请求、命中及生命周期，不需要 IDE。

T3D 的 `native_graph.index.itemCount` 为索引输入行数，`native_graph.extract.itemCount` 为实际切片输出行数。切片命中不再次产生提取 scope，可以同时用 cache hit 事件统计请求量。默认整包完整性校验开关不变，不进行额外全盘哈希扫描。

实际命令、启动数据、失败和覆盖边界见[本次实现验证](../verification/2026-10-11-resource-management.md)；采集栈见[性能埋点](performance-instrumentation.md)。

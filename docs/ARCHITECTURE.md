# 计时与角色运行架构

v2.5.0 先拆分学习陪伴这一条链路，不重写整个 WPF 项目，不改变配置格式，也不引入新的 UI 框架。

```text
DesktopSession                        组合服务，提供配置保存回调
  CompanionRuntime                    WPF DispatcherTimer、窗口打开/关闭
    CompanionService                  计时配置、勿扰、通知、学习记录、保存边界
      CompanionSession                纯计时状态机，使用 TimeProvider
      BreakReminder / StudyHistory    提醒调度、完成记录
  FocusWindow                         控件事件、防抖、动画、原生窗口布局
    FocusPanelController              输入草稿、校验、按钮命令、只读显示快照
      CompanionSession                与服务共享同一个实例
```

## 状态与命令

- `CompanionSession` 是唯一计时状态来源。完整和迷你模式切换不新建 Session，不重新开始计时。
- `FocusPanelController` 保存尚未提交的分钟文本，不把无效文本写进 `PetSettings`。非法输入或保存失败会阻止开始和切换阶段。
- `Snapshot()` 一次读取剩余时间，返回状态、秒数、进度和命令文案，不更改 Session，也不写文件。
- `FocusWindow` 只把控件事件转交给控制器，负责 400ms 保存防抖、焦点、鼠标捕获、动画和布局。`Layout`、`Mini` 两个 partial 仍负责 WPF 专属操作。
- Session、Service 和 Controller 的操作由同一所属线程串行调用。它们不依赖 WPF 调度器，但不是新增的线程安全 API；后台任务需要由 Runtime 调度回 UI 线程。

## 保存顺序

调整时长时，Service 克隆候选设置，先调用 `DesktopSession` 提供的保存函数，成功后才采用新的分钟值并配置 Session。写入失败保留旧设置，由 Controller 恢复显示并给出错误。未变化的数值不重复落盘。

窗口位置通过独立的 `FocusWindowOptions` 副本保存，不重置暂停或运行中的计时。原有 `DesktopSettingsStore` 的原子写入和备份机制继续使用。学习历史仍为单独文件，不混入角色皮肤。

## 生命周期

关闭面板停止窗口自己的显示、动画、防抖和布局计时器，注销 Session 事件并释放 Controller。共享 CompanionRuntime 继续计时，再打开时读取同一个 Session。

正常退出由 DesktopSession 先提交待保存输入、窗口位置及历史，再释放 Runtime。致命异常退出仍丢弃待保存设置。Runtime 释放时停止调度、解除 Service 的完成事件订阅并关闭其窗口；释放后的服务不再发出通知或保存记录。

## 本轮边界

这是职责提取，不是完整 MVVM 迁移。仍共用现有程序集、`PetSettings`、日志与保存 API；没有抽出跨平台项目，也没有把 `MainWindow`、皮肤编辑器和所有窗口改成 ViewModel。后续可以沿同样边界逐个迁移，避免一次扩大回归面。

验证方式与当前限制见 [质量门禁](QUALITY_GATES.md)。

## 角色运行内核（v2.7）

```text
DesktopSession.Clock                   同一桌面会话的可注入时钟
  MainWindow                           原生输入、位置、图像与定时器适配
    PetActivityContext                 可见性、鼠标忙碌、设置、贴边、勿扰、专注
    PetBehaviorCapabilities            当前皮肤支持的能力
    PetBehaviorController              行为状态、优先级、自动期限、游走预算
      SpritePlayback                   累计帧期限、暂停/恢复、迟到帧定位
      DesktopBehavior.Move             沿用既有边界反弹计算
```

控制器不引用 WPF 窗口、图像或 Dispatcher。`MainWindow` 保留少量只读状态转发属性，实际可变行为状态由控制器持有；原生窗口生命期、鼠标捕获和位置保存仍由窗口负责。控制器与窗口在同一所属线程串行使用，不是新的跨线程 API。

主活动状态按退出、未加载/隐藏、拖动、设置、菜单、输入处理中、贴边、游走、注视及动作归属判定。活动状态与许可分开：例如菜单打开时仍可保留当前动画，但不能继续自动走动；勿扰禁止自动行为，不禁止用户主动播放动作。手动动作在结束前不被专注动画抢占，完成后再回到专注或待机状态。

动画、自动动作期限、游走积分和贴边过渡采用单调时钟。本地时间只用于定时勿扰等日历语义。原生定时器只是唤醒手段，不是已播放帧数或已移动距离的来源。`SpritePlayback` 保存累计帧期限，用二分定位当前帧，长时间停顿不会逐帧补跑；移动每步仍有 100ms 的补偿上限。

帧动画与自动行为使用各自期限。自动行为不可用时停止其定时器；仅有未来随机/走动任务时直接等待该期限，不以 100ms 空轮询。鼠标注视、暂停走动时的接近检测和贴边悬停仍需要有限频率采样。当前没有全局鼠标钩子，也没有把所有角色强行合并到一条固定高频循环。

改变行为状态后，窗口更新活动许可并重设必要的定时器；隐藏会暂停播放时钟，退出会将控制器置于终态。参考 [角色性能测量](ACTIVITY_PERFORMANCE.md) 了解测量范围与限制。

## 规则与诊断（v2.8）

`PetManifest.Behavior` 是可选声明数据，沿用皮肤包的 JSON 解析与未知字段拒绝策略。包验证先检查动作结构，再检查规则引用、时长、权重、冷却和重复条件。`PetBehaviorController` 复制有效规则，使用注入时钟保存冷却，不读取文件或执行脚本；主活动许可检查先于规则选择，规则不能修改该优先级。

`RuntimeSamplingSession` 只有手动开始后才向当前角色注册 `RuntimeRoleMeter`。主窗口在定时回调、帧调度和活动刷新时向已注册的采样器发送固定类型的数据；没有订阅时不创建采样记录，也不额外启动角色计时器。多个采样会话独立订阅，释放其中一个不会影响另一个。

诊断窗口每约 500ms 同步在场角色和表格，窗口非模态且不作为某个角色的子窗口。采样会话内编号递增、不复用；移除角色会解除订阅并保留有上限的匿名历史。五分钟、停止和关闭均解除所有订阅，保留的导出快照不持有原生窗口或图像。

本地显示的角色名称保存在单独映射中，不进入 `RuntimeCapture`。诊断 JSON 只附带编号、相对时间、枚举及数值，仍使用既有三个 ZIP 条目。默认错误诊断不自动附带此前未开启的采样记录。

# 计时模块架构

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

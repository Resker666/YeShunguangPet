# 叶瞬光桌面宠物 WPF MVP

这是一个不依赖 Codex 的 Windows WPF 桌面宠物 MVP。程序内置 `Assets/spritesheet.png`，按 `192 x 208` 单元格播放现有 v2 精灵图。

## 功能

- 透明无边框窗口
- 默认置顶
- 左键拖动
- 鼠标靠近时使用第 9/10 行看向鼠标方向
- 右键动作菜单
- 系统托盘菜单
- 位置、大小、置顶、点击穿透、开机启动设置保存到 `%APPDATA%\YeShunguangPet\settings.json`
- 可发布为 Windows x64 自包含单文件 exe

## 精灵图约定

当前 MVP 使用固定 Codex v2 帧表：

| 行 | 状态 | 使用列 |
| --- | --- | --- |
| 0 | 待机 | 0-5 |
| 1 | 向右拖动 | 0-7 |
| 2 | 向左拖动 | 0-7 |
| 3 | 打招呼 | 0-3 |
| 4 | 跳一下 | 0-4 |
| 5 | 失败 | 0-7 |
| 6 | 等待确认 | 0-5 |
| 7 | 工作中 | 0-5 |
| 8 | 检查成果 | 0-5 |
| 9 | 看向方向 000 到 157.5 | 0-7 |
| 10 | 看向方向 180 到 337.5 | 0-7 |

## 发布

已经安装 .NET 8 SDK 的机器，在本目录执行：

```powershell
.\scripts\publish-self-contained.ps1
```

如果开发机没有 .NET SDK，可以用无管理员权限的本地 SDK 发布脚本：

```powershell
.\scripts\bootstrap-dotnet-and-publish.ps1
```

这个脚本会把 SDK 放到 `%LOCALAPPDATA%\CodexDotnetSdk\8.0`，避免长路径解压问题，也不会改系统 PATH。

输出目录：

```text
bin\Release\net8.0-windows\win-x64\publish
```

发布完成后，普通 Windows 电脑可以直接运行 `YeShunguangPet.exe`，不需要 Codex，不需要 API key，不需要联网。

## 操作

- 左键拖动叶瞬光
- 右键打开动作/设置菜单
- 托盘双击显示或隐藏
- 开启点击穿透后，窗口不再接收鼠标；需要从托盘菜单关闭点击穿透


## 
Unofficial non-commercial fan-made desktop pet. This project is not affiliated with, endorsed by, or sponsored by HoYoverse. Zenless Zone Zero and related characters belong to their respective owners.

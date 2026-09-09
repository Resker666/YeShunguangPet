# 演示素材来源与复现

本批素材展示 v2.4.0，内容只用于介绍当前软件，不代表实际用户数、使用时长或学习成果。

## 来源与边界

- 角色图像来自仓库已有的 `Pets/YeShunguang/spritesheet.png` 和 `Pets/robin/spritesheet.png`。使用真实动作帧，没有重新生成角色或写回原始 PNG。
- 计时器和皮肤预览来自生产 WPF 控件的离屏渲染，不是重新画出来的概念界面。
- 原生控件使用独立配置、独立日志和可控演示时钟，不读取个人台词、学习记录或桌面配置；不会注册启动项或录制个人桌面。
- 图片排版、字幕和背景为演示合成。视频不是个人桌面实时录屏；请称为“效果演示”或“原生界面演示”。
- 皮肤镜头展示选择和预览，没有表现自动生成角色、自动补齐动画或一键下载任意第三方角色。
- 为平台展示对输出媒体进行缩放、GIF 调色和 MP4 编码，原始精灵图文件的字节不改变。素材权利范围见 [ASSET_NOTICE.md](../../ASSET_NOTICE.md)。
- 没有背景音乐、旁白或伪造用户评价。方形与横版封面均标明非官方同人项目。

## 原始精灵图 SHA256

```text
YeShunguang: 42FBB6129741A7468526AD0CAD27DF82EE3939BE140C768D3F431CC0C9A45C2D
robin:       0AF0626FB802726D7795F890743B0954B3B8BD0D95D0637D3A0C876AA262A739
```

输出文件的大小和摘要见 [media/manifest.json](../media/manifest.json)。

## 重新生成

在 Windows 开发机的仓库根目录执行，需要 .NET 8 SDK、Python、Pillow，以及可用的 FFmpeg 或 `imageio-ffmpeg`。中文字体使用系统 Microsoft YaHei；这些工具只用于维护素材，不是桌宠用户的运行依赖。

```powershell
dotnet run --project benchmarks/PerformanceProbe.csproj --configuration Release -- --promotion . artifacts/promotion-capture
python -m pip install Pillow imageio-ffmpeg markdown-it-py PyYAML
python scripts/build-promotion-media.py
python scripts/verify-promotion.py
```

也可以用 `--ffmpeg` 指定本机 FFmpeg 路径。原始控件导出位于 `artifacts/promotion-capture`，展示文件位于 `docs/media`。导出器不包括在桌宠 EXE 中。

生成后应逐项检查：GIF 能播放、MP4 时长与分辨率正确、字符不越界、皮肤图像与真实界面一致、链接有效、封面小于 GitHub 要求的大小。不要只确认文件存在就发布。

应用版本变化后，应重新导出并核对字幕、界面和功能；不要把 v2.4.0 的旧视频默认为新版实录。

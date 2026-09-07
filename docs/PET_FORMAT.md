# 本地皮肤包格式（schemaVersion 1）

从 v1.2.0 开始，程序读取外部 JSON 和 PNG。增加角色、更换精灵图、调整动作帧与时长都不需要重新编译 EXE。

准备让 AI 制作新角色时，请先看 [制作教程与提示词](AI_SKIN_GUIDE.md)。本文用于核对最终文件的技术格式。

## 目录

```text
YeShunguangPet.exe
Pets/
  YeShunguang/
    pet.json
    spritesheet.png
```

程序读取 EXE 旁的 `Pets/*/pet.json`，以及当前用户的 `%APPDATA%\YeShunguangPet\Pets/*/pet.json`。扫描顺序为随附目录优先、用户目录其次；同一 id 只能使用一份，重复项会显示错误。

在“设置 → 皮肤 → 导入皮肤”中选取解压后皮肤目录内的 `pet.json`。程序先校验整个皮肤包，再把清单与其引用的 PNG 复制到用户目录。导入后可以删除原始下载目录。

导入不会覆盖现有同 id 皮肤。更新已有皮肤时，可先退出程序，在“皮肤目录”中自行替换对应文件；也可以给新版本使用不同 id 并重新导入。点击“刷新”重新扫描；选择皮肤并“保存”后才切换桌面角色。取消设置保留当前角色，但已导入的包会留在皮肤目录。

## 最小清单

以下清单配合一张 `64 × 32` 的 PNG，每格 `32 × 32`，只有两帧待机：

```json
{
  "schemaVersion": 1,
  "id": "my-pet",
  "name": "我的角色",
  "description": "本地皮肤",
  "spriteSheet": "spritesheet.png",
  "cellWidth": 32,
  "cellHeight": 32,
  "columns": 2,
  "rows": 1,
  "animations": {
    "idle": {
      "row": 0,
      "startColumn": 0,
      "durationsMs": [300, 200],
      "loop": true
    }
  }
}
```

完整默认皮肤示例位于 [pet.json](../Pets/YeShunguang/pet.json)。

## 字段

| 字段 | 约定 |
| --- | --- |
| `schemaVersion` | 必填，当前为整数 `1` |
| `id` | 必填，1-64 位小写字母、数字或连字符，以字母开头；不能使用 Windows 保留名 |
| `name` | 必填，1-64 字符，用于选择器、桌面窗口标题和托盘提示 |
| `description` | 可省略，最多 400 字符 |
| `spriteSheet` | 可省略，默认 `spritesheet.png`；只能是同目录 PNG 文件名 |
| `cellWidth / cellHeight` | 必填，单格宽高，各为 16-512 像素 |
| `columns / rows` | 必填，行列数量，各为 1-64 |
| `animations` | 必填，动作表，至少包含循环的 `idle` |
| `lookDirections` | 可省略或为空；提供时必须有 16 个帧坐标 |

JSON 使用 UTF-8（支持 BOM），属性名使用上述大小写；重复字段和未知字段会被拒绝，避免拼写错误被静默忽略。
PNG 尺寸必须等于 `cellWidth × columns` 乘 `cellHeight × rows`，任一边不超过 8192，总像素数不超过 16,777,216。PNG 文件最大 64 MiB，清单最大 128 KiB。
建议使用透明 RGBA PNG。程序不修改 PNG 文件；显示大小按单元格尺寸和用户缩放比例计算。

## 动作

每个动作由 `row`、`startColumn`、`durationsMs`、`loop` 描述。行和列从 0 开始。
`row` 和 `startColumn` 可省略，默认 0；`loop` 默认 false。
从 `startColumn` 起连续播放同一行，帧数就是 `durationsMs` 的长度。数组不能为空，也不能超出该行；每帧时长为 20-10000 毫秒。

| 动作键 | 用途 | 必须提供 |
| --- | --- | --- |
| `idle` | 待机，必须循环 | 是 |
| `runningRight` | 向右拖动、自动走动 | 否 |
| `runningLeft` | 向左拖动、自动走动 | 否 |
| `waving` | 打招呼、召回、随机待机；必须单次播放 | 否 |
| `jumping` | 跳跃、随机待机；必须单次播放 | 否 |
| `failed` | 失败动作；必须单次播放 | 否 |
| `waiting` | 等待 | 否 |
| `running` | 工作 | 否 |
| `review` | 检查 | 否 |

缺失动作的菜单项会禁用。拖动与召回仍可用，缺失相关动画时播放待机。自动走动需要左右两个动作，随机待机需要 `waving` 或 `jumping`。这些能力检查不会清除用户保存的偏好，切换回具备相应动作的皮肤后继续使用原设置。

## 注视方向

`lookDirections` 按顺时针排列，从正上方开始，每隔 22.5 度一帧。第 0 帧向上，第 4 帧向右，第 8 帧向下，第 12 帧向左。每项为 `{"row": 9, "column": 0}` 这样的单元格坐标，可分布在任意行列。坐标不能越界；不提供时禁用注视鼠标。

## 恢复与升级

所选 id 保存在原有 `settings.json` 的 `SelectedPetId` 字段。旧版设置自动选择默认叶瞬光，保留其他偏好。已选择的包被删除或损坏时，启动会尝试默认叶瞬光，再尝试其他有效皮肤，并告知用户；如果没有任何有效皮肤，显示恢复提示后退出。

每个包仅包含声明数据和 PNG，不执行脚本。引用不能越过皮肤目录，不支持 URL、绝对路径、符号链接或目录联接。皮肤素材的权利由各自提供者确认，不随程序的 MIT 源码许可一并授权。

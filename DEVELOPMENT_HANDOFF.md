# Carpet Grid Unity Demo 开发接管文档

更新时间：2026-07-07  
工程路径：`D:\DEMO`  
引擎版本：Unity `2022.3.62f2`

## 0. 文档维护自动化约定

后续所有研发改动都需要同步更新本文档，方便接手研发快速了解项目现状。每次修改代码、配置、资源、关卡、工具脚本或工程设置后，请在本文档中补充对应信息：

- 改动涉及的模块、文件或资源目录。
- 行为变化、数据格式变化、运行入口变化。
- 新增的已知问题、测试方式、验收步骤或接管风险。
- 如果改动已经提交到 Git，请记录关键提交信息或变更摘要。

Codex 自动化会定期检查 `D:\DEMO` 的 Git 变更，并优先维护这份文档；人工修改项目时也应把本文档作为研发交接的单一阅读入口。

## 1. 项目概述

本项目是一个 Unity 2D/UI 原型，核心玩法为“地毯棋盘解谜”：玩家拖动不同颜色、不同长度的地毯在网格上铺设路径，使所有地毯长度耗尽并停在各自目标格后通关。

当前工程已经包含三段完整流程：

1. 开场页：Logo、开始按钮、剧情文字、视频播放、过渡到菜单。
2. 章节菜单：4 个章节入口、章节解锁/完成状态、设置面板、背景/装饰动画。
3. 关卡游玩：按章节进度进入关卡，拖动地毯解谜，胜利后自动返回菜单并推进进度。

工程内还保留了编辑模式、菜单布局编辑器、关卡配置表生成脚本，方便继续扩展关卡和美术配置。

## 2. 运行与工程入口

### 2.1 打开方式

1. 使用 Unity Hub 打开 `D:\DEMO`。
2. 建议从 `Assets/Scenes/Intro.unity` 运行完整流程。
3. 当前 Build Settings 场景顺序：
   - `Assets/Scenes/Intro.unity`
   - `Assets/Scenes/LevelSelectMenu.unity`
   - `Assets/Scenes/Main.unity`

### 2.2 重要实现特点

场景本身主要作为空壳入口使用，运行时脚本通过 `RuntimeInitializeOnLoadMethod` 自动创建控制器和 Canvas：

- `Intro` 场景自动创建 `IntroSceneController`
- `LevelSelectMenu` 场景自动创建 `CarpetLevelMenu`
- `Main` 场景自动创建 `CarpetGridGame`

因此接管时不要只看场景层级判断功能是否缺失，核心 UI 和逻辑都在代码运行时动态生成。

## 3. 技术栈与依赖

Unity 包依赖位于 `Packages/manifest.json`：

- `com.unity.ugui`：运行时 UI。
- `com.unity.modules.ui`：Unity UI 基础模块。
- `com.unity.modules.video`：开场视频播放。
- `com.unity.modules.imgui`：编辑器窗口 UI。
- `com.unity.modules.jsonserialize`：`JsonUtility` 序列化。

当前没有引入第三方 Unity 插件。表格生成脚本使用本机 Codex runtime 下的 Node.js artifact 工具。

## 4. 目录结构

关键目录如下：

```text
D:\DEMO
├─ Assets
│  ├─ Editor
│  │  └─ CarpetMenuLayoutEditor.cs
│  ├─ levels
│  │  ├─ level-001.json
│  │  ├─ level-002.json
│  │  └─ ...
│  ├─ Resources
│  │  ├─ Art
│  │  ├─ Intro
│  │  └─ Menu
│  ├─ Scenes
│  │  ├─ Intro.unity
│  │  ├─ LevelSelectMenu.unity
│  │  └─ Main.unity
│  ├─ Scripts
│  │  ├─ CarpetGridGame.cs
│  │  ├─ CarpetLevelFlow.cs
│  │  ├─ CarpetLevelMenu.cs
│  │  └─ IntroSceneController.cs
│  └─ StreamingAssets
│     ├─ Art
│     ├─ Intro
│     ├─ Levels
│     └─ Menu
├─ Tools
│  └─ generate_chapter_workbook.mjs
├─ outputs
│  ├─ chapter_level_config.xlsx
│  └─ chapter_level_config_preview.png
└─ README_UNITY.md
```

注意：`Library`、`Temp`、`Logs`、`UserSettings` 是 Unity 生成目录，不应作为业务源码维护重点。

## 5. 核心脚本职责

### 5.1 `Assets/Scripts/IntroSceneController.cs`

负责开场流程：

- 自动创建开场 Canvas。
- 读取 `Assets/StreamingAssets/Intro/intro_story_config.json`。
- 展示 Logo、开始按钮和背景色。
- 点击开始后进入剧情文字层。
- 剧情文字按打字机效果显示。
- 再次点击进入视频播放。
- 视频播放结束后显示过渡层，并加载 `LevelSelectMenu` 场景。
- 如果视频加载失败或超时，直接进入过渡层再到菜单。

配置项包括：

- `videoPath`
- `logoResource`
- `startButtonResource`
- `backgroundColor`
- `storyOverlayAlpha`
- `storyFontSize`
- `storyLineHeight`
- `lineRevealSeconds`
- `storyHint`
- `finalStoryHint`
- `transitionLabel`
- `transitionImageResource`
- `transitionSeconds`
- `storyPages`

### 5.2 `Assets/Scripts/CarpetLevelMenu.cs`

负责章节选择菜单：

- 自动创建菜单 Canvas。
- 读取 `Assets/StreamingAssets/Menu/menu_config.json`。
- 支持最多 4 个章节按钮。
- 根据章节进度显示章节状态：
  - `Open`：可进入。
  - `Locked`：上一章节未完成，锁定。
  - `Completed`：当前章节内关卡全部完成。
- 点击开放章节时，根据当前章节进度进入对应关卡。
- 菜单支持背景图、装饰图、装饰阴影、呼吸动画、帧动画、触发动画文件夹。
- 内置设置面板：
  - 音乐开关。
  - 音效开关。
  - 震动开关。
  - 重置游戏进度。

进度使用 `PlayerPrefs` 保存：

- 章节进度 key 前缀：`carpet-menu-progress-`
- 音乐 key：`carpet-setting-sound`
- 音效 key：`carpet-setting-sfx`
- 震动 key：`carpet-setting-vibration`

### 5.3 `Assets/Scripts/CarpetLevelFlow.cs`

负责场景流转和关卡请求：

- 场景名常量：
  - `Intro`
  - `LevelSelectMenu`
  - `Main`
- `StartLevel(buttonIndex, level)`：记录章节按钮索引和关卡号，然后加载 `Main`。
- `ConsumeRequestedLevel()`：`Main` 场景启动时读取并清空请求关卡号。
- `CompleteActiveLevelAndReturn()`：通关后推进章节进度并返回菜单。
- `ReturnToMenu()`：不推进进度，直接返回菜单。
- 内部使用 `CarpetSceneTransitionRunner` 处理异步加载，避免重复触发。

### 5.4 `Assets/Scripts/CarpetGridGame.cs`

负责主玩法：

- 自动创建游戏 Canvas。
- 根据 `CarpetLevelFlow` 请求关卡进入指定关卡。
- 读取关卡 JSON。
- 读取章节美术配置。
- 渲染棋盘、地毯、目标、路径、高亮、关卡标题和操作按钮。
- 处理拖拽移动、撤回、同组同步移动、借道、胜利判定。
- 保留编辑模式相关 UI 和逻辑。

关键常量：

- 最大关卡数：`99`
- 主关卡目录名：`Assets/levels`
- 美术配置：`Assets/StreamingAssets/Art/game_art_config.json`
- 拖拽启动阈值：`10px`
- 拖拽步进间隔：`0.055s`
- 移动动画时间：`0.13s`

### 5.5 `Assets/Editor/CarpetMenuLayoutEditor.cs`

Unity 编辑器工具，菜单入口：

```text
Tools/Carpet/Menu Layout Editor
```

用途：

- 可视化编辑 `Assets/StreamingAssets/Menu/menu_config.json`。
- 支持章节按钮位置、尺寸、旋转、文案、关卡列表。
- 支持装饰图位置、尺寸、旋转、颜色、阴影、呼吸参数、帧动画参数。
- 支持预览参考画布 `1440 x 900`。
- 支持 Reload 和 Save。

## 6. 游戏功能需求与实现状态

### 6.1 已实现的完整流程

- 开场页进入剧情。
- 剧情结束后播放视频。
- 视频结束后进入章节菜单。
- 章节菜单根据进度解锁。
- 进入当前章节的当前关卡。
- 通关后自动返回章节菜单。
- 章节进度推进，下一关或下一章解锁。

### 6.2 已实现的棋盘玩法

- 棋盘支持 `1 x 1` 到 `99 x 99`。
- 关卡可配置多个地毯。
- 每个地毯包含：
  - 起点：`row`、`col`
  - 目标点：`targetRow`、`targetCol`
  - 长度：`length`
  - 颜色：`color`
  - 分组：`groupId`
  - 可借道颜色：`passColor`
- 玩家按住地毯并拖过相邻格移动。
- 每次有效移动只允许上下左右相邻格。
- 铺新格会消耗长度。
- 撤回最近一步会恢复长度与原格状态。
- 地毯铺完后不会消失，仍可沿最近路径撤回。
- 所有地毯长度归零且停在各自目标点时胜利。
- 游玩模式胜利后自动返回菜单并推进进度。

### 6.3 已实现的特殊规则

- 异色地块默认不能覆盖。
- 配置了 `passColor` 的地毯可以免费经过指定异色路径。
- 同色但不属于自己的路径可以免费经过。
- 同色且已铺完、停在格上的地毯可以被同色地毯通过。
- 同组地毯会按同一方向尝试同步移动。
- 同组内被阻挡的成员停止，未被阻挡的成员继续移动。
- 撤回时会检查借道依赖，避免破坏其他地毯依赖的路径。
- 不再判定失败，地毯铺满但未到目标时可通过撤回继续调整。

### 6.4 已实现的编辑/调试能力

当前 `CarpetGridGame` 中仍保留编辑模式相关逻辑：

- 编辑/游玩模式切换。
- 设置棋盘宽高。
- 设置地毯数量、长度、颜色。
- 放置地毯。
- 放置目标。
- 修改起点、目标、长度、组号、借道颜色。
- 删除地毯及其目标。
- 重置染色。
- 保存当前关卡到 `PlayerPrefs`。

接管注意：运行时加载关卡的主逻辑目前优先读取 JSON 文件，`SaveCurrentLevel()` 虽然会写入 `PlayerPrefs` 的 `carpet-grid-unity-levels-v1`，但 `LoadSavedLevels()` 当前主要从文件系统读取关卡，未恢复读取该 PlayerPrefs 银行数据。若要做正式关卡编辑器，需要补齐“从 PlayerPrefs 导出为 JSON”或“重新读取 PlayerPrefs”的闭环。

## 7. 数据与配置

### 7.1 关卡数据

主关卡目录：

```text
Assets/levels
```

兼容备用目录：

```text
Assets/StreamingAssets/Levels
```

读取优先级：

1. 如果 `Assets/levels` 存在，只读取该目录下的 `*.json`。
2. 只有当 `Assets/levels` 不存在时，才读取 `Assets/StreamingAssets/Levels`。

关卡编号解析规则：

- 文件名全数字，例如 `1.json`。
- 文件名末尾带数字，例如 `level-001.json`。
- 代码会提取文件名末尾数字作为关卡号。

当前 `Assets/levels` 包含：

- `level-001.json`
- `level-002.json`
- `level-003.json`
- `level-004.json`
- `level-005.json`
- `level-006.json`
- `level-007.json`
- `level-010.json`

关卡 JSON 示例：

```json
{
  "rows": 5,
  "cols": 5,
  "carpets": [
    {
      "id": 1,
      "row": 0,
      "col": 0,
      "targetRow": 0,
      "targetCol": 2,
      "length": 2,
      "color": "#e85d64",
      "groupId": "",
      "passColor": ""
    }
  ]
}
```

也兼容包装格式：

```json
{
  "level": {
    "rows": 5,
    "cols": 5,
    "carpets": []
  }
}
```

### 7.2 章节菜单配置

路径：

```text
Assets/StreamingAssets/Menu/menu_config.json
```

核心结构：

```json
{
  "backgroundColor": "#e9dfc7",
  "backgroundImage": "Menu/pixel-large-crt-1783320811299.png",
  "buttons": [
    {
      "label": "章节一",
      "levels": [1, 2],
      "position": { "x": 0, "y": 360 },
      "size": { "x": 560, "y": 108 },
      "rotation": 0
    }
  ],
  "decorations": [],
  "animations": []
}
```

当前章节映射：

| 章节 | 关卡 |
| --- | --- |
| 章节一 | 1, 2 |
| 章节二 | 3, 4 |
| 章节三 | 5, 6 |
| 章节四 | 7, 10 |

### 7.3 游戏美术配置

路径：

```text
Assets/StreamingAssets/Art/game_art_config.json
```

用途：

- 配置棋盘背景、格子图、地毯图、目标图、返回图标、重开图标。
- 配置全局颜色。
- 按章节或关卡覆盖背景与颜色。

当前章节美术映射：

| 章节 | 关卡 | 背景 |
| --- | --- | --- |
| 1 | 1, 2 | `Art/chapter_1_background.png` |
| 2 | 3, 4 | `Art/chapter_2_background.png` |
| 3 | 5, 6 | `Art/chapter_3_background.png` |
| 4 | 7, 10 | `Art/chapter_4_background.png` |

资源路径写法需要对应 `Assets/Resources` 下的资源，代码会去掉扩展名再使用 `Resources.Load`。

### 7.4 开场剧情配置

路径：

```text
Assets/StreamingAssets/Intro/intro_story_config.json
```

用途：

- 指定视频：`Intro/intro_video.mp4`
- 指定 Logo：`Intro/swallows_diamond_logo`
- 指定开始按钮图：`Intro/start_button`
- 配置剧情页和逐字显示速度。

注意：当前该 JSON 内的剧情文本存在中文编码乱码，运行时会按乱码内容显示。需要在交付前用 UTF-8 正确文本重新写入。

## 8. 资源清单

### 8.1 玩法美术

位于：

```text
Assets/Resources/Art
```

主要资源：

- 章节背景：`chapter_1_background.png` 到 `chapter_4_background.png`
- 棋盘格：`marble_board_cell.png`
- 棋盘背景：`marble_board_background.png`
- 地毯：`diamond_carpet.png`
- 返回图标：`icon_back_arrow.png`
- 重开图标：`icon_restart_arrow.png`
- 关卡标题数字：`LevelDisplay/digit_0.png` 到 `digit_9.png`
- 关卡标题单词图：`LevelDisplay/level_word.png`

### 8.2 菜单美术

位于：

```text
Assets/Resources/Menu
```

主要资源：

- 菜单背景：`pixel-large-crt-1783320811299.png`
- 工作台：`workbench_pixel.png`
- 角色静态图：`menu_character_chibi_static_final.png`
- 设置按钮：`icon_settings_gear.png`
- NPC/云/闪电等动画帧：`animation/*`

### 8.3 开场资源

位于：

```text
Assets/Resources/Intro
Assets/StreamingAssets/Intro
```

主要资源：

- `swallows_diamond_logo.png`
- `start_button.png`
- `intro_video.mp4`
- `intro_story_config.json`

## 9. 工具与产物

### 9.1 章节配置表生成

脚本：

```text
Tools/generate_chapter_workbook.mjs
```

输出：

```text
outputs/chapter_level_config.xlsx
outputs/chapter_level_config_preview.png
outputs/chapter_level_config.xlsx.inspect.ndjson
```

作用：

- 读取 `Assets/levels` 下的关卡文件。
- 生成章节到关卡 JSON 的映射表。
- 输出 Excel 和预览图。

注意：该脚本中的部分中文也存在乱码，后续需要修复编码后再作为正式策划表工具使用。

### 9.2 菜单布局编辑器

入口：

```text
Unity Editor > Tools > Carpet > Menu Layout Editor
```

建议用途：

- 后续不要手改菜单按钮位置和装饰坐标，优先使用该工具保存 JSON。
- 修改后运行菜单场景检查实际布局。

## 10. 已知问题与接管风险

1. 中文编码问题  
   `README_UNITY.md`、`intro_story_config.json`、部分 C# 字符串、`generate_chapter_workbook.mjs` 中存在中文乱码。功能逻辑可运行，但 UI 文案和文档需要重新整理为 UTF-8。

2. 关卡编辑保存闭环不完整  
   `SaveCurrentLevel()` 写入 PlayerPrefs，但当前正式加载路径优先使用 JSON 文件。若需要运行时编辑关卡并落盘，需要增加导出 JSON 或恢复 PlayerPrefs 读取。

3. 主玩法脚本体量较大  
   `CarpetGridGame.cs` 同时承担 UI 构建、数据加载、渲染、输入、玩法规则、动画和编辑能力，后续维护建议拆分为：
   - `LevelStore`
   - `BoardState`
   - `MoveResolver`
   - `BoardRenderer`
   - `GameHud`
   - `LevelEditorController`

4. 菜单按钮数量硬编码为 4  
   `CarpetLevelMenu` 使用 `ChapterButtonCount = 4` 和固定长度进度数组。如果要扩展更多章节，需要改代码。

5. `Assets/levels` 与 `StreamingAssets/Levels` 存在重复关卡来源  
   当前只要 `Assets/levels` 存在，`StreamingAssets/Levels` 就不会被读取。后续应明确正式关卡目录，避免策划改错位置。

6. 缺少自动化测试  
   当前未发现 Unity Test Runner 测试。核心移动规则复杂，建议为移动合法性、撤回、借道、同组移动、胜利判定补充 EditMode 单元测试。

7. Git 仓库与交接文档同步
   当前工程已经初始化为 Git 仓库，`main` 分支已推送到 `https://github.com/jiayu924chen-boop/Swallow-s-Diamond.git`。后续每次提交前应确认 `DEVELOPMENT_HANDOFF.md` 已记录本次研发变更，避免代码状态和接管文档脱节。

## 11. 后续开发建议

优先级建议如下：

1. 修复所有中文文本编码，确认 UI 文案、剧情文案、README、工具表头均为 UTF-8。
2. 明确正式关卡来源，建议统一使用 `Assets/StreamingAssets/Levels` 或统一使用 `Assets/levels`，不要双源并存。
3. 拆分 `CarpetGridGame.cs`，先从纯逻辑 `MoveResolver` 和数据读写 `LevelStore` 开始。
4. 为关卡 JSON 增加 schema 或校验工具，提前发现越界坐标、重复 ID、非法颜色、空关卡等问题。
5. 为章节菜单解除 4 章节硬编码，改为根据 `menu_config.json.buttons.Length` 动态生成。
6. 完善关卡编辑器：支持导入、导出、预览、校验、批量生成。
7. 补充移动规则测试，尤其覆盖同组移动和借道撤回依赖。
8. 做一次移动端分辨率适配验证，当前 UI 基于 `1080 x 1920` 参考分辨率动态生成。

## 12. 研发验收清单

接管后建议先按以下路径验收：

1. Unity 打开 `D:\DEMO`，确认版本为 `2022.3.62f2`。
2. 从 `Intro.unity` 点击 Play。
3. 点击开始按钮，确认剧情层显示。
4. 点击推进剧情并进入视频。
5. 视频结束后进入章节菜单。
6. 点击章节一，进入关卡 1。
7. 拖动地毯完成关卡，确认胜利后自动返回菜单。
8. 确认章节一进度推进到关卡 2。
9. 打开 `Tools/Carpet/Menu Layout Editor`，Reload 并保存一次菜单配置。
10. 修改一个关卡 JSON 后重新运行，确认关卡加载规则符合预期。

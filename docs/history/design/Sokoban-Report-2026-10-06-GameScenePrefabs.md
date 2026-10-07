> 历史阶段记录：以下内容反映对应版本的设计与实现过程。当前功能和操作请阅读[新版操作手册](../../USER_MANUAL.md)。

# 游玩场景预制体化与场景烘焙报告（v0.14.0）

日期：2026-10-06　工程基线：Unity 2022.3.51f1c1 / Windows　关联规格：[Sokoban-SDD.md](Sokoban-SDD.md) 第 12.6 节

## 1. 背景与目标

v0.13.x 及之前，游玩界面的所有物体都在运行时用代码 `new GameObject` 创建：布局只能改代码里的数字、必须进 Play Mode 才看得见，也无法替换美术资源。本轮目标是把"不直观的动态创建"改成"直观的、可手动修改的"资产，以增强游戏内表现力，并满足两个边界条件：

- 编辑器（`Assets/level_editor`）**一行不改**，与游戏本体保持解耦；
- 游戏本体继续读关卡 JSON，棋盘尺寸仍由数据决定。

## 2. 改了什么

### 2.1 game.unity 烘焙的外壳

场景从 1 个 GameObject（Main Camera）变为完整烘焙的界面树：

```
Main Camera (SokobanSceneStartup)
EventSystem
SokobanRuntimeCanvas (Canvas / CanvasScaler 1920×1080 / GraphicRaycaster)
├── ScreenBackground
├── Workspace (+ CanvasGroup 交互锁)
│   ├── Background
│   ├── Hud
│   │   ├── Title / Stats / GMHint / ViewMode / Message
│   │   ├── BoardPanel
│   │   │   └── GameViewport (RectMask2D + SokobanBoardView，已拖好五个预制体引用)
│   │   └── UndoButton / RestartButton / LevelButton / MapButton / PauseButton
│   ├── ErrorPanel（默认隐藏）: ErrorTitle / Error / ErrorBack
│   ├── WinPanel（默认隐藏）: WinDialog > WinTitle / WinStats / Next / Replay / WinBack
│   ├── PauseOverlay（默认隐藏）: PauseDialog > PauseTitle / PauseHint / PauseResume / PauseRestart / PauseSettings / PauseLevels
│   └── MapOverlay（默认隐藏）: MapTitle / MapClose / MapArea > MapViewport / MapHint
└── SokobanGameplayController (SokobanGameplaySceneController + SokobanGameplaySceneRefs)
```

控制器通过序列化的 `SokobanGameplaySceneRefs` 绑定这些对象；按钮回调从不可序列化的闭包 lambda 改为无参 public 方法（`OnUndoClicked`、`OnRestartClicked`、`OnLevelClicked`、`OnMapClicked`、`OnPauseClicked`、`OnMapCloseClicked`、`OnErrorBackClicked`、`OnWinNextClicked`、`OnWinReplayClicked`、`OnWinBackClicked`、`OnPauseResumeClicked`、`OnPauseRestartClicked`、`OnPauseSettingsClicked`、`OnPauseLevelsClicked`），在 Inspector 里以 UnityEvent 持久化绑定，可直接改绑。

**保持运行时创建**：全局 GM 面板（`DontDestroyOnLoad` 跨场景单例）与设置面板（start 与 game 共用）。二者结构上不适合烘焙进单个场景。

### 2.2 棋盘双层结构与五个预制体

预制体位于 `Assets/Resources/Prefabs/Board/`：

| 预制体 | 内容 | 已接贴图 |
| --- | --- | --- |
| `Tile_Floor` | RectTransform + Image + SokobanTileView + FxMount | Floor.png |
| `Tile_Wall` | 同上 | Wall.png |
| `Tile_Goal` | 同上 + GoalOverlay 子物体 | Floor.png + Aid.png |
| `Entity_Box` | RectTransform + Image + SokobanEntityView + FxMount + AudioSource | Box.png / Box_Aid.png |
| `Entity_Player` | 同上 + 四方向行走帧数组 | player_{u,d,l,r}_{00,01,02}.png |

运行时层级：

```
BoardView (RectMask2D)
└── Board        随"跟随视野"平移
    ├── Tiles    W×H 个地块实例，命名 Tile_{x}_{y}，加载后不再变化
    └── Entities 1 个 Player + N 个 Box 实例，是 Tiles 的后兄弟 → 渲染在其上
```

实体不再靠格子内子物体 `SetActive` 开关表示，而是持有自己的网格坐标、按约 0.11s 补间移动 `anchoredPosition`。箱子集合没有身份标识，用"最近未匹配"把旧实例对应到新坐标：单步推动时唯一匹配，撤销/重开等多格变化时就近归位。实体层是地块层的后兄弟，箱子压在目标点上时目标点自然露出，叠放语义与 v0.3.11 一致。

### 2.3 表现力挂点（本轮预留、未填充）

- `Image.sprite`：已接入现有贴图；在预制体上可直接换任意贴图。
- `Animator` 挂点：预制体上挂 Animator 并启用后，`SokobanEntityView` 的代码换帧自动让位（`codeDrivenFrames` 可手动关闭）。
- `AudioSource`：实体预制体已挂，`PlayOneShot(clip)` 可用；尚未创建任何音频资产。
- `FxMount`：地块与实体预制体上的空子物体，直接往里挂 ParticleSystem 即可。
- 无贴图时回退 `SokobanTheme` 纯色，删除贴图界面仍完整。

## 3. 怎么手动修改（速查）

- 改 HUD/弹窗布局、文字、颜色：直接在 Scene/Hierarchy 里选中 `game.unity` 的对应物体改 Inspector，保存场景即可，无需碰代码。
- 改按钮行为：选中按钮 → Button 组件 → On Click() 列表，可改绑控制器的任意 public 无参方法。
- 换棋盘美术：打开 `Assets/Resources/Prefabs/Board/` 下对应预制体（Prefab Mode），替换 Image 的 Sprite 或往 FxMount 挂粒子。
- 加玩家/箱子动画：在 `Entity_Player` / `Entity_Box` 预制体上挂 Animator 与 AnimatorController，用 `SokobanEntityView.PlayTrigger(name)` 的触发器名约定接推箱/入位/通关。
- 改坏了想重来：菜单 `Sokoban/Bake Game Scene` 会按代码里的布局常量重建预制体与整个场景（会丢弃你在场景里未备份的手改，请先自行留副本）。只重建预制体用 `Sokoban/Create Board Prefabs`。

## 4. 踩坑与硬约束（务必记住）

1. **一个 MonoBehaviour 一个同名 .cs 文件。** Unity 的 MonoScript 按"类名 == 文件名"绑定；同一文件里的第二个 MonoBehaviour、或首类与文件名不匹配时，场景/预制体中的脚本引用会序列化成 `m_Script: {fileID: 0}` / 裸 fileID，加载时报 "referenced script is missing"。本轮 `SokobanTileView`、`SokobanEntityView`、`SokobanGameplaySceneController` 因此各自独占同名文件。新增可挂载脚本时遵守此约定。
2. **烘焙过程中不得调用 `AssetDatabase.Refresh()`。** 它会在烘焙中途触发域重载，使后续 `AddComponent` 拿到旧程序集的失效类型。正确顺序：改代码 → 单独刷新并等编译完成 → 再执行烘焙菜单。
3. **Qoder 侧的 unityMCP 客户端不会自动重连。** 服务端（127.0.0.1:8080/mcp）重启后，需在 CLI 执行 `/mcp reload`。本轮另备了一条不依赖客户端注册的直连通道：`C:\Users\hoo\unity-mcp-client\umcp.mjs`（streamable HTTP 客户端，49 个工具）与 `run-code.mjs <文件.cs>`（在 Editor 内执行 C# 并返回结果），可在客户端未注册时继续驱动 Unity。
4. 冒烟测试依赖的对象名与反射成员是硬契约，改名会直接破坏 `SokobanGmSmoke` / `SokobanStandaloneSmoke`：`Workspace`、`UndoButton`、`PauseButton`、`MapButton`、`MapClose`、`Next`、`WinTitle`、`WinStats`、`WinBack`、`PauseResume`、`Error`，以及私有成员 `level`、`state`、`elapsedSeconds`、`TryMove`、`UndoMove`、`ClosePause`。

## 5. 验证结果

自动化（本轮实测）：

- Unity 编译 0 error；`Sokoban/Run All Checks` 全绿：求解与 60 组随机 Dijkstra 对照、视野/双层棋盘契约、管理 38、生成 310、HTML 256、批量 2245、验证 45、产品 29、印章 36、解锁 17 全部 PASS。
- Play Mode 实测：L001 加载后 Tiles=56、Entities=2、stride=82；右移一格补间到位（-205→-123，恰为一个 stride），玩家行走帧随方向与进度推进到 `player_r_02`；暂停时 `UndoButton` 正确失能；完整地图重建 56 地块；GM 强制胜利弹出结算且标题为"GM 完成"。全程 0 error 0 warning。
- start / level / editor 三场景加载 0 error 0 warning（editor 网格 532 个 MonoBehaviour 正常）。
- 截图存档：`Assets/Screenshots/bake-game-01-play.png`（真实美术游玩画面）、`bake-game-02-pause.png`、`bake-game-03-map.png`、`bake-game-04-win.png`、`bake-editor-05.png`。

仍需人工回归（鼠标/键盘级交互，自动化不覆盖）：

- 方向键 / WASD 移动、Z 撤销、R 重开、P/Esc 暂停、M 地图开关的按键手感与补间观感；
- 五个底部按钮与三个弹窗内按钮的点击、hover/pressed 变色；
- 通关后"下一关/返回选关"分支、编辑试玩"返回编辑器"分支；
- 窗口拉伸/改分辨率时棋盘 stride 重算与跟随视野钳制；
- 大地图（>14×10）跟随视野下实体补间与相机平滑是否跟手。

## 6. 未做与后续

- 未创建任何音频资产、AnimatorController、粒子；挂点已预留，填充由美术迭代完成。
- 设置面板的音量滑条仍未真正驱动 AudioSource（沿用占位语义）。
- 未构建、未打包 Windows 版本；未更新历史源码包。
- `a/d/s/w.png` 键位图标尚未接入 HUD 提示栏，可作为下一步小改。

## 7. 追加（0.14.1）：长按连发移动

原实现用 `Input.GetKeyDown`，方向键/WASD 只在按下瞬间走一步，长按无反应。现改为：按下立即走一步；继续按住 `moveRepeatDelay`（默认 0.28s）后，按 `moveRepeatInterval`（默认 0.13s）连发。间隔略大于实体补间时长（0.11s），因此观感为连续滑动而非逐格跳。

- 方向优先级：维护按下顺序列表，末尾（最后按下）的方向生效；松开某键后回落到仍按住的键；全松即停。
- 方向键的按下/抬起在 `Update` 最开头消费，暂停/地图/结算等提前 return 的分支不会漏掉 GetKeyUp，避免"幽灵连发"。
- 撤销(Z/Backspace)与重开(R)保持单次触发，不连发，防止误触批量撤销。
- 两个间隔是 `SokobanGameplaySceneController` 上的序列化字段，选中场景里的 `SokobanGameplayController` 物体即可在 Inspector 的"长按连发"分组调整，无需改代码。
- 验证：反射驱动 `UpdateHeldMovement` 逐步断言（首按 1 步 → 延迟后连发 2/3 步 → 清空按下列表后不再移动且连发状态复位）；`Sokoban/Run All Checks` 全绿。真实按键手感请用户在 Play Mode 回归。

> 历史阶段记录：以下内容反映对应版本的设计与实现过程。当前功能和操作请阅读[新版操作手册](../../USER_MANUAL.md)。

# 推箱子项目进度报告（暂停快照）

> 快照时间：2026-10-04  
> 当前状态：暂停后续功能修改，等待下一位 agent 接手  
> 工程目录：D:/kuluobishi  
> Unity：2022.3.51f1c1  
> MCP：http://127.0.0.1:8080/mcp

## 1. 当前结论

level.unity 文件没有丢失，也没有被删除。当前场景文件仍然存在：

- Assets/Scenes/start.unity
- Assets/Scenes/level.unity
- Assets/Scenes/game.unity
- Assets/Scenes/editor.unity

level.unity 中保留了原始占位 Canvas 和 SokobanSceneStartup 组件。实际的选关界面目前由运行时脚本动态创建，名称为 SokobanRuntimeCanvas。退出 Play Mode 后，运行时对象会被 Unity 清理，因此回到 Scene View 时看到原始占位 Canvas，看起来像“场景复原”。

这属于当前实现方式导致的显示差异，不代表关卡数据或场景文件被回滚。若要求打开 Scene View 就能看到最终产品 UI，需要把运行时 UI 固化为场景对象或 Prefab；这项工作尚未完成。

## 2. 已完成内容

### 文档

- Assets/doc/Sokoban-SDD.md：SDD 规格文档，当前版本 0.2.2。
- Assets/doc/Sokoban-Architecture-v0.1.md：基础版本架构设计。

### 核心代码

- Assets/game_script/SokobanCore.cs
  - 关卡 JSON 数据结构。
  - BuiltIn/Generated 目录读取和保存。
  - 玩家移动、推箱、胜利判断。
  - 撤销历史基础支持。
  - 关卡验证。
- Assets/game_script/SokobanUI.cs
  - 运行时 Canvas、面板、按钮、文本、输入框等创建辅助。
  - 目标字体应使用已有资源 Assets/resources/fonts/STXIHEI.TTF。
- Assets/game_script/SokobanSceneControllers.cs
  - start 启动界面。
  - level 选关界面。
  - game 局内界面、撤销、重开、通关、GM。
  - editor 运行时编辑器界面。
- Assets/game_script/SokobanSceneStartup.cs
  - 场景启动入口，按场景名激活对应控制器。

### 关卡数据

已创建：

- Assets/resources/level/BuiltIn/level.json：关卡目录索引。
- Assets/resources/level/BuiltIn/1/level.json
- Assets/resources/level/BuiltIn/2/level.json
- Assets/resources/level/BuiltIn/3/level.json
- Assets/resources/level/Generated/README.txt

此前已在 Unity Play Mode 验证选关界面能读取 3 个 BuiltIn 关卡，并生成截图：

- Assets/Screenshots/level-select-v2.png

### 构建设置

ProjectSettings/EditorBuildSettings.asset 已登记四个场景：

1. Assets/Scenes/start.unity
2. Assets/Scenes/level.unity
3. Assets/Scenes/game.unity
4. Assets/Scenes/editor.unity

## 3. 当前已知阻塞问题

暂停前最后一次 Unity 编译暴露了以下问题，尚未修复和重新验证：

### 3.1 选关/运行时控制器的参数类型错误

文件：Assets/game_script/SokobanSceneControllers.cs 第 67 行。

当前形式：

TextAlignmentOptions alignment = TextAnchor.MiddleLeft

Legacy UI Text 应统一使用：

TextAnchor alignment = TextAnchor.MiddleLeft

控制器文件顶部还残留 using TMPro;，完成 Legacy UI 切换后应检查是否仍需要。

### 3.2 UI 文本实现仍混用了 Legacy UI 和 TMP

文件：Assets/game_script/SokobanUI.cs 第 59-76 行。

方法创建的是 UnityEngine.UI.Text，但随后使用：

objectRoot.GetComponent<TextMeshProUGUI>()

并按 TMP 类型设置属性和返回值。这与方法返回类型不一致，属于独立的编译风险。当前目标是统一使用 Unity Legacy UI Text，直接设置其 font 为已有的 STXIHEI TTF；不要重新生成字体资产。

### 3.3 尚未处理的场景警告

此前 Unity 控制台有 4 条：

The referenced script (Unknown) on this Behaviour is missing!

具体对象尚未完成定位和清理。应在编译恢复后逐场景检查。

## 4. 尚未完成的功能验证

以下功能还没有完成一轮通过编译后的回归：

- level 选关按钮进入 game。
- 方向键/WASD 移动。
- 撤销、重开、上一关、下一关。
- F12 显示/隐藏局内 GM。
- GM 跳转关卡和胜利操作。
- 通关面板和下一关流程。
- editor 打开 BuiltIn 关卡。
- 笔刷切换、格子编辑、参数编辑。
- 关卡验证和 JSON 保存。
- Generated 关卡新建、保存、试玩。
- 求解器、自动生成、多关卡求解、多线程计算。
- Excel 导出：用户最终确认要求为“一个表里面一格一个关卡 JSON”，即单个工作表中每个非空单元格放置一个完整关卡 JSON。

## 5. 下一步建议顺序

1. 统一 Legacy UI 文本实现并修复编译错误。
2. 等 Unity 编译结束，读取 Console 确认无 error。
3. 再进入 level Play Mode，确认中文字体和选关界面。
4. 完成 start → level → game 的最小流程回归。
5. 验证撤销、重开、通关和 F12 GM。
6. 验证 editor 的打开、编辑、验证、保存、试玩。
7. 定位并清理 4 条 missing script 警告。
8. 决定是否把运行时 UI 固化到场景/Prefab，使 Scene View 不再显示原始占位 UI。
9. 再实现求解、自动生成、批量求解、多线程和 Excel 导出。

## 6. 接手约束

- 所有项目文档继续放在 Assets/doc/。
- 中文文本全部使用已有字体资源 Assets/resources/fonts/STXIHEI.TTF；用户已明确不需要创建新的字体资产。
- GM 在所有构建保留，通过隐藏开关启用。
- 求解目标：先最少推箱数，再最少步数。
- 自动生成策略：随机候选 + 验证 + 求解筛选。
- Excel：单个 .xlsx，一个工作表，一个单元格一个关卡 JSON。
- 当前用户要求已暂停工作；在收到继续指令前不要修改场景、代码或数据。


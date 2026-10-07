# 技术与目录说明

对应 0.14.2。系统采用四个流程场景，编辑器在运行时工作，也包含在 Windows 独立版本中。当前未拆成独立程序集；主要类型位于 Kuluobishi.Sokoban 与 Kuluobishi.Sokoban.Editor 命名空间。

## 代码职责

| 路径与入口 | 职责 |
| --- | --- |
| Assets/Scripts/Game/SokobanCore.cs | JSON DTO、规则模拟、关卡验证、资源及文件仓库 |
| SokobanProgressStore.cs、SokobanLevelLibrary.cs | 进度、成绩、分类、排序、公开分类、索引与删除备份 |
| SokobanLevelExchange.cs、SokobanXlsx.cs | 严格 JSON 导入、关卡表格转换、Open XML 工作簿读写与整批保存 |
| SokobanSolver.cs、SokobanSolutionPlayback.cs | 搜索求解、结果与回放 |
| SokobanGenerator.cs、SokobanHtml*.cs、SokobanDifficulty.cs | 参数、构造生成、质量评价、难度估计 |
| SokobanBatchGenerator.cs、SokobanBatchVerifier.cs | 不依赖 UI 的批处理流程 |
| SokobanSceneStartup.cs、SokobanSceneControllers.cs | 场景控制器启动、主菜单与选关 |
| SokobanGameplaySceneController.cs、SokobanBoardView.cs | 游戏流程、输入、地图视野与实体显示 |
| SokobanUI.cs、SokobanBalatroSkin.cs、Sokoban*Motion.cs | 共用 UI、样式、动效 |
| SokobanPresentationAudio.cs、SokobanCrtOverlay.cs | 音频与后处理 |
| Assets/Scripts/Game/Editor/SokobanGameSceneBaker.cs | Unity 编辑器中的游戏场景/预制体烘焙工具 |
| Assets/Scripts/LevelEditor/SokobanEditorController.cs | 画布、文档、输入、剪贴板、撤销、保存与试玩 |
| SokobanEditorSolver / Generation / Management / Stamps 等 partial 文件 | 分离编辑器各功能页面，仍属于同一控制器 |
| SokobanEditorSession.cs、SokobanEditorTypes.cs | 多页签会话、未保存保护、文档快照 |
| SokobanBatchGenerationService.cs、SokobanBatchVerificationService.cs | 跨场景任务生命周期与通知 |
| Assets/Tests/Editor | 自定义模型检查，菜单与批处理构建入口 |
| Assets/Tests/SokobanStandaloneSmoke.cs 及 partial 文件 | 开发版独立运行回归与手册截图 |

表内仅给文件名的行位于前一个同组路径下。资源引用通过 Unity .meta 中的 GUID 关联。

## 规则与表现边界

规则以整数格坐标、墙数组、玩家和箱子集合表示。模拟先判断是否可移动，再更新状态和计数；UI 和动画根据结果更新。撤销使用状态或文档快照，不依赖动画反推规则。

SokobanAnimationSettings 统一管理动画偏好，默认开启，以 PlayerPrefs 即时保存；各表现组件订阅变化并在关闭时完成落位或清理动效。独立视觉时钟驱动背景、面板和 CRT 噪声，关闭时冻结，重开后继续；不使用 Time.timeScale 控制，因此不影响游戏规则、计时、音效或编辑器求解回放的步骤节奏。

game.unity 包含已烘焙的游戏 UI 和引用，棋盘运行时按格使用 Resources/Prefabs/Board 的预制体创建。其他页面主要通过 uGUI 动态创建。新工具页优先复用 SokobanUI、主题和对话框规则，避免再次各写一套数据逻辑。

玩家输入使用 Unity 旧 Input API。CRT 对鼠标位置做采样坐标映射，避免曲面变形后显示位置与按钮点击位置错位。当前编辑器不使用玩家 CRT 皮肤，逻辑色块与游戏贴图的差异属于现有设计。

## 求解与生成

求解目标为按推箱数、再按总移动数的字典序最优。A* 扩展推箱动作，玩家可达站位通过 BFS 判断，并记录完整走路与推动序列；结合反向距离、箱子/目标匹配下界及安全死锁剪枝控制搜索规模。不能把输出称为“只按总移动最短”。

生成参考 HTML 版本逐步添加墙或箱子/目标，在构造求解通过时保留、失败时回退，再按参考质量规则挑选候选。最后用本项目求解器确认推箱范围和完整解答。难度独立评估，按依赖、腾挪、争用、失败分支估算；具体权重是 35%、30%、20%、15%。

两种生成表单共用参数解析与生成器。批量层只增加数量、逐关种子、去重、进度、取消、结果保存，避免单关和批量策略分叉。Task.Run 和取消令牌用于计算，Unity 对象更新在主线程进行。

## 数据约定

关卡 JSON schemaVersion 为 1。terrain 中 # 为墙、. 为地面；坐标左上为 (0,0)，x 向右、y 向下。玩家、箱子和目标独立存储，可表达箱子/玩家叠在目标上。

verifiedMoves 为可信解答总移动数，-1 未验证，0 为合法零步解。内容修改后通过 SokobanVerificationMetadata 使旧解答、复杂度与质量失效。玩家胜利以实际箱子状态判定，不以元数据代替。

关卡 JSON 保存布局、名称、元数据、生成参数与种子、生成器版本、难度质量和解答。参考仓库 URL、参考提交、候选编号、尝试次数、求解算法名、搜索节点数和耗时不属于关卡持久数据。旧 JSON 中的这些字段读取时会被忽略；保存、复制 JSON、XLSX 配置列和导入均采用相同数据模型。

分类、顺序、删除标记和 publishedCategories 保存在 library.json。来源目录与分类相互独立；SokobanCampaign 按当前公开分类生成普通选关、解锁和下一关顺序。索引也包含历史和删除记录，关卡列表通过仓库读取有效内容。

独立运行版先读本地覆盖，再读打包 Resources；编辑器保存回工程 Resources。进度在 persistentDataPath/progress.json。印章在 data/Stamps 或持久目录 Stamps，透明格通过稀疏内容表达。

保存使用临时文件/替换、失败保留原文档；管理操作有备份及回滚。批验证写回前核对文件快照，发生外部变更时不覆盖。未保存会话只保留于当前进程，异常退出不恢复。

## JSON 与 XLSX 交换

主界面的 JSON 导入要求明确提供尺寸、地形、玩家、箱子与目标，通过结构检查后分配新 ID，打开未保存页签。关卡管理的 XLSX 导出读取已保存内容；导入先校验整表并预览，名称、分类与顺序列用于整理关卡，完整布局与元数据保存在配置JSON列。导入按顺序创建新文件，最后一次性提交分类索引；提交失败时撤回本次新建文件。

XLSX 使用 .NET ZIP 与 XML API 实现标准 Open XML 工作簿，支持内联字符串、共享字符串和富文本字符串，不依赖 Office。写入时将文本显式保存为字符串，读取时拒绝公式；对文件大小、解压大小、行列数和单元格字符数设置上限。Windows 独立运行版使用系统文件选择窗口，Unity 内使用 EditorUtility 文件选择窗口，也可手动填写完整路径。


# 素材 工具与参考来源

本文件记录项目实际使用与已知来源，不将第三方资源声明为原创。整理日期 2026-10-07；项目用途为游戏技术策划笔试展示。各项资源保留各自归属，未为整个仓库附加统一开源许可证。

## 运行素材

| 路径或内容 | 来源与作者 | 用途及说明 |
| --- | --- | --- |
| `Assets/Resources/img` | 原工程提供，具体下载来源及作者未提供 | 玩家方向帧、木箱、墙、地板、目标点等临时美术；不声明原创或已获公开再分发授权 |
| `Assets/Resources/StartLogo.png` | AI 生成 Logo；工具型号未确认 | 参考图片与原有木箱，提示词见 [Logo 记录](docs/StartLogo-Prompt.md)；参考图和木箱来源需另行追溯 |
| `Assets/Resources/fonts/STXIHEI.TTF` 及 SDF | 原工程中的华文细黑字体，许可证未随附 | 当前中文 UI 及烘焙场景使用；不能因文件已在工程内就推定可公开分发 |
| `Assets/Resources/Balatro/cards/Enhancers.png` | Balatro，LocalThunk 制作，Playstack 发行 | 卡片边框，九宫格用于面板 |
| `Assets/Resources/Balatro/ui/ui_assets.png` | 同上 | 筹码、沙漏等按钮图标 |
| `Assets/Resources/Balatro/ui/button_shape.png` | 从原版 UI 轮廓代码移植生成 | Unity 按钮九宫格遮罩，不是自行独立设计的轮廓 |
| `Assets/Resources/Balatro/ui/balatro_alt.png` | Balatro | 界面基础背景图资源 |
| `Assets/Resources/Balatro/sounds/music1–3.ogg` | Balatro | 主菜单、选关和游玩音乐 |
| `button / card3 / coin1 / crumple1.ogg` | Balatro | 按钮点击、悬停、箱子入位和推动音效 |
| `Assets/Shaders/Presentation/SokobanBackground.shader` | 移植 Balatro background.fs 公式并调整配色 | 油彩背景 |
| `Assets/Shaders/Presentation/SokobanCRTOverlay.shader` | 移植 Balatro CRT.fs 公式并调整参数 | 曲面畸变、扫描线、色差、噪声等效果 |
| `Assets/TextMesh Pro` | Unity TextMesh Pro 随附资源 | 保留目录中的字体许可与 EmojiOne Attribution 文件，按原条款处理 |

Balatro 资源来自用户本机合法安装目录中的可执行文件内 LÖVE 归档；原提取记录日期为 2026-10-06。原作链接：[Balatro](https://www.playbalatro.com/)、[Steam 商店](https://store.steampowered.com/app/2379780/Balatro/)。来源归属为 LocalThunk / Playstack，未取得可在 GitHub 公开再分发的授权证明。**署名用于说明来源，不替代资源许可。**

## 关卡生成参考

参考仓库：[huanggaole/AutoGenerateSokobanLevel](https://github.com/huanggaole/AutoGenerateSokobanLevel)。核对版本固定为提交 [4ccf418633cf47e153436723c45518ea60f8cfa9](https://github.com/huanggaole/AutoGenerateSokobanLevel/tree/4ccf418633cf47e153436723c45518ea60f8cfa9/HTML_Sokoban)。

参考内容包括 HTML 版本的逐步构造、随机放置权重、构造求解规则、质量评估、回退模板和默认配置。对应本项目 `SokobanHtmlGenerationSearch / Layout / Quality / Fallback / ConstructionSolver` 等 C# 实现。实现对应关系见 [生成设计记录](docs/development/GENERATION.md)。

本地下载的参考仓库未发现 LICENSE 文件，未据此宣称该算法代码使用 MIT 或其他特定许可证。运行时无需下载或执行原仓库代码。

## 开发工具

- Unity 2022.3.51f1c1、C#、uGUI、内置渲染管线：运行、UI、场景与构建。
- Unity TextMesh Pro：工程已有的字体和包资源；主要运行时文本使用 uGUI Text。
- Unity MCP：开发期 Unity 操作与检查，游戏运行不依赖该工具。
- Codex：代码整理、检查、文档与截图流程辅助。AI 生成 Logo 来自已有图片文件，不推定具体生成平台。
- PowerShell：模型检查与构建；Node.js：图文手册生成；Python：图片处理。

工程源代码的许可证由作者决定。正式公开或复用前，应分别处理代码、字体、美术、音频、算法参考的许可；本文件不会把第三方内容包含进一个未经确认的统一许可。

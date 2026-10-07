> 历史阶段记录：以下内容反映对应版本的设计与实现过程。当前功能和操作请阅读[新版操作手册](../USER_MANUAL.md)。

# Balatro 素材来源说明

本目录中的 `Raw` 素材来自用户本机安装的 Balatro：

`D:/steam/steamapps/common/Balatro/Balatro.exe`

资源从可执行文件内的 LÖVE 资源归档中提取到本项目，原始安装目录未被修改。提取日期：2026-10-06。

当前保留的内容包括：

- `Raw/textures`：图片与 UI 图集
- `Raw/sounds`：音乐和音效
- `Raw/fonts`：字体文件
- `Raw/shaders`：原始 Shader 源文件
- `Raw/manifest.json`：归档路径、文件大小和 SHA-256 记录

## 本次实际使用

- `Resources/Balatro/cards/Enhancers.png`：使用原图第一行第二张空白卡的边缘，九宫格缩放为主菜单、选关、棋盘和弹窗的框。
- `Resources/Balatro/ui/ui_assets.png`：使用原版筹码和沙漏图标，显示在开始、地图和暂停等按钮上。
- `Resources/Balatro/ui/button_shape.png`：从原版 `engine/ui.lua` 的 `UIElement:draw_pixellated_rect` 顶点轮廓生成的白色遮罩，用于 Unity 九宫格按钮。这是原代码轮廓的移植产物，原版没有独立的按钮 PNG。
- `Reference/engine-ui.lua` 和 `Reference/globals.lua`：从同一可执行文件归档读取的 UI 几何与色表，作为移植核对依据，不参与运行。
- `Shaders/Presentation/SokobanBackground.shader`：将 `Raw/shaders/background.fs` 的五轮油彩迭代和混色公式移植到 HLSL，保留原算法，按推箱子素材调整配色。
- `Shaders/Presentation/SokobanCRTOverlay.shader`：移植 `Raw/shaders/CRT.fs` 的交叉轴曲面畸变、边缘羽化遮罩、RGB 荧光扫描线、横向色差采样和量化噪声公式。在 Unity 内置渲染管线中对完整玩家界面统一采样一次；扫描线频率和暗角按本项目调整，未启用原版的故障条带与高成本泛光。
- `Resources/Balatro/ui/balatro_alt.png`、`Resources/Balatro/sounds`：Logo、音乐、按钮、悬停、推箱和入位声音。
- 按钮悬停使用 `Resources/Balatro/sounds/card3.ogg`，与用户指定的 `Raw/sounds/card3.ogg` 文件 SHA-256 相同。

原作 Balatro 作者：LocalThunk；发行：Playstack。本项目按用户指定的笔试展示用途标注来源。玩家、箱子、地面、墙和目标图案仍为推箱子项目原有资源。


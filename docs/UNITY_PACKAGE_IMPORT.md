# Unity 资源包导入说明

`KuroTest.unitypackage` 包含游戏与关卡编辑器的代码、场景和资源，适合导入到新建 Unity 工程中使用。完整源码工程已包含依赖和工程设置，可直接通过 Unity Hub 打开；资源包导入则需要配置目标工程。

## 创建工程并导入

1. 使用 **Unity 2022.3.51f1c1**，创建采用**内置渲染管线**的 2D 工程。
2. 在 **Window → Package Manager** 中确认已安装 **Unity UI / uGUI 1.0.0**（`com.unity.ugui`）和 **TextMesh Pro 3.0.7**（`com.unity.textmeshpro`）。
3. 点击 **Assets → Import Package → Custom Package…**，选择 `KuroTest.unitypackage`，保留全部资源并点击 **Import**，等待导入和脚本编译完成。
4. 在 **Edit → Project Settings → Player → Other Settings → Active Input Handling** 中选择 **Input Manager (Old)** 或 **Both**；如 Unity 提示重启，按提示重启。
5. 在 **Player → Resolution and Presentation** 中，将 **Fullscreen Mode** 设为 **Windowed**，默认宽高设为 **1280 / 720**，关闭 **Default Is Native Resolution**，勾选 **Resizable Window**。这样构建出的游戏支持拖动窗口边缘调整尺寸；游戏设置中可切换为无边框全屏，并自动记住模式和窗口尺寸。

普通 `.unitypackage` 包含 Assets 资源，不包含源工程的 `Packages` 与 `ProjectSettings`。下面的场景和 Shader 设置需要在目标工程中完成。

## 配置场景

在 **File → Build Settings → Scenes In Build** 中按以下顺序加入并勾选四个场景：

1. `Assets/Scenes/start.unity`
2. `Assets/Scenes/level.unity`
3. `Assets/Scenes/game.unity`
4. `Assets/Scenes/editor.unity`

即使只在 Unity 中点击 Play，也需要加入这些场景，以便主菜单、选关、游戏和编辑器之间正常切换。

## 配置动态加载的 Shader

在 **Edit → Project Settings → Graphics → Always Included Shaders** 中保留默认项，并添加以下 Shader：

| Shader 名称 | 资源文件 |
| --- | --- |
| `UI/SokobanBalatroBackground` | `Assets/Shaders/Presentation/SokobanBackground.shader` |
| `UI/SokobanCRTOverlay` | `Assets/Shaders/Presentation/SokobanCRTOverlay.shader` |
| `UI/SokobanCircleTransition` | `Assets/Shaders/Presentation/SokobanCircleTransition.shader` |

这些 Shader 按名称加载，显式加入可以避免 Windows 构建时被裁剪。需要自行构建 Windows 版本时，还应通过 Unity Hub 安装该版本的 **Windows Build Support**。

## 开始运行

打开 `Assets/Scenes/start.unity`，点击 **Play**，再点击「开始游戏」。

**进入编辑器：先点击 Game 窗口使其获得键盘焦点，再按 `=` → 展开右上角「GM ▼」→ 点击「关卡编辑器」。**

游戏和编辑器的详细操作见[图文操作手册](USER_MANUAL.md)。素材归属和来源见[素材说明](../THIRD_PARTY_NOTICES.md)。

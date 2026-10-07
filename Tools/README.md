# 工具使用说明

以下命令从项目根目录执行。运行游戏不需要 Node.js 或文档生成工具。

## 检查与 Windows 构建

先关闭该项目的 Unity 编辑器，避免同时打开同一个工程。

```powershell
./Tools/check-project.ps1 -UnityPath 'C:/Program Files/Unity 2022.3.51f1c1/Editor/Unity.exe'
./Tools/check-project.ps1 -UnityPath 'C:/Program Files/Unity 2022.3.51f1c1/Editor/Unity.exe' -BuildWindows
./Tools/check-project.ps1 -UnityPath 'C:/Program Files/Unity 2022.3.51f1c1/Editor/Unity.exe' -BuildWindows -Development
```

第一条执行模型检查；第二条构建正式版；第三条构建开发版。日志与构建放在 `Builds/ProjectChecks`，不会被 Unity 退出时清理。模型检查关闭图形设备；构建保留图形设备，以便正常处理场景环境光与反射探针。

## 更新图文手册

编辑 `docs/USER_MANUAL.md` 与 `docs/images`，然后生成图片内嵌的 HTML。需要 Node.js 及 `marked`；可将依赖放到被忽略的 Temp 中：

```powershell
npm install --prefix ./Temp/ManualTools --no-save --package-lock=false marked
node ./Tools/build-manual.mjs --marked ./Temp/ManualTools/node_modules/marked/lib/marked.esm.js
```

如果已有可解析的 `marked`，可直接运行 `node ./Tools/build-manual.mjs`。生成文件为 `docs/Sokoban-User-Manual.html`。在 Edge 或 Chrome 打开它，点击「打印或保存为 PDF」，选择 A4、纵向、关闭浏览器页眉页脚，输出到 `docs/Sokoban-User-Manual.pdf`。页面会应用打印样式并显示目录；浏览器版本与字体可能影响分页，更新后应检查每页的表格、图片和中文显示。

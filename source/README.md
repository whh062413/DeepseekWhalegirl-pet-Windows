# 源码说明 / Source

## 构建

程序使用 C#、Windows Forms 和 Win32 接口，目标为 Windows / .NET Framework 4.x。构建脚本调用 Windows .NET Framework 的 `csc.exe`。

从仓库根目录执行：

```powershell
powershell -ExecutionPolicy Bypass -File .\source\重新编译.ps1
```

输出为 `dist\鲸鱼娘桌宠.exe`。重新构建前先退出运行中的桌宠，避免 EXE 被占用。`dist/` 已加入忽略规则，发布的 EXE 和运行包放在 GitHub Releases。

`build.ps1` 可接收 `-SpritePath`、`-OutputPath` 和 `-IconPath` 参数，便于替换素材或指定输出位置。正常构建使用 `重新编译.ps1` 即可。

## 文件与素材

| 路径 | 用途 |
| --- | --- |
| `PetApp.cs` | 桌宠窗口、动画、交互、设置及诊断入口 |
| `重新编译.ps1` | 使用仓库内素材构建可执行文件 |
| `build.ps1` | 调用 C# 编译器，内嵌精灵图并设置图标 |
| `art/sprites.png` | 最终透明动作图集 |
| `art/pet.ico` | 程序及托盘图标 |
| `art/prompts.json` | 初始美术生成提示词 |
| `art/iron-basin-edit-prompt.txt` | 铁盆动作修改提示词 |
| `REFERENCES.md` | 角色、视频及图片参考来源 |
| `checks/NativeInputAudit.cs` | 原生窗口消息交互检查工具源码 |

图集为 **4 列 × 17 行**，每格 **320 × 320 像素**，共 **68 帧**。每行对应一个动作，行顺序必须与 `PetAction` 枚举一致：

```text
Idle, Walk, Feed, Happy, Sleep, Think, Curious, Surprise,
Pout, Shy, Stretch, TailHug, BegRice, Victory, TailSnack, Wake, Bow
```

帧编号从 0 开始，按从左到右、从上到下排列。铁盆修改涉及吃饭的 `8–11` 帧、端盆讨饭的 `48–51` 帧，以及尾巴藏饭的 `56–59` 帧。修改图集时需保持格子尺寸和顺序。

图集会内嵌到 EXE 中；运行时无需外置素材、Codex、Python、Node.js 或网络连接。用户设置保存在 `%LOCALAPPDATA%\DeepSeekWhalePet`。

## 诊断选项

在仓库根目录构建后，可执行以下命令。报告和导出文件放入忽略的 `dist/`：

```powershell
# 逻辑与素材自检
& '.\dist\鲸鱼娘桌宠.exe' --self-test '--report=dist\self-test.txt'

# 在桌宠窗口中循环展示动作，持续约 46 秒
& '.\dist\鲸鱼娘桌宠.exe' --showcase-test=46 '--report=dist\showcase-test.txt'

# 短时运行检查
& '.\dist\鲸鱼娘桌宠.exe' --smoke-test=8 '--report=dist\smoke-test.txt'

# 导出动作帧或静态预览
& '.\dist\鲸鱼娘桌宠.exe' '--export-frames=dist\frames'
& '.\dist\鲸鱼娘桌宠.exe' '--preview=dist\preview.png'
```

`checks/NativeInputAudit.cs` 通过原生窗口消息检查程序的交互处理，不移动全局鼠标指针。此类自动检查不等于人工鼠标体验验收，也不能替代不同显示缩放、多屏环境下的实际测试。

## English

`PetApp.cs` implements the Windows desktop pet using Windows Forms and Win32 APIs. From the repository root, run `powershell -ExecutionPolicy Bypass -File .\source\重新编译.ps1`; the executable is written to the ignored `dist/` directory.

`art/sprites.png` is a transparent 4 × 17 atlas with 320 × 320 pixel cells. Its 68 frames follow the `PetAction` enum in row order and are embedded at build time. `art/pet.ico` supplies the application and tray icon. Runtime preferences are stored under `%LOCALAPPDATA%\DeepSeekWhalePet`.

The diagnostic commands above cover self-tests, a timed action showcase, a short run, frame export, and a static preview. The native-message audit harness exercises application handlers without moving the global pointer; it does not establish manual interaction or mixed-DPI acceptance.

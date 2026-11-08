# MusicWave · 音乐律动桌面

Windows 原生 C# / WPF 程序。真实桌面图标随音乐起伏，壁纸由静止背景和一个或多个动态图层组成。动态图层围绕底部锚点压扁、回弹、上下跳动，接近参考视频的节拍变形。

## 直接运行

双击 `artifacts/MusicWave/MusicWave.exe`。这是 Windows x64 自包含版本，无需安装 .NET，也不需要管理员权限。首次运行自带可调节的分层星球演示。也可双击根目录的 `run.cmd`。

1. 在窗口里先看实时预览，点击 **应用到桌面** 才会控制真实桌面。
2. 播放音乐，勾选 **自动识别系统音乐 BPM**。选择音乐实际播放的输出设备，等约 4–12 秒分析节拍。默认输出变更或耳机重新连接后，重新选择设备以重启监听。
3. 需要固定速度时取消自动识别，输入 BPM 或拖动滑块。可点按定速，或用 ½ / 1× / 2× 调节倍速。有效频率显示在预览下方，`Hz = BPM × 倍速 ÷ 60`。
4. 点击 **停止并恢复**，恢复图标原始位置并移除动态壁纸。关闭窗口也会停止并恢复；最小化或“收起到托盘”会继续后台运行，双击托盘图标打开控制台。

## 制作视频里的分层效果

准备一张不动的背景图片，以及一张或多张角色的**透明背景 PNG**。程序对独立图片做变形；导入完整合成图时，整张图片会一起变形。视频不是可编辑的分层源文件，本程序不自动从视频抠图，也不将 MP4 当作壁纸播放。

1. 用 **静态背景** 选择背景图。最底部的静态背景默认铺满屏幕并裁切填充。
2. 用 **＋动态图层** 导入透明 PNG；每个角色可单独添加为一个图层。
3. 选中图层，在右侧调整名称、显示、静止、透明度、位置和尺寸。
4. **压扁回弹** 设置为 30%–60%，**上下弹跳** 设置为 0–40 px，即可得到视频里底部固定、角色压扁再弹起的感觉。纯压扁效果把上下弹跳设为 0。
5. 相位都设为 0、独立速度都设为 1，所有角色就同步运动；修改相位可做错落的波浪。
6. 列表从上到下为从后到前；↑ / ↓ 改变遮挡顺序。勾选 **静止图层** 后，该图层的所有运动参数都不生效。

可将一张或多张图片文件直接拖入窗口，自动新增动态图层；拖到预览里时，图片底部中心放在鼠标落点，多张图片略微错开。支持 PNG、JPEG、BMP、TIFF，无法读取的图片会提示并跳过。

在预览里点击前景图层即可选中并拖动位置，透明 PNG 的留白会穿透选取后面的角色，选中状态与图层列表、右侧属性同步。拖动选中框的八个边角手柄可独立拉伸宽高，按住 **Shift** 保持当前比例；按 **Esc** 取消本次拖动。操作期间该图层在预览中保持当前动画姿态，松开后继续律动。最底部的静态背景不参与鼠标选取；可在列表中调整其属性。

位置使用屏幕百分比：X 是水平中心，Y 是图片底部。默认宽、高定义保持图片比例的容纳区域；预览拉伸后会自动勾选 **自由拉伸图片**，宽、高直接控制图片尺寸，取消勾选可恢复按原始比例容纳。比如 X=50、Y=90 就是屏幕下方居中。图层与图标的像素幅度均以 1920 宽设计画布为基准，按各自所在显示器的宽度等比缩放。例如在 3840 宽的 4K 屏幕上，图标幅度 26 对应实际 52 屏幕像素。预览使用所选显示器的真实图标位置、相同的缩放、相位与像素取整；图形使用方块示意，启动或应用时读取布局。

支持 PNG、JPEG、BMP、TIFF 静态图片。PNG 的透明通道会保留。没有硬编码的图层数量限制，但大图与图层数量会增加显存、内存和绘制开销。

## 图层组与整体动画

1. 在图层列表中按住 **Ctrl** 或 **Shift** 选择多个前景图层，点击 **新建组**。最底部的静态背景不会被加入组。
2. 右侧切换到 **组动画**，选择要编辑的组，可重命名并统一调整上下弹跳、压扁回弹、左右摆动、节拍相位和组动画速度，也可勾选 **静止整个组**。
3. 组内成员围绕同一个底部锚点整体变形，成员之间的相对位置也随组缩放。新组采用首个成员当前的动画参数并启用动画；成员自身的动画参数保留，在组内由组动画接管。
4. 紫色虚线框表示当前组，圆形十字表示组锚点。锚点初始取成员布局区域的底部中心；成员移动后锚点保持不变，可在右侧输入锚点百分比，或点击 **按成员布局重置锚点**。
5. 选择右侧的目标组后，多选图层并点击 **加入当前组**。点击 **移出组** 可将所选图层恢复为独立动画；**解散组** 会保留全部成员图层、位置、尺寸和原有独立参数。空组会自动清理。

每个图层最多属于一个组。分组不改变图层列表的前后遮挡顺序，成员的显示、位置、尺寸和透明度仍可单独调整。成员列表显示所属组名，图层属性中的 **编辑所属组动画** 可直接打开对应组。分组、成员关系、组动画和锚点会自动保存，并随导出项目一同保存；旧项目仍可载入。

## 图标、壁纸和恢复

- 图标是 Explorer 里的真实图标，仍可双击和操作；鼠标左键按住时临时停止写入位置。修改布局前建议先停止动画，运行中的拖动会在后续帧回到动画基准位置。
- 图标采用 Windows Shell `IFolderView2` API，不向 Explorer 注入代码，也不读取其进程内存。启动时暂时关闭自动排列和对齐网格，停止后恢复之前的选项。
- 壁纸窗口位于图标下方、系统背景上方，不永久更改系统壁纸。旧版 Windows 使用独立 WorkerW；Windows 11 24H2 使用 Progman 内的原生分层窗口，并校验它在图标和系统背景之间的顺序。退出后原壁纸重新可见。
- 应用图标动画之前先写恢复快照。异常终止后重新打开程序会尝试恢复；也可点击“恢复图标位置”。恢复期间按图标身份匹配，跳过已删除或重命名的图标。
- 显示器布局变更会停止并恢复，需要重新选择目标显示器、重新应用。壁纸一次运行在所选显示器；图标作用于整个桌面。
- `Ctrl+Alt+P` 暂停/继续，`Ctrl+Alt+R` 停止并恢复。快捷键被占用时窗口会提示，按钮仍可使用。

## 项目与配置

参数自动保存在 `%LOCALAPPDATA%/MusicWave/settings.json`。图标恢复文件为同目录的 `desktop-recovery.json`，错误日志为 `error.log`。

“导出项目”保存 JSON 图层和参数，图片保持原路径引用。分享项目时需要一并保留图片；手动将图片路径改为相对项目文件的路径也可载入。导入前会检查图片能否读取，失败时保留现有项目。

## 编译

需要 Windows x64 和 .NET 8 SDK。

```powershell
dotnet build src/MusicWave/MusicWave.csproj -c Release
dotnet run --project src/MusicWave/MusicWave.csproj -c Release
```

打包免安装版本：

```powershell
.\build.ps1 -Portable
```

运行自检（报告输出到指定文件）：

```powershell
.\artifacts\MusicWave\MusicWave.exe --self-test G:\music-wave-desktop\artifacts\checks\self-test.txt
.\artifacts\MusicWave\MusicWave.exe --desktop-check G:\music-wave-desktop\artifacts\checks\desktop-check.txt
.\artifacts\MusicWave\MusicWave.exe --audio-smoke G:\music-wave-desktop\artifacts\checks\audio-smoke.txt
```

`--desktop-check` 只读取桌面信息，`--wallpaper-check` 输出桌面窗口层级。开发用的 `--desktop-smoke` 会实际移动图标并显示 3 秒壁纸，检查实际屏幕上未被其他应用遮挡的桌面像素是否改变，再验证图标坐标和排列标志完整恢复；使用前应退出程序主窗口并露出部分桌面。

1.0.4 修复图标逻辑坐标持续变化、屏幕画面却保持静止的问题。在本机 Windows 11 24H2 上复现：仅调用 Shell 定位和强制重绘均不能刷新图标图像；在每批定位后向可见图标视图发送 `WM_SETREDRAW(TRUE)`，画面才会跟随坐标更新。运行、停止及异常恢复均执行此提交，隐藏的图标视图不会被显示。新增实际屏幕图像匹配测试：在固定背景下匹配 5065 个图标像素，实测图像移动 52 像素后准确返回，图像匹配误差为 0，并验证图标坐标和排列选项完整恢复。

包含图标画面提交后的本机 4K、12 个图标性能复测约 51 FPS，Shell 定位与提交平均约 12 ms；旧版约 60 FPS 的循环测量没有验证图标画面是否更新。实际帧率取决于 Explorer 重绘开销和系统负载。

```powershell
# 检查实际屏幕上的图标图像位移、返回和恢复；请先停止主窗口的桌面效果，并露出左上角图标
.\artifacts\MusicWave\MusicWave.exe --icon-visual-check G:\music-wave-desktop\artifacts\checks\icon-presentation.txt
# 运行期间采集未被其他窗口遮挡的图标区域，保存 12 帧 PNG 和逻辑坐标用于对照
.\artifacts\MusicWave\MusicWave.exe --icon-visual-observe G:\music-wave-desktop\artifacts\checks\icons-visual.txt
```

1.0.3 统一图标与预览的缩放：真实桌面和预览共用基于 1920 宽画布的图标位移算法，4K 屏幕不再遗漏 2 倍缩放。波浪相位以图标在各自屏幕上的设计坐标计算，预览读取真实布局并保持显示器宽高比。本机 4K、12 个图标完成 38 个姿态的 Shell 坐标检查：幅度 26 对应坐标变化 52 像素，幅度 72 对应坐标变化 144 像素，静音回到原位，所有图标位置和排列选项准确恢复。

```powershell
# 只读取运行中图标的实际行程，不修改桌面
.\artifacts\MusicWave\MusicWave.exe --icon-observe G:\music-wave-desktop\artifacts\checks\icons-observe.txt
# 会临时移动图标并检查实际幅度、相位、预览布局和恢复；请先停止主窗口的桌面效果
.\artifacts\MusicWave\MusicWave.exe --icon-motion-check G:\music-wave-desktop\artifacts\checks\icons-motion.txt
```

1.0.2 修复实际桌面与图标卡顿：壁纸改用 DirectComposition / Direct3D 11，将图层像素缓存到 GPU，只提交位移、缩放和透明度，不再逐帧使用 WPF 离屏渲染、整帧回读和 GDI 放大。图层按照显示器原生坐标合成；演示图层的缓存纹理最长边不超过 4096。图标和壁纸由独立的高精度时钟以 60 Hz 调度，错过的帧直接跳过，窗口收起后仍继续运行。相同图标坐标不重复写入 Explorer，静止画面不重复提交 GPU，隐藏控制台时不再绘制预览。

本机 3840×2160、11 个图标、默认演示项目测得：旧版循环约 6.5 FPS，壁纸单帧 CPU 耗时约 102 ms；新版带预览测试整体约 59 FPS，隐藏窗口后约 60 FPS，壁纸单帧 CPU 提交约 0.03 ms（不代表 GPU 绘制耗时）。实际屏幕像素与 WPF 参考图像核对通过，并验证透明 PNG、图层调整和图标完整恢复。帧率会随图标数量、Explorer 响应和系统负载变化。

性能复测（会临时移动图标，请先停止主窗口的桌面效果）：

```powershell
.\artifacts\MusicWave\MusicWave.exe --animation-benchmark G:\music-wave-desktop\artifacts\checks\animation-after.txt
```

报告包含帧间隔、壁纸提交耗时、Shell 调用耗时，以及预览显示和控制台隐藏两个阶段。1.0.1 引入的独立原生宿主窗口继续用于兼容 Windows 11 24H2，避免重新挂载 WPF 窗口后不显示的问题。

已在本机 Windows 11（26100）、3840×2160 显示器验证：12 个真实图标位移/准确恢复、原生分层壁纸实际屏幕像素改变、WASAPI 音频启停、72/90/120/150/180 BPM 合成节拍检测、透明 PNG 读取、项目保存/载入和界面操作。其他 Windows 10/11 Explorer 版本、多显示器与不同缩放组合尚未逐一验证。Explorer 的壁纸宿主机制并非公开 API，系统版本或其他动态壁纸软件可能影响它；连接失败会显示错误并回滚。

自动 BPM 当前搜索范围约 60–240。弱鼓点、变拍、半拍/双拍歧义时可能不准确，可使用手动 BPM、倍速或点按校准。系统静音时自动模式缓慢回到原位。手动模式不依赖任何音频设备。

## 代码结构

- `Audio/`：WASAPI 输出回环采集、低频能量与正向能量变化、自相关 BPM 估计。
- `Animation.cs`：共享节拍时钟、相位校正、静音渐变与底部锚点变形。
- `Desktop/`：Shell 图标坐标、恢复快照、WorkerW 壁纸窗、托盘和全局快捷键。
- `Rendering/`：WPF 预览与 DirectComposition 桌面合成，共享图层参数、演示素材和变形数学；GPU 缓存与透明合成。
- `FramePump.cs`：60 Hz 高精度时钟、绝对时间调度与积压帧丢弃。
- `MainWindow.xaml`：中文控制台、图层编辑、输出设备和显示器选择。

主要 API 参考：[桌面图标位置的官方示例](https://devblogs.microsoft.com/oldnewthing/20130318-00/?p=4933/)、[IFolderView2](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-ifolderview2)、[WM_SETREDRAW](https://learn.microsoft.com/en-us/windows/win32/gdi/wm-setredraw)、[NAudio 2.2.1 回环采集](https://github.com/naudio/NAudio/blob/v2.2.1/Docs/WasapiLoopbackCapture.md)。NAudio 依赖使用 MIT 许可。

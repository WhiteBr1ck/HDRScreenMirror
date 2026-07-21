using System.Globalization;

namespace HDRScreenMirror;

internal enum UiLanguage
{
    Chinese,
    English
}

internal static class Localization
{
    internal const string Automatic = "auto";
    internal const string SimplifiedChinese = "zh-CN";
    internal const string English = "en-US";

    private static readonly IReadOnlyDictionary<string, string> ChineseStrings =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["CaptureDisplay"] = "捕获显示器",
            ["OutputDisplay"] = "输出显示器",
            ["PaperWhite"] = "SDR 白点亮度",
            ["Present"] = "呈现",
            ["InteractionSafety"] = "交互与安全",
            ["WindowManagement"] = "窗口管理",
            ["GlobalHotkeys"] = "全局快捷键",
            ["CurrentStatus"] = "当前状态",
            ["ResetDefault"] = "恢复默认",
            ["AllOutputs"] = "输出到所有同显卡显示器",
            ["VSync"] = "垂直同步",
            ["StatusOverlay"] = "输出屏状态面板",
            ["RenderCursor"] = "在输出屏渲染鼠标",
            ["MouseThrough"] = "鼠标穿透输出画面",
            ["EnableHotkeys"] = "启用全局快捷键",
            ["MinimizeToTray"] = "关闭时最小化到托盘",
            ["MoveOutputWindows"] = "开始镜像时将输出屏窗口移到捕获屏",
            ["RefreshDisplays"] = "重新枚举显示器",
            ["StartMirror"] = "开始 HDR 镜像",
            ["Stop"] = "停止",
            ["About"] = "关于",
            ["Language"] = "语言",
            ["ShortcutText"] = "Ctrl + Alt + Shift + M   开始或停止镜像\r\n" +
                               "Ctrl + Alt + Shift + Q   紧急停止镜像\r\n" +
                               "Ctrl + Alt + Shift + H   召回主界面到捕获显示器",
            ["MainNote"] = "镜像运行时会自动移回捕获显示器并临时置顶，停止后取消置顶。" +
                           "“输出到所有”会驱动捕获屏之外、连接在同一块 GPU 上的全部显示器。",
            ["PaperWhiteHelp"] = "nits。仅影响 SDR 回退与鼠标亮度；HDR 主画面不受影响。",
            ["StatusEnumerating"] = "正在枚举显示器……",
            ["StatusNoDisplays"] = "没有找到连接到桌面的 DXGI 显示器。",
            ["StatusFoundDisplays"] = "找到 {0} 个显示输出。请选择捕获与输出显示器。",
            ["EnumerateFailed"] = "枚举显示器失败",
            ["NoGpuOutputs"] = "没有找到与捕获屏连接在同一块 GPU 上的其他显示器。",
            ["NoOutput"] = "没有可用输出",
            ["NonHdrWarning"] = "以下目标没有报告 HDR10 色彩空间：{0}\r\n继续运行可能导致亮度或颜色不正确。仍然继续吗？",
            ["NonHdrTitle"] = "部分目标显示器未开启 HDR",
            ["StatusMirroring"] = "镜像运行中，正在输出到 {0} 个显示器。",
            ["StartFailed"] = "启动 HDR 镜像失败",
            ["SameDisplay"] = "捕获显示器和输出显示器不能相同。",
            ["CrossGpu"] = "当前版本不支持跨 GPU 镜像。请选择连接在同一块显卡上的输出。",
            ["StatusStopped"] = "已停止。",
            ["RuntimeFailed"] = "HDR 镜像运行失败",
            ["HotkeyFailed"] = "部分全局快捷键注册失败，可能已经被其他程序占用。",
            ["TrayShow"] = "显示主界面",
            ["TrayStart"] = "开始镜像",
            ["TrayStop"] = "停止镜像",
            ["TrayExit"] = "退出程序",
            ["TrayRunning"] = "HDRScreenMirror 正在镜像",
            ["AboutTitle"] = "关于 HDRScreenMirror",
            ["AboutSubtitle"] = "Windows HDR 屏幕镜像工具",
            ["AboutVersion"] = "版本 {0}  ·  x64",
            ["AboutDetails"] = "捕获：DXGI Desktop Duplication\r\n渲染：Direct3D 11\r\n输出：FP16 scRGB HDR",
            ["Close"] = "关闭",
            ["HdrOn"] = "HDR 已开启",
            ["HdrOff"] = "HDR 未开启",
            ["OverlayStarting"] = "正在启动",
            ["OverlayWaiting"] = "等待输入信号",
            ["OverlayHotkeys"] = "Ctrl + Alt + Shift + Q 停止   ·   H 召回主界面",
            ["OverlayMetrics"] = "{0,4:F1} fps   鼠标 {1}",
            ["CursorOn"] = "开",
            ["CursorOff"] = "关",
            ["NeedOutput"] = "至少需要一个输出显示器。",
            ["SessionStarted"] = "镜像会话已经启动。",
            ["OpenAdapterFailed"] = "无法重新打开捕获显示器对应的显卡。",
            ["OpenCaptureFailed"] = "无法重新打开捕获显示器。",
            ["D3DReady"] = "Direct3D 11 已初始化，正在等待第一帧……",
            ["OutputCrossGpu"] = "输出显示器 {0} 不在捕获显卡上，当前版本不支持跨 GPU 输出。",
            ["OpenOutputFailed"] = "无法打开输出显示器 {0}。",
            ["Fp16Unsupported"] = "目标显示器 {0} 不支持 FP16 scRGB 交换链。请确认 Windows HDR 已开启。",
            ["Hdr"] = "HDR",
            ["SdrFallback"] = "SDR 回退",
            ["CursorDisabled"] = "已关闭",
            ["CursorHidden"] = "隐藏",
            ["CursorRendered"] = "已渲染",
            ["CursorWaiting"] = "等待形状",
            ["RunningStatus"] = "运行中  {0:F1} fps  输入={1}  {2}  尺寸={3}×{4}  鼠标={5}  总帧={6}",
            ["Running"] = "运行中",
            ["UnsupportedFormat"] = "不支持捕获格式 {0}。",
            ["InvalidPointerBuffer"] = "Desktop Duplication 返回了无效的鼠标形状缓冲区大小。",
            ["AppStartFailed"] = "HDRScreenMirror 启动失败"
        };

    private static readonly IReadOnlyDictionary<string, string> EnglishStrings =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["CaptureDisplay"] = "Capture display",
            ["OutputDisplay"] = "Output display",
            ["PaperWhite"] = "SDR white level",
            ["Present"] = "Presentation",
            ["InteractionSafety"] = "Interaction & safety",
            ["WindowManagement"] = "Window management",
            ["GlobalHotkeys"] = "Global hotkeys",
            ["CurrentStatus"] = "Current status",
            ["ResetDefault"] = "Reset default",
            ["AllOutputs"] = "All displays on this GPU",
            ["VSync"] = "VSync",
            ["StatusOverlay"] = "Output status panel",
            ["RenderCursor"] = "Render cursor on output",
            ["MouseThrough"] = "Click through mirror",
            ["EnableHotkeys"] = "Enable global hotkeys",
            ["MinimizeToTray"] = "Minimize to tray on close",
            ["MoveOutputWindows"] = "Move output windows to capture display on start",
            ["RefreshDisplays"] = "Refresh displays",
            ["StartMirror"] = "Start HDR mirror",
            ["Stop"] = "Stop",
            ["About"] = "About",
            ["Language"] = "Language",
            ["ShortcutText"] = "Ctrl + Alt + Shift + M   Start or stop mirroring\r\n" +
                               "Ctrl + Alt + Shift + Q   Emergency stop\r\n" +
                               "Ctrl + Alt + Shift + H   Recall this window to the capture display",
            ["MainNote"] = "While mirroring, this window returns to the capture display and stays temporarily on top. " +
                           "“All displays” drives every display on the same GPU except the capture display.",
            ["PaperWhiteHelp"] = "nits. Affects SDR fallback and cursor brightness; the HDR image is unchanged.",
            ["StatusEnumerating"] = "Enumerating displays…",
            ["StatusNoDisplays"] = "No desktop attached DXGI display was found.",
            ["StatusFoundDisplays"] = "Found {0} display outputs. Select a capture and output display.",
            ["EnumerateFailed"] = "Display enumeration failed",
            ["NoGpuOutputs"] = "No other display was found on the same GPU as the capture display.",
            ["NoOutput"] = "No output available",
            ["NonHdrWarning"] = "These targets do not report an HDR10 color space: {0}\r\nContinuing may produce incorrect brightness or color. Continue anyway?",
            ["NonHdrTitle"] = "HDR is not enabled on some targets",
            ["StatusMirroring"] = "Mirroring to {0} display outputs.",
            ["StartFailed"] = "Failed to start HDR mirroring",
            ["SameDisplay"] = "The capture and output displays cannot be the same.",
            ["CrossGpu"] = "Cross GPU mirroring is not supported. Select an output connected to the same GPU.",
            ["StatusStopped"] = "Stopped.",
            ["RuntimeFailed"] = "HDR mirroring failed",
            ["HotkeyFailed"] = "Some global hotkeys could not be registered. Another application may already use them.",
            ["TrayShow"] = "Show control panel",
            ["TrayStart"] = "Start mirroring",
            ["TrayStop"] = "Stop mirroring",
            ["TrayExit"] = "Exit",
            ["TrayRunning"] = "HDRScreenMirror is mirroring",
            ["AboutTitle"] = "About HDRScreenMirror",
            ["AboutSubtitle"] = "Windows HDR screen mirroring tool",
            ["AboutVersion"] = "Version {0}  ·  x64",
            ["AboutDetails"] = "Capture: DXGI Desktop Duplication\r\nRendering: Direct3D 11\r\nOutput: FP16 scRGB HDR",
            ["Close"] = "Close",
            ["HdrOn"] = "HDR enabled",
            ["HdrOff"] = "HDR disabled",
            ["OverlayStarting"] = "Starting",
            ["OverlayWaiting"] = "Waiting for input",
            ["OverlayHotkeys"] = "Ctrl + Alt + Shift + Q Stop   ·   H Show control panel",
            ["OverlayMetrics"] = "{0,4:F1} fps   CURSOR {1}",
            ["CursorOn"] = "ON ",
            ["CursorOff"] = "OFF",
            ["NeedOutput"] = "At least one output display is required.",
            ["SessionStarted"] = "The mirroring session has already started.",
            ["OpenAdapterFailed"] = "The GPU for the capture display could not be reopened.",
            ["OpenCaptureFailed"] = "The capture display could not be reopened.",
            ["D3DReady"] = "Direct3D 11 is ready and waiting for the first frame…",
            ["OutputCrossGpu"] = "Output display {0} is not on the capture GPU. Cross GPU output is not supported.",
            ["OpenOutputFailed"] = "Output display {0} could not be opened.",
            ["Fp16Unsupported"] = "Output display {0} does not support an FP16 scRGB swap chain. Confirm that Windows HDR is enabled.",
            ["Hdr"] = "HDR",
            ["SdrFallback"] = "SDR fallback",
            ["CursorDisabled"] = "disabled",
            ["CursorHidden"] = "hidden",
            ["CursorRendered"] = "rendered",
            ["CursorWaiting"] = "waiting for shape",
            ["RunningStatus"] = "Running  {0:F1} fps  input={1}  {2}  size={3}×{4}  cursor={5}  frames={6}",
            ["Running"] = "Running",
            ["UnsupportedFormat"] = "Unsupported capture format: {0}.",
            ["InvalidPointerBuffer"] = "Desktop Duplication returned an invalid pointer shape buffer size.",
            ["AppStartFailed"] = "HDRScreenMirror failed to start"
        };

    private static UiLanguage _current = DetectWindowsLanguage();

    static Localization()
    {
        string[] missingEnglish = ChineseStrings.Keys.Except(EnglishStrings.Keys).ToArray();
        string[] missingChinese = EnglishStrings.Keys.Except(ChineseStrings.Keys).ToArray();
        if (missingEnglish.Length > 0 || missingChinese.Length > 0)
        {
            throw new InvalidOperationException(
                $"Localization keys do not match. Missing English: {string.Join(", ", missingEnglish)}; " +
                $"missing Chinese: {string.Join(", ", missingChinese)}.");
        }
    }

    public static UiLanguage Current => _current;

    public static void SetPreference(string? preference)
    {
        _current = preference switch
        {
            SimplifiedChinese => UiLanguage.Chinese,
            English => UiLanguage.English,
            _ => DetectWindowsLanguage()
        };
    }

    public static string T(string key)
    {
        IReadOnlyDictionary<string, string> strings =
            _current == UiLanguage.Chinese ? ChineseStrings : EnglishStrings;
        return strings.TryGetValue(key, out string? value) ? value : key;
    }

    public static string F(string key, params object?[] arguments) =>
        string.Format(CultureInfo.CurrentCulture, T(key), arguments);

    private static UiLanguage DetectWindowsLanguage() =>
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("zh", StringComparison.OrdinalIgnoreCase)
            ? UiLanguage.Chinese
            : UiLanguage.English;
}

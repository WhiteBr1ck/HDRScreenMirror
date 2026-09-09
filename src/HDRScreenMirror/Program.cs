using HDRScreenMirror.DirectX;
using HDRScreenMirror.Interop;
using HDRScreenMirror.UI;
using System.Diagnostics;

namespace HDRScreenMirror;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Any(x => string.Equals(x, "--list", StringComparison.OrdinalIgnoreCase)))
        {
            NativeMethods.AttachToParentConsole();
            PrintDisplays();
            return;
        }

        if (args.Any(x => string.Equals(x, "--smoke", StringComparison.OrdinalIgnoreCase)))
        {
            NativeMethods.AttachToParentConsole();
            ApplicationConfiguration.Initialize();
            RunSmokeTest(args);
            return;
        }

        bool uiDiagnostics = args.Any(x => string.Equals(x, "--ui-debug", StringComparison.OrdinalIgnoreCase));
        if (uiDiagnostics)
            NativeMethods.AttachToParentConsole();

        using SingleInstanceCoordinator singleInstance = new();
        if (!singleInstance.IsPrimary)
        {
            singleInstance.SignalPrimary();
            return;
        }

        AppSettings settings = AppSettings.Load();
        Localization.SetPreference(settings.Language);
        ApplicationConfiguration.Initialize();
        Application.ThreadException += (_, eventArgs) =>
        {
            if (uiDiagnostics)
                Console.Error.WriteLine(eventArgs.Exception);
            MessageBox.Show(eventArgs.Exception.ToString(), "HDRScreenMirror", MessageBoxButtons.OK, MessageBoxIcon.Error);
        };

        try
        {
            using ControlForm controlForm = new(settings);
            controlForm.Shown += (_, _) =>
                singleInstance.StartListening(controlForm.RecallFromSecondInstance);
            Application.Run(controlForm);
        }
        catch (Exception exception)
        {
            if (uiDiagnostics)
                Console.Error.WriteLine(exception);
            MessageBox.Show(exception.ToString(), Localization.T("AppStartFailed"), MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static void PrintDisplays()
    {
        try
        {
            foreach (DisplayTarget display in DxgiDisplayEnumerator.GetDisplays())
            {
                Console.WriteLine(display.ToDiagnosticString());
            }
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            Environment.ExitCode = 1;
        }
    }

    private static void RunSmokeTest(string[] args)
    {
        int captureIndex = ReadIntArgument(args, "--capture", 0);
        int presentIndex = ReadIntArgument(args, "--present", 1);
        int seconds = Math.Clamp(ReadIntArgument(args, "--seconds", 3), 1, 30);
        int frameRateLimit = Math.Clamp(ReadIntArgument(args, "--fps", 60), 24, 500);
        FrameRateMode frameRateMode = ReadStringArgument(args, "--frame-rate", "output") switch
        {
            "fixed" => FrameRateMode.Fixed,
            "unlimited" => FrameRateMode.Unlimited,
            _ => FrameRateMode.FollowOutput
        };
        bool allOutputs = args.Any(x => string.Equals(x, "--all", StringComparison.OrdinalIgnoreCase));

        try
        {
            IReadOnlyList<DisplayTarget> displays = DxgiDisplayEnumerator.GetDisplays();
            DisplayTarget capture = displays.Single(x => x.GlobalIndex == captureIndex);
            IReadOnlyList<DisplayTarget> targets = allOutputs
                ? displays.Where(x => x.GlobalIndex != capture.GlobalIndex && x.AdapterIndex == capture.AdapterIndex).ToArray()
                : [displays.Single(x => x.GlobalIndex == presentIndex)];
            if (targets.Count == 0 || targets.Any(x => x.GlobalIndex == capture.GlobalIndex))
                throw new InvalidOperationException("冒烟测试要求至少一个不同于捕获屏的输出显示器。");
            if (targets.Any(x => x.AdapterIndex != capture.AdapterIndex))
                throw new InvalidOperationException("冒烟测试要求所有输出连接在捕获屏所在的 GPU。 ");

            List<MirrorForm> mirrorWindows = [];
            List<StatusOverlayForm> statusOverlays = [];
            List<AnalysisOverlayForm> analysisOverlays = [];
            List<MirrorOutputBinding> bindings = [];
            foreach (DisplayTarget target in targets)
            {
                MirrorForm mirrorWindow = new(target.Bounds, false, false);
                mirrorWindow.Show();
                mirrorWindows.Add(mirrorWindow);
                bindings.Add(new MirrorOutputBinding(target, mirrorWindow.Handle));
                StatusOverlayForm statusOverlay = new(target.Bounds, capture, target, false, true, false, false);
                statusOverlay.Show(mirrorWindow);
                statusOverlays.Add(statusOverlay);
                AnalysisOverlayForm analysisOverlay = new(target.Bounds, capture, true, true, false);
                analysisOverlay.Show(mirrorWindow);
                analysisOverlays.Add(analysisOverlay);
            }
            Application.DoEvents();

            Exception? runtimeFailure = null;
            string lastStatus = "尚未收到状态";
            using MirrorSession session = new(
                capture,
                bindings,
                203,
                frameRateMode,
                frameRateLimit);
            session.StatusChanged += status =>
            {
                lastStatus = status;
                Console.WriteLine(status);
            };
            session.TelemetryChanged += telemetry =>
            {
                foreach (StatusOverlayForm overlay in statusOverlays)
                {
                    if (overlay.IsHandleCreated && !overlay.IsDisposed)
                        overlay.BeginInvoke(() => overlay.UpdateTelemetry(telemetry));
                }
                foreach (AnalysisOverlayForm overlay in analysisOverlays)
                {
                    if (overlay.IsHandleCreated && !overlay.IsDisposed)
                        overlay.BeginInvoke(() => overlay.UpdateMirrorTelemetry(telemetry));
                }
            };
            session.LuminanceChanged += telemetry =>
            {
                foreach (StatusOverlayForm overlay in statusOverlays)
                {
                    if (overlay.IsHandleCreated && !overlay.IsDisposed)
                        overlay.BeginInvoke(() => overlay.UpdateLuminance(telemetry));
                }
                foreach (AnalysisOverlayForm overlay in analysisOverlays)
                {
                    if (overlay.IsHandleCreated && !overlay.IsDisposed)
                        overlay.BeginInvoke(() => overlay.UpdateLuminance(telemetry));
                }
            };
            session.GamutChanged += telemetry =>
            {
                foreach (AnalysisOverlayForm overlay in analysisOverlays)
                {
                    if (overlay.IsHandleCreated && !overlay.IsDisposed)
                        overlay.BeginInvoke(() => overlay.UpdateGamut(telemetry));
                }
            };
            session.Failed += exception => runtimeFailure = exception;
            session.SetGamutAnalysis(true);
            session.Start();

            Stopwatch clock = Stopwatch.StartNew();
            while (clock.Elapsed < TimeSpan.FromSeconds(seconds) && runtimeFailure is null)
            {
                Application.DoEvents();
                Thread.Sleep(10);
            }

            session.Stop();
            foreach (AnalysisOverlayForm overlay in analysisOverlays)
            {
                overlay.Close();
                overlay.Dispose();
            }
            foreach (StatusOverlayForm overlay in statusOverlays)
            {
                overlay.Close();
                overlay.Dispose();
            }
            foreach (MirrorForm mirrorWindow in mirrorWindows)
            {
                mirrorWindow.Close();
                mirrorWindow.Dispose();
            }
            Application.DoEvents();

            if (runtimeFailure is not null)
                throw new InvalidOperationException("镜像线程在冒烟测试期间失败。", runtimeFailure);

            Console.WriteLine($"SMOKE_TEST_OK: outputs={targets.Count} {lastStatus}");
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            Environment.ExitCode = 1;
        }
    }

    private static int ReadIntArgument(string[] args, string name, int defaultValue)
    {
        for (int i = 0; i + 1 < args.Length; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(args[i + 1], out int value))
                return value;
        }

        return defaultValue;
    }

    private static string ReadStringArgument(string[] args, string name, string defaultValue)
    {
        for (int i = 0; i + 1 < args.Length; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                return args[i + 1].Trim().ToLowerInvariant();
        }

        return defaultValue;
    }
}

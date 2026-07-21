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
        NativeMethods.TryEnablePerMonitorDpiAwareness();

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
            Application.Run(new ControlForm(settings));
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
            List<MirrorOutputBinding> bindings = [];
            foreach (DisplayTarget target in targets)
            {
                MirrorForm mirrorWindow = new(target.Bounds, false, false);
                mirrorWindow.Show();
                mirrorWindows.Add(mirrorWindow);
                bindings.Add(new MirrorOutputBinding(target, mirrorWindow.Handle));
                StatusOverlayForm statusOverlay = new(target.Bounds, capture, target, false, false);
                statusOverlay.Show(mirrorWindow);
                statusOverlays.Add(statusOverlay);
            }
            Application.DoEvents();

            Exception? runtimeFailure = null;
            string lastStatus = "尚未收到状态";
            using MirrorSession session = new(capture, bindings, 203, true);
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
            };
            session.Failed += exception => runtimeFailure = exception;
            session.Start();

            Stopwatch clock = Stopwatch.StartNew();
            while (clock.Elapsed < TimeSpan.FromSeconds(seconds) && runtimeFailure is null)
            {
                Application.DoEvents();
                Thread.Sleep(10);
            }

            session.Stop();
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
}

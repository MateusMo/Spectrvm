using System;
using Avalonia;
using WebViewControl;
using Xilium.CefGlue.Common;

namespace Spectrvm;

class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        CefRuntimeLoader.Initialize();
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
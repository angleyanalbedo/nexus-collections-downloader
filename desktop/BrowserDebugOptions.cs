using Microsoft.Web.WebView2.Core;

namespace Ash.Nexus;

internal static class BrowserDebugOptions
{
    // Separate profiles run separate browser processes and need distinct ports.
    public static CoreWebView2EnvironmentOptions? Create(int portOffset)
    {
        var args = Environment.GetCommandLineArgs();
        var index = Array.IndexOf(args, "--cdp-port");
        if (index < 0) return null;
        if (index + 1 >= args.Length || !int.TryParse(args[index + 1], out var port) || port < 1024 || port > 65534)
            throw new InvalidOperationException("--cdp-port 需要 1024–65534 之间的端口号。");

        return new CoreWebView2EnvironmentOptions
        {
            AdditionalBrowserArguments = $"--remote-debugging-address=127.0.0.1 --remote-debugging-port={port + portOffset}"
        };
    }
}

namespace Ash.Nexus;

internal static class Localization
{
    public static string Language { get; set; } = "zh-CN";
    static readonly Dictionary<string,string> English = new()
    {
        ["工作空间已就绪。浏览 Mod 或粘贴合集链接开始。"] = "Ready. Browse Mods or paste a collection URL.",
        ["正在初始化 WebView2…"] = "Initializing WebView2…",
        ["正在加载 Nexus 页面…"] = "Loading Nexus page…",
        ["正在等待网站自动完成验证…"] = "Waiting for automatic website verification…",
        ["网站验证仍未完成，请点击“查看网页”完成人机验证。"] = "Verification required. Open the browser to continue.",
        ["需要登录 Nexus，请点击“查看网页”完成登录。"] = "Sign in to Nexus through the browser.",
        ["需要登录 Nexus，请点击“查看网页”完成登录后继续。"] = "Sign in to Nexus through the browser to continue.",
        ["正在通过网站获取下载链接…"] = "Resolving the download URL…",
        ["正在等待网站生成下载链接…"] = "Waiting for the website to generate a download URL…",
        ["队列已结束。"] = "Queue finished.",
        ["队列包含已暂停任务。"] = "The queue contains paused tasks.",
        ["当前文件完成后停止队列。"] = "Queue stopped; active transfers will finish.",
        ["所选文件已加入队列。"] = "Selected files added to the queue.",
        ["该文件已在队列中。"] = "This file is already queued.",
        ["已删除下载记录，已下载的文件不会被删除。"] = "History entry removed. Downloaded files were kept.",
        ["下载链接已失效，请点击重试重新获取。"] = "Download URL expired. Retry to request a new one.",
        ["操作未完成，请检查页面后重试。"] = "Operation failed. Check the page and try again.",
        ["传输中断或文件写入失败，可重试以重新获取链接。"] = "Transfer or file write failed. Retry to request a new URL.",
    };
    public static string Text(string value) => Language == "en" && English.TryGetValue(value,out var translated) ? translated : value;
}

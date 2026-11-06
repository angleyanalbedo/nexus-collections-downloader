using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;

namespace Ash.Nexus;
public partial class MainWindow : Window {
    string dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ASH", "Nexus");
    readonly string assets = Path.Combine(AppContext.BaseDirectory, "Assets");
    Store store = null!;
    List<DownloadTask> tasks = [];
    PageSnapshot? snapshot;
    PageSnapshot? browseSnapshot;
    bool downloadPageActive;
    long navigationGeneration;
    DownloadTask? active;
    readonly Dictionary<string,CancellationTokenSource> runningTransfers=[];
    readonly Dictionary<string,TransferTicket> transferTickets=[];
    readonly Dictionary<string,ModernTransfer> runningServices=[];
    DateTime? challengeSince;
    bool ready, busy, queueEnabled, closing, reading;
    string output = "", message = "正在初始化 WebView2…";
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };
    public MainWindow() {InitializeComponent();}
    async void OnLoaded(object sender, RoutedEventArgs e) {
        try {
            store = new Store(dataDir); tasks=store.Load();output=store.Setting("output")??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"Downloads","ASH");
            var uiEnv=await CoreWebView2Environment.CreateAsync(null,Path.Combine(dataDir,"UiProfile"),BrowserDebugOptions.Create(1));
            await Ui.EnsureCoreWebView2Async(uiEnv);
            Ui.CoreWebView2.SetVirtualHostNameToFolderMapping("ash.local",assets,CoreWebView2HostResourceAccessKind.DenyCors);
            Ui.CoreWebView2.Settings.AreDefaultContextMenusEnabled=false;
            Ui.CoreWebView2.NavigationStarting+=(_,a)=>{if(!a.Uri.StartsWith("https://ash.local/",StringComparison.Ordinal))a.Cancel=true;};
            Ui.CoreWebView2.NewWindowRequested+=(_,a)=>a.Handled=true;
            Ui.CoreWebView2.WebMessageReceived+=OnMessage;
            var siteEnv=await CoreWebView2Environment.CreateAsync(null,Path.Combine(dataDir,"NexusProfile"),BrowserDebugOptions.Create(0));
            BrowserPanel.Visibility=Visibility.Visible;BrowserRow.Height=new GridLength(1);
            await Site.EnsureCoreWebView2Async(siteEnv);
            HideBrowserPanel();
            Site.CoreWebView2.Settings.IsWebMessageEnabled=false;
            Site.CoreWebView2.NavigationStarting+=(_,a)=>{
                if(a.Uri.StartsWith("nxm:",StringComparison.OrdinalIgnoreCase)){a.Cancel=true;HandleNxm();return;}
                if(!Data.NexusUrl(a.Uri)&&a.Uri!="about:blank"){a.Cancel=true;SetMessage("外部页面已拦截，请在网站确认目标后用系统浏览器访问。");}
            };
            Site.CoreWebView2.NewWindowRequested+=(_,a)=>{a.Handled=true;if(a.Uri.StartsWith("nxm:",StringComparison.OrdinalIgnoreCase))HandleNxm();else if(Data.NexusUrl(a.Uri))Site.CoreWebView2.Navigate(a.Uri);};
            Site.CoreWebView2.SourceChanged+=(_,_)=>{BrowserAddress.Text=DisplayUrl(Site.Source?.AbsoluteUri??"");};
            Site.CoreWebView2.NavigationCompleted+=async(_,a)=>{if(!a.IsSuccess&&a.WebErrorStatus!=CoreWebView2WebErrorStatus.OperationCanceled){SetMessage("网页加载失败："+a.WebErrorStatus+"；可点击“查看网页”手动检查。");}else if(!busy)await ReadPage(false);};
            Site.CoreWebView2.DownloadStarting+=OnDownload;
            ready=true;message="工作空间已就绪。浏览 Mod 或粘贴合集链接开始。";
            Ui.CoreWebView2.NavigationCompleted+=(_,_)=>Publish();
            Ui.CoreWebView2.Navigate("https://ash.local/index.html");
            timer.Tick+=async(_,_)=>{if(active is not null||runningTransfers.Count>0)Publish();if(!busy&&!reading&&Site.Source?.Host.EndsWith("nexusmods.com")==true)await ReadPage(false);};timer.Start();
        } catch(Exception ex){MessageBox.Show("ASH 启动失败："+ex.Message+"\n请确认已安装 .NET 8 桌面运行时和 Microsoft Edge WebView2。","ASH");}
    }
    void Publish(){if(!ready||closing)return;Ui.CoreWebView2.PostWebMessageAsJson(Data.Encode(new {type="state",tasks,snapshot=browseSnapshot??snapshot,busy,queueEnabled,output,message,initialUrl=InitialUrl(),navigationManagedByHost=false,siteUrl=DisplayUrl(Site.Source?.AbsoluteUri??""),browserVisible=BrowserPanel.Visibility==Visibility.Visible}));}
    string InitialUrl(){
        var args=Environment.GetCommandLineArgs();
        var index=Array.IndexOf(args,"--open");
        if(index>=0&&index+1<args.Length)return args[index+1];
        var remembered=store.Setting("lastBrowseUrl");
        if(!string.IsNullOrWhiteSpace(remembered)){try{return Data.PageUrl(remembered);}catch(InvalidOperationException){}}
        return "https://www.nexusmods.com/games";
    }
    bool RememberBrowseUrl()=>true;
    void SetMessage(string s){message=s;Publish();}
    static string DisplayUrl(string s){if(!Uri.TryCreate(s,UriKind.Absolute,out var u))return "";return u.GetLeftPart(UriPartial.Path);}
    void HideBrowserPanel(){BrowserPanel.Visibility=Visibility.Collapsed;BrowserPanel.Opacity=0;BrowserPanel.IsHitTestVisible=false;BrowserRow.Height=new GridLength(0);}
    void ShowBrowser(){BrowserPanel.Visibility=Visibility.Visible;BrowserPanel.Opacity=1;BrowserPanel.IsHitTestVisible=true;BrowserRow.Height=new GridLength(350);Publish();}
    void HideBrowser(object sender,RoutedEventArgs e){HideBrowserPanel();Publish();}
    void RefreshBrowser(object sender,RoutedEventArgs e)=>Site.CoreWebView2?.Reload();
    async void ReadCurrent(object sender,RoutedEventArgs e)=>await ReadPage(true);
    void HandleNxm(){if(active is not null){active.Status="waiting";active.Error="网站提供的是管理器链接。请在网页选择 Manual / 手动下载；当前版本尚未实现 nxm 兑换。";store.Save(active);}SetMessage("已拦截 nxm 管理器链接，请点击“查看网页”选择手动下载入口。");}
    void SaveLastUrl(){
        if(downloadPageActive||store is null||!Data.NexusUrl(Site.Source?.AbsoluteUri??""))return;
        try{store.Set("lastBrowseUrl",Data.PageUrl(Site.Source!.AbsoluteUri));}catch(InvalidOperationException){}
    }
    void OnClosing(object? sender,CancelEventArgs e){
        if(runningTransfers.Count>0&&MessageBox.Show(this,"文件仍在下载。关闭后下次需要重新获取下载入口。是否关闭？","ASH",MessageBoxButton.YesNo)!=MessageBoxResult.Yes){e.Cancel=true;return;}
        closing=true;timer.Stop();SaveLastUrl();foreach(var t in tasks)store?.Save(t);foreach(var service in runningServices.Values)service.Service.CancelAsync();foreach(var cancel in runningTransfers.Values)cancel.Cancel();transferTickets.Clear();store?.Dispose();Ui.Dispose();Site.Dispose();
    }
}






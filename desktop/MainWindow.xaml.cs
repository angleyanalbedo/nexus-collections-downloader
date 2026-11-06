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
    bool ready, busy, queueEnabled, closing, reading, siteLoading, fixtureNavigation, startupNavigationStarted;
    string output = "", message = "正在初始化 WebView2…";
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };
    readonly List<object> navigationLog=[];
    public MainWindow() {InitializeComponent();}
    async void OnLoaded(object sender, RoutedEventArgs e) {
        try {
            var args=Environment.GetCommandLineArgs();
            var verifyFolder=args.Length>2&&args[1]=="--verify-search"?Path.GetFullPath(args[2]):null;
            void Stage(string value){if(verifyFolder!=null){Directory.CreateDirectory(verifyFolder);File.WriteAllText(Path.Combine(verifyFolder,"stage.txt"),value);}}
            Stage("initializing");
            if(args.Length>2&&args[1]=="--self-check")dataDir=Path.Combine(Path.GetFullPath(args[2]),"TestProfile");
            store = new Store(args.Length>2&&args[1]=="--verify-collection"?Path.Combine(Path.GetFullPath(args[2]),"QueueTest"):dataDir); tasks=store.Load();output=store.Setting("output")??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"Downloads","ASH");
            var uiEnv=await CoreWebView2Environment.CreateAsync(null,Path.Combine(dataDir,"UiProfile"));
            await Ui.EnsureCoreWebView2Async(uiEnv);Stage("ui initialized");
            Ui.CoreWebView2.SetVirtualHostNameToFolderMapping("ash.local",assets,CoreWebView2HostResourceAccessKind.DenyCors);
            Ui.CoreWebView2.Settings.AreDefaultContextMenusEnabled=false;
            Ui.CoreWebView2.NavigationStarting+=(_,a)=>{if(!a.Uri.StartsWith("https://ash.local/",StringComparison.Ordinal))a.Cancel=true;};
            Ui.CoreWebView2.NewWindowRequested+=(_,a)=>a.Handled=true;
            Ui.CoreWebView2.WebMessageReceived+=OnMessage;
            var siteEnv=await CoreWebView2Environment.CreateAsync(null,Path.Combine(dataDir,"NexusProfile"));
            BrowserPanel.Visibility=Visibility.Visible;BrowserRow.Height=new GridLength(1);
            await Site.EnsureCoreWebView2Async(siteEnv);Stage("site initialized");
            HideBrowserPanel();
            Site.CoreWebView2.Settings.IsWebMessageEnabled=false;
            Site.CoreWebView2.NavigationStarting+=(_,a)=>{
                navigationLog.Add(new{stage="starting",url=DisplayUrl(a.Uri),scheme=Uri.TryCreate(a.Uri,UriKind.Absolute,out var loggedUri)?loggedUri.Scheme:"",localBlank=a.Uri=="about:blank"});
                if(a.Uri.StartsWith("nxm:",StringComparison.OrdinalIgnoreCase)){a.Cancel=true;HandleNxm();return;}
                if(!Data.NexusUrl(a.Uri)&&a.Uri!="about:blank"&&!(fixtureNavigation&&a.Uri.StartsWith("data:text/html",StringComparison.Ordinal))){a.Cancel=true;SetMessage("外部页面已拦截，请在网站确认目标后用系统浏览器访问。");}
                else siteLoading=true;
            };
            Site.CoreWebView2.NewWindowRequested+=(_,a)=>{a.Handled=true;if(a.Uri.StartsWith("nxm:",StringComparison.OrdinalIgnoreCase))HandleNxm();else if(Data.NexusUrl(a.Uri))Site.CoreWebView2.Navigate(a.Uri);};
            Site.CoreWebView2.SourceChanged+=(_,_)=>{BrowserAddress.Text=DisplayUrl(Site.Source?.AbsoluteUri??"");};
            Site.CoreWebView2.NavigationCompleted+=async(_,a)=>{navigationLog.Add(new{stage="completed",success=a.IsSuccess,error=a.WebErrorStatus.ToString()});siteLoading=false;if(!a.IsSuccess&&a.WebErrorStatus!=CoreWebView2WebErrorStatus.OperationCanceled){SetMessage("网页加载失败："+a.WebErrorStatus+"；可点击“查看网页”手动检查。");}else if(!busy)await ReadPage(false);};
            Site.CoreWebView2.DownloadStarting+=OnDownload;
            ready=true;message="工作空间已就绪。浏览 Mod 或粘贴合集链接开始。";
            Ui.CoreWebView2.NavigationCompleted+=(_,_)=>Publish();
            Ui.CoreWebView2.Navigate("https://ash.local/index.html");
            timer.Tick+=async(_,_)=>{if(active is not null||runningTransfers.Count>0)Publish();if(!busy&&!reading&&Site.Source?.Host.EndsWith("nexusmods.com")==true)await ReadPage(false);};timer.Start();
            if(args.Length>2&&(args[1]=="--diagnose"||args[1]=="--self-check"))await Diagnose(args[2],args[1]=="--self-check");
            if(args.Length>2&&args[1]=="--ui-audit"){await Task.Delay(1800);Directory.CreateDirectory(args[2]);var audit=await Ui.CoreWebView2.ExecuteScriptAsync("JSON.stringify({title:document.title,buttons:[...document.querySelectorAll(\"button\")].map(b=>b.textContent),overflow:document.documentElement.scrollWidth>innerWidth,errors:document.querySelectorAll(\"[id]\").length})");File.WriteAllText(Path.Combine(args[2],"ui-audit.json"),JsonSerializer.Deserialize<string>(audit));}
            
            if(args.Length>3&&args[1]=="--inspect-page")await InspectPage(args[2],args[3]);
            if(args.Length>2&&args[1]=="--verify-collection")await VerifyCollection(Path.GetFullPath(args[2]));
            if(args.Length>2&&args[1]=="--verify-startup"){
                for(var i=0;i<60&&snapshot?.Items.Count is not >0;i++)await Task.Delay(500);
                Directory.CreateDirectory(args[2]);
                File.WriteAllText(Path.Combine(args[2],"startup.json"),Data.Encode(new{startupNavigationStarted,snapshot,busy,message}));
            }
            if(verifyFolder!=null){
                Stage("navigating games");await InspectPage("https://www.nexusmods.com/games",verifyFolder);
                if(snapshot?.Status=="ready"){
                    Stage("searching blade");await SearchSite("games","","blade");await InspectPage(Site.Source!.AbsoluteUri,Path.Combine(verifyFolder,"blade"),false);
                    Stage("clearing search");await SearchSite("games","","");await InspectPage(Site.Source!.AbsoluteUri,Path.Combine(verifyFolder,"cleared"),false);
                }
                Stage("complete");
            }
        } catch(Exception ex){MessageBox.Show("ASH 启动失败："+ex.Message+"\n请确认已安装 .NET 8 桌面运行时和 Microsoft Edge WebView2。","ASH");}
    }
    void Publish(){if(!ready||closing)return;Ui.CoreWebView2.PostWebMessageAsJson(Data.Encode(new {type="state",tasks,snapshot=browseSnapshot??snapshot,busy,queueEnabled,output,message,initialUrl=InitialUrl(),navigationManagedByHost=HostManagesNavigation(),siteUrl=DisplayUrl(Site.Source?.AbsoluteUri??""),browserVisible=BrowserPanel.Visibility==Visibility.Visible}));}
    string InitialUrl(){
        var args=Environment.GetCommandLineArgs();
        if(args.Length>2&&args[1]=="--open")return args[2];
        if(args.Length==1){
            var remembered=store.Setting("lastBrowseUrl");
            if(!string.IsNullOrWhiteSpace(remembered)){try{return Data.PageUrl(remembered);}catch(InvalidOperationException){}}
        }
        return "https://www.nexusmods.com/games";
    }
    bool RememberBrowseUrl(){var args=Environment.GetCommandLineArgs();return args.Length==1||(args.Length>2&&args[1]=="--open");}
    bool HostManagesNavigation(){var args=Environment.GetCommandLineArgs();return args.Length>1&&args[1]!="--open"&&args[1]!="--verify-startup";}
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
    async Task Navigate(string url){
        url=Data.PageUrl(url);var generation=++navigationGeneration;challengeSince=null;snapshot=null;message="正在加载 Nexus 页面…";busy=true;Publish();
        try{
            Site.CoreWebView2.Navigate(url);
            for(var i=0;i<30&&generation==navigationGeneration;i++){
                await Task.Delay(600);
                if(generation!=navigationGeneration)return;
                if(await ReadPage(false)||snapshot?.Status is "challenge" or "login")break;
            }
            if(generation==navigationGeneration&&(snapshot is null||snapshot.Status=="loading"))message="网页仍未就绪，加载完成后将自动读取。";
        }finally{if(generation==navigationGeneration){busy=false;Publish();}}
    }
    async Task<bool> ReadPage(bool notify){
        if(!ready||reading||!Data.NexusUrl(Site.Source?.AbsoluteUri??""))return false;
        reading=true;
        try{
            var json=await Site.CoreWebView2.ExecuteScriptAsync(File.ReadAllText(Path.Combine(assets,"nexus.js")));
            var text=JsonSerializer.Deserialize<string>(json);if(text is null)return false;
            var page=JsonSerializer.Deserialize<PageSnapshot>(text,Data.Json);if(page is null)return false;
            if(!Uri.TryCreate(page.Url,UriKind.Absolute,out var documentUri)||documentUri.GetLeftPart(UriPartial.Query)!=Site.Source?.GetLeftPart(UriPartial.Query))return false;
            page.Items=page.Items.Where(m=>Data.NexusUrl(m.Url)).ToList();
            if(page.Status is "challenge" or "login"){
                snapshot=page;
                if(page.Status=="challenge"){
                    challengeSince??=DateTime.UtcNow;
                    if((DateTime.UtcNow-challengeSince.Value).TotalSeconds>=4){message="网站验证仍未完成，请点击“查看网页”完成人机验证。";}
                    else message="正在等待网站自动完成验证…";
                }else{
                    message="需要登录 Nexus，请点击“查看网页”完成登录。";
                }
                Publish();return false;
            }
            if(page.Status=="loading"){snapshot=page;if(!downloadPageActive)browseSnapshot=page;Publish();return false;}
            if(page.Status!="ready")return false;
            challengeSince=null;
            snapshot=page;
            if(!downloadPageActive){browseSnapshot=page;if(RememberBrowseUrl())store.Set("lastBrowseUrl",page.Url);}
            if(!notify)message="";
            if(notify)message=page.Items.Count>0?$"已从当前页面读取 {page.Items.Count} 条内容。":"页面尚未识别到文件或 Mod，展开原网页检查后重新读取。";
            Publish();return page.Items.Count>0||!string.IsNullOrWhiteSpace(page.Description);
        }catch(Exception){if(notify)SetMessage("页面解析失败，请刷新原网页后重试。");return false;}finally{reading=false;}
    }
    async void OnMessage(object? sender,CoreWebView2WebMessageReceivedEventArgs e){
        if(!ready||!Uri.TryCreate(e.Source,UriKind.Absolute,out var source)||source.Scheme!="https"||source.Host!="ash.local"||source.AbsolutePath!="/index.html"||source.Query.Length>0)return;
        try{
            using var doc=JsonDocument.Parse(e.WebMessageAsJson);var p=doc.RootElement;var action=p.GetProperty("action").GetString();
            string Val(string name)=>p.TryGetProperty(name,out var v)?v.GetString()??"":"";
            switch(action){
                case "ready":Publish();break;
                case "browse":
                    if(active?.Status=="waiting")throw new InvalidOperationException("正在获取当前文件的下载链接，请稍候。");
                    startupNavigationStarted=true;downloadPageActive=false;await Navigate(Val("url"));break;
                case "login":if(active!=null)throw new InvalidOperationException("先停止队列再登录。");ShowBrowser();await Navigate("https://www.nexusmods.com/users/myaccount");break;
                case "browser":ShowBrowser();if(Site.Source is null)await Navigate("https://www.nexusmods.com/phantombladezero/mods/");break;
                case "read":await ReadPage(true);break;
                case "search":
                    if(active?.Status=="waiting"||busy)throw new InvalidOperationException("网页正在加载或获取下载链接，请稍候。");
                    downloadPageActive=false;
                    await SearchSite(Val("scope"),Val("game"),Val("query"));break;
                case "enqueueCollection":
                    if(busy||active?.Status=="waiting")throw new InvalidOperationException("请先等待网页下载链接生成，再提交合集清单。");
                    var selectedFiles=Data.CollectionSelection(browseSnapshot??snapshot,Val("key"),p.GetProperty("ids").EnumerateArray().Select(x=>x.GetString()??""));
                    var added=selectedFiles.Where(f=>!tasks.Any(t=>t.Status!="cancelled"&&t.File.FileId==f.FileId&&new Uri(t.File.Url).GetLeftPart(UriPartial.Path)==new Uri(f.Url).GetLeftPart(UriPartial.Path))).Select(f=>new DownloadTask{File=f}).ToList();
                    store.SaveMany(added);tasks.AddRange(added);SetMessage($"已将所选的 {added.Count} 个合集文件加入下载队列。");
                    if(p.TryGetProperty("start",out var shouldStart)&&shouldStart.ValueKind==JsonValueKind.True){queueEnabled=true;await Next();}
                    break;
                case "enqueue":
                    var selectionPage=browseSnapshot??snapshot;if(selectionPage is null)break;
                    var enqueued=0;
                    foreach(var id in p.GetProperty("ids").EnumerateArray().Select(x=>x.GetString())){
                        var f=selectionPage.Items.FirstOrDefault(x=>x.Id==id&&x.Kind=="file");if(f is null)continue;
                        if(tasks.Any(t=>t.File.Id==f.Id&&t.Status!="cancelled"))continue;
                        if(f.DownloadUrl.Length>0)Data.PageUrl(f.DownloadUrl);
                        var t=new DownloadTask{File=f};tasks.Add(t);store.Save(t);enqueued++;
                    }SetMessage(enqueued>0?"所选文件已加入队列。":"该文件已在队列中。");
                    if(p.TryGetProperty("start",out var startSingle)&&startSingle.ValueKind==JsonValueKind.True){queueEnabled=true;await Next();}
                    break;
                case "start":queueEnabled=true;await Next();break;
                case "stop":queueEnabled=false;SetMessage("当前文件完成后停止队列。");break;
                case "pause":
                    var pauseTask=tasks.FirstOrDefault(t=>t.Id==Val("id"))??active;
                    if(pauseTask!=null&&runningServices.TryGetValue(pauseTask.Id,out var pauseService)){pauseTask.Status="paused";pauseTask.CanResume=true;store.Save(pauseTask);pauseService.Service.Pause();}
                    Publish();break;
                case "resume":
                    var resumeTask=tasks.FirstOrDefault(t=>t.Id==Val("id"))??active;
                    if(resumeTask!=null&&runningServices.TryGetValue(resumeTask.Id,out var resumeService)){resumeTask.Status="downloading";resumeTask.CanResume=false;store.Save(resumeTask);resumeService.Service.Resume();Publish();}
                    else if(resumeTask!=null&&resumeTask.Status=="paused"&&transferTickets.TryGetValue(resumeTask.Id,out var resumeTicket))_=RunTransfer(resumeTask,resumeTicket);
                    else SetMessage("下载链接已失效，请点击重试重新获取。");
                    break;
                case "cancel":
                    var cancelTask=tasks.FirstOrDefault(t=>t.Id==Val("id"))??active;
                    if(cancelTask!=null){
                        cancelTask.Status="cancelled";cancelTask.Error="";cancelTask.CanResume=false;store.Save(cancelTask);
                        if(runningServices.TryGetValue(cancelTask.Id,out var cancelService))cancelService.Service.CancelAsync();
                        if(runningTransfers.TryGetValue(cancelTask.Id,out var cancelSource))cancelSource.Cancel();
                        transferTickets.Remove(cancelTask.Id);
                        if(ReferenceEquals(active,cancelTask)){
                            active=null;downloadPageActive=false;
                            if(queueEnabled)ScheduleNext();
                        }
                    }
                    Publish();break;
                case "cancelAll":
                    queueEnabled=false;
                    foreach(var task in tasks.Where(t=>t.Status is "queued" or "waiting" or "downloading" or "paused")){
                        task.Status="cancelled";task.Error="";task.CanResume=false;store.Save(task);
                        if(runningServices.TryGetValue(task.Id,out var service))service.Service.CancelAsync();
                        if(runningTransfers.TryGetValue(task.Id,out var taskCancel))taskCancel.Cancel();
                        transferTickets.Remove(task.Id);
                    }
                    active=null;Publish();break;
                case "deleteTask":
                    var deleteTask=tasks.FirstOrDefault(t=>t.Id==Val("id"));
                    if(deleteTask is null)break;
                    if(deleteTask.Status is not ("completed" or "cancelled" or "failed"))
                        throw new InvalidOperationException("只能删除已结束的下载记录。");
                    runningTransfers.Remove(deleteTask.Id);
                    runningServices.Remove(deleteTask.Id);
                    transferTickets.Remove(deleteTask.Id);
                    tasks.Remove(deleteTask);
                    store.Delete(deleteTask.Id);
                    SetMessage("已删除下载记录，已下载的文件不会被删除。");
                    break;
                case "clearFinished":
                    if(active is not null&&active.Status is ("queued" or "waiting" or "downloading" or "paused"))
                        throw new InvalidOperationException("请先取消正在进行的任务。");
                    var finished=tasks.Where(t=>t.Status is "completed" or "cancelled" or "failed").ToList();
                    foreach(var task in finished){
                        runningTransfers.Remove(task.Id);
                        runningServices.Remove(task.Id);
                        transferTickets.Remove(task.Id);
                        tasks.Remove(task);
                    }
                    store.DeleteFinished();
                    SetMessage(finished.Count>0?$"已清空 {finished.Count} 条下载记录，已下载的文件不会被删除。":"没有可清空的下载记录。");
                    break;
                case "retry":
                    var retry=tasks.FirstOrDefault(t=>t.Id==Val("id"));
                    if(retry!=null&&retry.Status is "failed" or "paused" or "cancelled"){
                        retry.Status="queued";retry.Error="";retry.CanResume=false;store.Save(retry);
                        queueEnabled=true;ScheduleNext();
                    }
                    Publish();break;
                case "folder":var dialog=new Microsoft.Win32.OpenFolderDialog{Title="选择下载目录"};if(dialog.ShowDialog(this)==true){output=dialog.FolderName;store.Set("output",output);Publish();}break;
                case "openFolder":Directory.CreateDirectory(output);Process.Start(new ProcessStartInfo("explorer.exe",output){UseShellExecute=true});break;
            }
        }catch(Exception ex){SetMessage(ex is InvalidOperationException?ex.Message:"操作未完成，请检查页面后重试。");}
    }
    void ScheduleNext()=>Dispatcher.BeginInvoke(new Action(async()=>{try{await Next();}catch{queueEnabled=false;SetMessage("下一项下载未能打开，请重试队列。");}}));
    void FailDownloadLookup(DownloadTask task,string error,string message,bool continueQueue){
        task.Status="failed";task.Error=error;task.CanResume=false;store.Save(task);
        if(ReferenceEquals(active,task))active=null;
        downloadPageActive=false;
        if(!continueQueue)queueEnabled=false;
        SetMessage(message);
        if(continueQueue&&queueEnabled)ScheduleNext();
    }
    async Task Next(){
        if(!queueEnabled||active!=null||busy)return;
        active=tasks.FirstOrDefault(t=>t.Status=="queued");if(active==null){queueEnabled=false;SetMessage("队列已完成。");return;}
        var task=active;downloadPageActive=true;
        task.Status="waiting";task.Error="正在通过网站获取下载链接…";store.Save(task);
        var target=new UriBuilder(Data.PageUrl(task.File.Url));
        target.Query="tab=files&file_id="+Uri.EscapeDataString(task.File.FileId)+"&nmm=0";
        await Navigate(target.Uri.AbsoluteUri);
        var deadline=DateTime.UtcNow.AddSeconds(75);
        while(ReferenceEquals(active,task)&&task.Status=="waiting"&&DateTime.UtcNow<deadline){
            var lookup=await InvokeDownloadAdapter(task.File.FileId,target.Path);
            if(lookup is null){await Task.Delay(500);continue;}
            var result=lookup.Value.GetRawText();
            string? step=null,url=null,error=null;
            using(var doc=JsonDocument.Parse(result)){
                if(doc.RootElement.TryGetProperty("step",out var stepProp))step=stepProp.GetString();
                if(doc.RootElement.TryGetProperty("url",out var urlProp))url=urlProp.GetString();
                if(doc.RootElement.TryGetProperty("error",out var errorProp))error=errorProp.GetString();
            }
            if(step=="challenge"||snapshot?.Status is "challenge" or "login"){
                if(snapshot?.Status=="login"){SetMessage("需要登录 Nexus，请点击“查看网页”完成登录后继续。");}
                else{
                    challengeSince??=DateTime.UtcNow;
                    if((DateTime.UtcNow-challengeSince.Value).TotalSeconds>=4){SetMessage("网站验证仍未完成，请点击“查看网页”完成人机验证。");}
                    else SetMessage("正在等待网站自动完成验证…");
                }
            }
            else if(step is "clicked-slow" or "clicked-manual" or "countdown"){task.Error="正在等待网站生成下载链接…";Publish();}
            else if(step is "invalid-file" or "blocked" or "failed"){
                var detail=string.IsNullOrWhiteSpace(error)?"未能获取下载链接：文件可能已归档、移除或当前账户无权下载。":error;
                FailDownloadLookup(task,detail,detail,true);
                return;
            }
            else if(step=="found-url"&&!string.IsNullOrWhiteSpace(url)){
                if(!Uri.TryCreate(url,UriKind.Absolute,out var link)||link.Scheme!="https"||!string.IsNullOrEmpty(link.UserInfo)){
                    FailDownloadLookup(task,"下载服务器返回了无效链接，请重试。","下载服务器返回了无效链接，队列已停止。",false);
                    return;
                }
                active=null;
                downloadPageActive=false;
                _=StartTransferFromUrl(task,link);
                ScheduleNext();
                return;
            }
            await Task.Delay(1000);
        }
        if(ReferenceEquals(active,task)&&task.Status=="waiting"){
            FailDownloadLookup(task,"75 秒内未取得 Nexus 下载链接，请检查登录、验证或文件版本。","获取下载链接超时，队列已停止；可查看网页步骤后重新尝试。",false);
        }
    }

    async Task<JsonElement?> InvokeDownloadAdapter(string fileId,string pagePath){
        var script=File.ReadAllText(Path.Combine(assets,"nexus-download.js"));
        var invocationId=Guid.NewGuid().ToString("N");
        var args=Data.Encode(new{fileId,pagePath});
        var starter=
            "(function(){"+
            "const key="+JsonSerializer.Serialize(invocationId)+";"+
            "window.__ASH_DOWNLOAD_LOOKUP__={id:key,value:{step:'pending'}};"+
            "Promise.resolve(("+script+")("+args+")).then(function(raw){"+
            "if(!window.__ASH_DOWNLOAD_LOOKUP__||window.__ASH_DOWNLOAD_LOOKUP__.id!==key)return;"+
            "try{window.__ASH_DOWNLOAD_LOOKUP__.value=typeof raw==='string'?JSON.parse(raw):raw||{step:'failed',error:'下载适配器没有返回结果。'};}"+
            "catch(e){window.__ASH_DOWNLOAD_LOOKUP__.value={step:'failed',error:String(e)};}"+
            "}).catch(function(e){if(window.__ASH_DOWNLOAD_LOOKUP__&&window.__ASH_DOWNLOAD_LOOKUP__.id===key)window.__ASH_DOWNLOAD_LOOKUP__.value={step:'failed',error:String(e)};});"+
            "return true;})()";
        await Site.CoreWebView2.ExecuteScriptAsync(starter).WaitAsync(TimeSpan.FromSeconds(5));
        var deadline=DateTime.UtcNow.AddSeconds(30);
        while(DateTime.UtcNow<deadline){
            var raw=await Site.CoreWebView2.ExecuteScriptAsync(
                "window.__ASH_DOWNLOAD_LOOKUP__&&window.__ASH_DOWNLOAD_LOOKUP__.id==="+
                JsonSerializer.Serialize(invocationId)+"?window.__ASH_DOWNLOAD_LOOKUP__.value:null")
                .WaitAsync(TimeSpan.FromSeconds(5));
            using var document=JsonDocument.Parse(string.IsNullOrWhiteSpace(raw)?"null":raw);
            var root=document.RootElement;
            if(root.ValueKind==JsonValueKind.Object&&root.TryGetProperty("step",out var step)&&
               step.GetString()!="pending")return root.Clone();
            await Task.Delay(200);
        }
        return null;
    }

    async Task StartTransferFromUrl(DownloadTask task,Uri url){
        try{
            var folder=task.File.CollectionName.Length>0?task.File.CollectionName[..Math.Min(60,task.File.CollectionName.Length)]+"_r"+task.File.Revision:task.Id;
            var remoteName=Path.GetFileName(url.LocalPath);
            var baseName=string.IsNullOrWhiteSpace(task.File.Name)?task.File.ModName:task.File.Name;
            var fallback=string.IsNullOrWhiteSpace(baseName)?"download":baseName;
            if(!Path.HasExtension(fallback))fallback+=".zip";
            var candidate=string.IsNullOrWhiteSpace(remoteName)||!Path.HasExtension(remoteName)?fallback:remoteName;
            task.Path=Data.SafePath(output,folder,candidate);
            var cookies=new System.Net.CookieContainer();
            foreach(var cookie in await Site.CoreWebView2.CookieManager.GetCookiesAsync(url.AbsoluteUri)){
                try{cookies.Add(new System.Net.Cookie(cookie.Name,cookie.Value,cookie.Path,cookie.Domain){Secure=cookie.IsSecure,HttpOnly=cookie.IsHttpOnly});}catch(System.Net.CookieException){}
            }
            var ticket=new TransferTicket{Url=url,Cookies=cookies,UserAgent=Site.CoreWebView2.Settings.UserAgent,Referer=Uri.TryCreate(DisplayUrl(Site.Source?.AbsoluteUri??""),UriKind.Absolute,out var referer)?referer:null};
            await RunTransfer(task,ticket);
        }catch(Exception ex){task.Status="failed";task.Error=ex is InvalidOperationException?ex.Message:"无法接管下载链接，请检查保存目录后重试。";store.Save(task);if(ReferenceEquals(active,task))active=null;queueEnabled=false;Publish();}
    }
    async void OnDownload(object? sender,CoreWebView2DownloadStartingEventArgs e){
        using var deferral=e.GetDeferral();
        e.Cancel=true;e.Handled=true; // WebView2 obtains the link; C# transfers the bytes.
        var task=active;
        if(task is null||task.Status!="waiting"||transferTickets.ContainsKey(task.Id)||closing)return;
        if(!Uri.TryCreate(e.DownloadOperation.Uri,UriKind.Absolute,out var url)||url.Scheme!="https"||!string.IsNullOrEmpty(url.UserInfo))return;
        try{
            var folder=task.File.CollectionName.Length>0?task.File.CollectionName[..Math.Min(60,task.File.CollectionName.Length)]+"_r"+task.File.Revision:task.Id;
            task.Path=Data.SafePath(output,folder,Path.GetFileName(e.ResultFilePath));
            var cookies=new System.Net.CookieContainer();
            foreach(var cookie in await Site.CoreWebView2.CookieManager.GetCookiesAsync(url.AbsoluteUri)){
                try{cookies.Add(new System.Net.Cookie(cookie.Name,cookie.Value,cookie.Path,cookie.Domain){Secure=cookie.IsSecure,HttpOnly=cookie.IsHttpOnly});}catch(System.Net.CookieException){}
            }
            var ticket=new TransferTicket{Url=url,Cookies=cookies,UserAgent=Site.CoreWebView2.Settings.UserAgent,Referer=Uri.TryCreate(DisplayUrl(Site.Source?.AbsoluteUri??""),UriKind.Absolute,out var referer)?referer:null};
            _=Dispatcher.BeginInvoke(new Action(()=>{if(!closing&&ReferenceEquals(active,task)&&task.Status=="waiting"){active=null;_=RunTransfer(task,ticket);ScheduleNext();}}));
        }catch{task.Status="failed";task.Error="无法接管下载链接，请检查保存目录后重试。";store.Save(task);active=null;queueEnabled=false;Publish();}
    }
    async Task RunTransfer(DownloadTask task,TransferTicket ticket){
        var transferCancel=new CancellationTokenSource();
        runningTransfers[task.Id]=transferCancel;transferTickets[task.Id]=ticket;
        var transfer=new ModernTransfer(ticket);runningServices[task.Id]=transfer;
        task.Status="downloading";task.CanResume=false;task.Error="";store.Save(task);HideBrowserPanel();Publish();
        try{
            var progress=new Progress<(long Received,long Total)>(p=>{if(!closing){task.Received=p.Received;task.Total=p.Total;}});
            await transfer.Run(ticket.Url.AbsoluteUri,task.Path,task.File.Size,progress,transferCancel.Token);
            task.Received=new FileInfo(task.Path).Length;task.Total=task.Received;task.Status="completed";
        }catch(OperationCanceledException){if(task.Status!="cancelled"){task.Status="paused";task.CanResume=true;}}
        catch(InvalidOperationException ex){task.Status="failed";task.Error=ex.Message;queueEnabled=false;}
        catch{task.Status="failed";task.Error="传输中断或文件写入失败，可重试以重新获取链接。";queueEnabled=false;}
        finally{
            runningTransfers.Remove(task.Id);runningServices.Remove(task.Id);transferCancel.Dispose();
            if(!closing){store.Save(task);if(task.Status!="paused")transferTickets.Remove(task.Id);Publish();if(task.Status=="completed"&&queueEnabled)ScheduleNext();}
        }
    }
    async Task Diagnose(string folder,bool offline){
        try{
            Directory.CreateDirectory(folder);await Task.Delay(2000);Publish();await Task.Delay(300);
            using(var image=File.Create(Path.Combine(folder,"ash-initial.png")))await Ui.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,image);
            var uiReport=await Ui.CoreWebView2.ExecuteScriptAsync("JSON.stringify({title:document.title,bridge:!!window.chrome?.webview,buttons:document.querySelectorAll('button').length,overflow:document.documentElement.scrollWidth>innerWidth,message:document.getElementById('message')?.textContent})");
            File.WriteAllText(Path.Combine(folder,"ui-report.json"),JsonSerializer.Deserialize<string>(uiReport));
            var interactionReport=await Ui.CoreWebView2.ExecuteScriptAsync("(()=>{document.querySelector('[data-page=downloads]').click();const queueVisible=!document.getElementById('downloadsView').hidden;document.querySelector('[data-page=collection]').click();const collectionVisible=!document.getElementById('collectionView').hidden;document.querySelector('[data-action=import]').click();const importVisible=!document.getElementById('importBox').hidden;document.querySelector('[data-action=closeImport]').click();document.querySelector('[data-page=discover]').click();return JSON.stringify({queueVisible,collectionVisible,importVisible})})()");
            File.WriteAllText(Path.Combine(folder,"interaction-report.json"),JsonSerializer.Deserialize<string>(interactionReport));
            ShowBrowser();await Task.Delay(500);
            await LoadFixture("<html><body><h1>Fixture</h1><article><h3>Fixture Mod</h3><a href='https://www.nexusmods.com/phantombladezero/mods/123'>Fixture Mod</a></article><article><a href='https://evil.test/phantombladezero/mods/3'>Untrusted</a></article></body></html>");
            var source=File.ReadAllText(Path.Combine(assets,"nexus.js"));
            var fixture="(function(location){return "+source+"})({href:'https://www.nexusmods.com/phantombladezero/mods/',host:'www.nexusmods.com',pathname:'/phantombladezero/mods/'})";
            var fixtureResult=await Site.CoreWebView2.ExecuteScriptAsync(fixture);
            File.WriteAllText(Path.Combine(folder,"adapter-mod-fixture.json"),JsonSerializer.Deserialize<string>(fixtureResult));
            await LoadFixture("<html><body><h1>Fixture Files</h1><dl data-id='99'><dt class='name'>Main Archive</dt><dd><a href='https://www.nexusmods.com/phantombladezero/mods/123?tab=files&amp;file_id=99'>Manual download</a></dd></dl></body></html>");
            fixture="(function(location){return "+source+"})({href:'https://www.nexusmods.com/phantombladezero/mods/123?tab=files',host:'www.nexusmods.com',pathname:'/phantombladezero/mods/123'})";
            fixtureResult=await Site.CoreWebView2.ExecuteScriptAsync(fixture);
            File.WriteAllText(Path.Combine(folder,"adapter-file-fixture.json"),JsonSerializer.Deserialize<string>(fixtureResult));
            if(offline){Close();return;}
            await Navigate("https://www.nexusmods.com/phantombladezero/mods/");await Task.Delay(3000);await ReadPage(true);
            var dom=await Site.CoreWebView2.ExecuteScriptAsync("JSON.stringify({url:location.origin+location.pathname,title:document.title,ready:document.readyState,text:document.body?.innerText.slice(0,1500),links:document.querySelectorAll('a[href]').length})");
            File.WriteAllText(Path.Combine(folder,"site-report.json"),Data.Encode(new{snapshot,message,siteLoading,navigationLog,dom=JsonSerializer.Deserialize<string>(dom)}));
            var controls=await Site.CoreWebView2.ExecuteScriptAsync("JSON.stringify({inputs:[...document.querySelectorAll('input')].map(e=>({type:e.type,name:e.name,placeholder:e.placeholder,label:e.getAttribute('aria-label'),form:!!e.form})),forms:[...document.forms].map(e=>({action:e.getAttribute('action'),role:e.getAttribute('role')}))})");
            File.WriteAllText(Path.Combine(folder,"search-controls.json"),JsonSerializer.Deserialize<string>(controls));
            var firstMod=snapshot?.Items.FirstOrDefault(x=>x.Kind=="mod"&&!x.Name.Contains("Topless",StringComparison.OrdinalIgnoreCase));
            if(firstMod!=null){
                await Navigate(firstMod.Url.Split('?')[0]+"?tab=files");await Task.Delay(1800);await ReadPage(true);
                File.WriteAllText(Path.Combine(folder,"real-files-report.json"),Data.Encode(new{snapshot,message}));
                var elements=await Site.CoreWebView2.ExecuteScriptAsync("JSON.stringify({title:document.title,ready:document.readyState,body:document.body?.innerText.slice(-7000),links:[...document.querySelectorAll('a[href]')].filter(a=>/download|file_id|DownloadPopUp|manual/i.test(a.href+' '+a.innerText)).map(a=>({text:a.innerText,url:a.href.replace(/([?&](?:key|expires)=)[^&]*/gi,'$1REDACTED')})),buttons:[...document.querySelectorAll('button')].map(b=>({text:b.innerText,label:b.getAttribute('aria-label')})).filter(b=>/download|file|manual/i.test(b.text+' '+b.label)),files:[...document.querySelectorAll('[data-id],.file-expander-header')].slice(0,12).map(e=>({id:e.dataset.id,text:e.innerText.slice(0,350)}))})");
                File.WriteAllText(Path.Combine(folder,"real-file-controls.json"),JsonSerializer.Deserialize<string>(elements));
            }
            using(var image=File.Create(Path.Combine(folder,"nexus-page.png")))await Site.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,image);
        }catch(Exception ex){File.WriteAllText(Path.Combine(folder,"diagnostic-error.txt"),ex+"\n"+Data.Encode(navigationLog));if(offline)Close();}
    }
    async Task LoadFixture(string html){
        var done=new TaskCompletionSource<bool>();
        void Completed(object? s,CoreWebView2NavigationCompletedEventArgs e)=>done.TrySetResult(e.IsSuccess);
        Site.CoreWebView2.NavigationCompleted+=Completed;
        try{fixtureNavigation=true;Site.CoreWebView2.NavigateToString(html);if(!await done.Task.WaitAsync(TimeSpan.FromSeconds(10)))throw new InvalidOperationException("Fixture navigation failed");}
        finally{fixtureNavigation=false;Site.CoreWebView2.NavigationCompleted-=Completed;}
    }
    async Task SearchSite(string scope,string game,string query){
        if(scope!="games"&&scope!="collections"&&scope!="mods")throw new InvalidOperationException("无效搜索类型。");
        if(scope!="games"&&!System.Text.RegularExpressions.Regex.IsMatch(game,"^[a-z0-9]+$"))throw new InvalidOperationException("无效游戏域名。");
        var url=scope=="games"?"https://www.nexusmods.com/games":$"https://www.nexusmods.com/games/{game}/{scope}";
        if(snapshot?.Kind!=scope || (scope!="games" && !snapshot.Url.Contains("/"+game+"/")))await Navigate(url);
        if(snapshot?.Status is "challenge" or "login")return;
        busy=true;message="正在搜索 Nexus…";Publish();
        try{
            for(var i=0;i<40;i++){
                if(await Site.CoreWebView2.ExecuteScriptAsync("document.readyState==='complete'")=="true")break;
                await Task.Delay(250);
            }
            await Task.Delay(350);
            var script="("+File.ReadAllText(Path.Combine(assets,"nexus-search.js"))+")("+Data.Encode(new{scope,query})+")";
            if(await Site.CoreWebView2.ExecuteScriptAsync(script)!="true"){
                SetMessage("无法识别此页面的搜索框，请点击“查看网页”后搜索；结果会自动同步。");return;
            }
            // Observe the live page after React debounce / navigation; never reload and discard its search state.
            for(var i=0;i<16;i++){
                await Task.Delay(500);
                if(i>=2)await ReadPage(false);
                if(snapshot?.Status is "challenge" or "login")break;
            }
        }finally{busy=false;Publish();}
    }
    async Task VerifyCollection(string folder){
        Directory.CreateDirectory(folder);
        await Navigate("https://www.nexusmods.com/games/phantombladezero/collections/1ebi4o/mods");
        for(var i=0;i<60&&snapshot?.Collection?.Complete!=true;i++){await Task.Delay(500);await ReadPage(false);}
        File.WriteAllText(Path.Combine(folder,"manifest.json"),Data.Encode(snapshot));
        if(snapshot?.Collection?.Complete!=true)return;
        await Task.Delay(1000);
        await Ui.CoreWebView2.ExecuteScriptAsync("document.querySelector('[data-mod-group]').click()");
        await Task.Delay(150);
        await Ui.CoreWebView2.ExecuteScriptAsync("document.querySelector('[data-collection-file]:checked').click()");
        await Task.Delay(150);
        var selection=await Ui.CoreWebView2.ExecuteScriptAsync("JSON.stringify({route:location.hash,checked:[...document.querySelectorAll('[data-collection-file]:checked')].map(e=>e.dataset.collectionFile),summary:document.querySelector('.collection-summary')?.innerText})");
        var selected=JsonSerializer.Deserialize<string>(selection);
        await Ui.CoreWebView2.ExecuteScriptAsync("document.querySelector('[data-collection-enqueue]').click()");
        await Task.Delay(1000);
        File.WriteAllText(Path.Combine(folder,"selection-test.json"),Data.Encode(new{selection=selected,tasks}));
        await Ui.CoreWebView2.ExecuteScriptAsync("document.querySelector('[aria-label=\"上一级\"]').click()");await Task.Delay(350);
        var parent=await Ui.CoreWebView2.ExecuteScriptAsync("location.hash");
        await Ui.CoreWebView2.ExecuteScriptAsync("document.querySelector('[aria-label=\"返回\"]').click()");await Task.Delay(350);
        var back=await Ui.CoreWebView2.ExecuteScriptAsync("JSON.stringify({route:location.hash,selected:document.querySelectorAll('[data-collection-file]:checked').length,overflow:document.documentElement.scrollWidth>innerWidth,trace:window.__ASH_NAV_TRACE__})");
        File.WriteAllText(Path.Combine(folder,"navigation-test.json"),Data.Encode(new{parent=JsonSerializer.Deserialize<string>(parent),back=JsonSerializer.Deserialize<string>(back)}));
    }
    async Task InspectPage(string url,string folder,bool navigate=true){
        Directory.CreateDirectory(folder);if(navigate)await Navigate(url);
        for(var n=0;n<30;n++){
            await ReadPage(false);
            var details=await Site.CoreWebView2.ExecuteScriptAsync("JSON.stringify({url:location.origin+location.pathname,title:document.title,ready:document.readyState,text:document.body?.innerText.slice(0,12000),inputs:[...document.querySelectorAll('input')].map(e=>({name:e.name,type:e.type,value:e.value,react:Object.keys(e).filter(k=>k.startsWith('__react')),visible:e.getClientRects().length,placeholder:e.placeholder,label:e.getAttribute('aria-label'),html:e.outerHTML,form:e.form?.outerHTML.slice(0,2500),ancestors:[e.parentElement,e.parentElement?.parentElement,e.parentElement?.parentElement?.parentElement].map(p=>p?.tagName+'.'+p?.className)})),links:[...document.querySelectorAll('a[href]')].filter(a=>/games|collections|mods|download|file_id|tab=/i.test(a.href+' '+a.innerText)).map(a=>({text:a.innerText,url:a.href.replace(/([?&](?:key|expires)=)[^&]*/gi,'$1REDACTED')}))})");
            File.WriteAllText(Path.Combine(folder,"page.json"),Data.Encode(new{snapshot,message,dom=JsonSerializer.Deserialize<string>(details)}));
            var cardDom=await Site.CoreWebView2.ExecuteScriptAsync(File.ReadAllText(Path.Combine(assets,"inspect-cards.js")));
            File.WriteAllText(Path.Combine(folder,"cards-dom.json"),JsonSerializer.Deserialize<string>(cardDom));
            if(snapshot?.Items.Count>0){
                await Task.Delay(8000);
                var coverAudit=await Ui.CoreWebView2.ExecuteScriptAsync("JSON.stringify([...document.querySelectorAll('#cards img')].map(e=>({url:e.src,loaded:e.complete&&e.naturalWidth>0,hidden:e.hidden})))");
                File.WriteAllText(Path.Combine(folder,"cover-audit.json"),JsonSerializer.Deserialize<string>(coverAudit));
            }
            if(snapshot?.Status is "challenge" or "login"||snapshot?.Items.Count>0||!string.IsNullOrWhiteSpace(snapshot?.Description))break;
            await Task.Delay(1000);
        }
    }
    void OnClosing(object? sender,CancelEventArgs e){
        if(runningTransfers.Count>0&&MessageBox.Show(this,"文件仍在下载。关闭后下次需要重新获取下载入口。是否关闭？","ASH",MessageBoxButton.YesNo)!=MessageBoxResult.Yes){e.Cancel=true;return;}
        closing=true;timer.Stop();SaveLastUrl();foreach(var t in tasks)store?.Save(t);foreach(var service in runningServices.Values)service.Service.CancelAsync();foreach(var cancel in runningTransfers.Values)cancel.Cancel();transferTickets.Clear();store?.Dispose();Ui.Dispose();Site.Dispose();
    }
}






using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;

namespace Ash.Nexus;
public partial class MainWindow {
    async void OnMessage(object? sender,CoreWebView2WebMessageReceivedEventArgs e){
        if(!ready||!Uri.TryCreate(e.Source,UriKind.Absolute,out var source)||source.Scheme!="https"||source.Host!="ash.local"||source.AbsolutePath!="/index.html"||source.Query.Length>0)return;
        try{
            using var doc=JsonDocument.Parse(e.WebMessageAsJson);var p=doc.RootElement;var action=p.GetProperty("action").GetString();
            string Val(string name)=>p.TryGetProperty(name,out var v)?v.GetString()??"":"";
            switch(action){
                case "setConcurrency":
                    if(!p.TryGetProperty("value",out var concurrencyValue)||!concurrencyValue.TryGetInt32(out var concurrency)||concurrency<1||concurrency>16)
                        throw new InvalidOperationException("同时下载文件数必须为 1–16。");
                    maxConcurrentDownloads=concurrency;
                    store.Set("maxConcurrentDownloads",concurrency.ToString());
                    Publish();
                    if(queueEnabled)ScheduleNext();
                    break;
                case "setLanguage":
                    var language=Val("value");
                    if(language is not ("en" or "zh-CN"))break;
                    Localization.Language=language;store.Set("language",language);Publish();break;
                case "ready":Publish();break;
                case "browse":
                    if(active?.Status=="waiting")throw new InvalidOperationException("正在获取当前文件的下载链接，请稍候。");
                    downloadPageActive=false;await Navigate(Val("url"));break;
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
                        runningServices.Remove(task.Id);
                        transferTickets.Remove(task.Id);
                        tasks.Remove(task);
                    }
                    store.DeleteFinished();
                    SetMessage(finished.Count>0?$"已清空 {finished.Count} 条下载记录，已下载的文件不会被删除。":"没有可清空的下载记录。");
                    break;
                case "retry":
                    var retry=tasks.FirstOrDefault(t=>t.Id==Val("id"));
                    if(retry!=null&&!runningTransfers.ContainsKey(retry.Id)&&retry.Status is ("failed" or "paused" or "cancelled")){
                        retry.Status="queued";retry.Error="";retry.CanResume=false;store.Save(retry);
                        queueEnabled=true;ScheduleNext();
                    }
                    Publish();break;
                case "folder":var dialog=new Microsoft.Win32.OpenFolderDialog{Title="选择下载目录"};if(dialog.ShowDialog(this)==true){output=dialog.FolderName;store.Set("output",output);Publish();}break;
                case "openFolder":Directory.CreateDirectory(output);Process.Start(new ProcessStartInfo("explorer.exe",output){UseShellExecute=true});break;
            }
        }catch(Exception ex){SetMessage(ex is InvalidOperationException?ex.Message:"操作未完成，请检查页面后重试。");}
    }
}

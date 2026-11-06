using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;

namespace Ash.Nexus;
public partial class MainWindow {
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
}

using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;

namespace Ash.Nexus;
public partial class MainWindow {
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
}

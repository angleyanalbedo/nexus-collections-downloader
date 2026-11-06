using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;

namespace Ash.Nexus;
// Tickets live in memory only. They are deliberately absent from the task database and UI bridge.
public sealed class TransferTicket {
    public required Uri Url { get; init; }
    public string UserAgent { get; init; } = "";
    public Uri? Referer { get; init; }
    public CookieContainer Cookies { get; init; } = new();
    public string? ETag { get; set; }
    public DateTimeOffset? Modified { get; set; }
}
public static class DirectTransfer {
    public static async Task Run(TransferTicket ticket,string path,long expected,IProgress<(long Received,long Total)> progress,CancellationToken cancel,HttpMessageHandler? testHandler=null){
        using var handler=testHandler??new HttpClientHandler{CookieContainer=ticket.Cookies,AllowAutoRedirect=true,AutomaticDecompression=DecompressionMethods.None};
        using var client=new HttpClient(handler){Timeout=Timeout.InfiniteTimeSpan};
        var part=path+".part";
        var offset=File.Exists(part)&&(ticket.ETag!=null||ticket.Modified!=null)?new FileInfo(part).Length:0;
        using var request=new HttpRequestMessage(HttpMethod.Get,ticket.Url);
        request.Headers.UserAgent.TryParseAdd(ticket.UserAgent);
        request.Headers.Referrer=ticket.Referer;
        request.Headers.AcceptEncoding.ParseAdd("identity");
        if(offset>0){request.Headers.Range=new RangeHeaderValue(offset,null);request.Headers.IfRange=ticket.ETag!=null?new RangeConditionHeaderValue(new EntityTagHeaderValue(ticket.ETag)):new RangeConditionHeaderValue(ticket.Modified!.Value);}
        using var response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,cancel);
        if(response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized or HttpStatusCode.Gone)throw new InvalidOperationException("下载链接已失效或需要重新验证，请重试以重新获取。");
        if(!response.IsSuccessStatusCode)throw new InvalidOperationException($"下载服务器返回 HTTP {(int)response.StatusCode}，请重试。");
        if(response.Content.Headers.ContentType?.MediaType=="text/html")throw new InvalidOperationException("服务器返回了网页，请重新获取下载链接。");
        if(response.StatusCode==HttpStatusCode.PartialContent){
            var range=response.Content.Headers.ContentRange;
            if(offset==0||range?.From!=offset||!range.HasLength||range.Length<offset)throw new InvalidOperationException("服务器续传范围不正确，请重新获取下载链接。");
            if(ticket.ETag!=null&&response.Headers.ETag?.Tag!=ticket.ETag)throw new InvalidOperationException("文件在续传时发生变化，请重试。");
        }else offset=0; // A server ignoring Range must restart the local file, never append.
        ticket.ETag=response.Headers.ETag is {IsWeak:false} etag?etag.Tag:null;
        ticket.Modified=response.Content.Headers.LastModified;
        var total=response.Content.Headers.ContentRange?.Length??(response.Content.Headers.ContentLength is long length?length+offset:expected);
        await using var input=await response.Content.ReadAsStreamAsync(cancel);
        await using(var output=new FileStream(part,offset>0?FileMode.Append:FileMode.Create,FileAccess.Write,FileShare.Read,131072,true)){
            var buffer=new byte[131072];long received=offset;progress.Report((received,total));
            while(true){var count=await input.ReadAsync(buffer,cancel);if(count==0)break;await output.WriteAsync(buffer.AsMemory(0,count),cancel);received+=count;progress.Report((received,total));}
            await output.FlushAsync(cancel);
            if((total>0&&received!=total)||(expected>0&&received!=expected))throw new InvalidOperationException("文件大小不符合预期，保留临时文件以便重试。");
        }
        cancel.ThrowIfCancellationRequested();
        File.Move(part,path,false);
    }
}

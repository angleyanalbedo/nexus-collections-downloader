using System.ComponentModel;
using System.IO;
using Downloader;

namespace Ash.Nexus;

public sealed class ModernTransfer {
    public DownloadService Service { get; }
    public ModernTransfer(TransferTicket ticket) {
        Service = new DownloadService(new DownloadConfiguration {
            ChunkCount = 8,
            ParallelDownload = true,
            ParallelCount = 4,
            MaxTryAgainOnFailure = 5,
            EnableAutoResumeDownload = true,
            DownloadFileExtension = ".part",
            MinimumSizeOfChunking = 1024 * 1024,
            BufferBlockSize = 81920,
            BlockTimeout = 15000,
            HttpClientTimeout = 30000,
            CheckDiskSizeBeforeDownload = true,
            RequestConfiguration = new RequestConfiguration {
                CookieContainer = ticket.Cookies,
                UserAgent = ticket.UserAgent,
                Referer = ticket.Referer?.AbsoluteUri,
                Accept = "*/*",
                Headers = new System.Net.WebHeaderCollection { "Accept-Encoding: identity" },
                KeepAlive = true,
            },
        });
    }
    public async Task Run(string url,string path,long expected,IProgress<(long Received,long Total)> progress,CancellationToken cancel) {
        AsyncCompletedEventArgs? completed = null;
        Service.DownloadProgressChanged += (_, e) => progress.Report((e.ReceivedBytesSize, e.TotalBytesToReceive));
        Service.DownloadFileCompleted += (_, e) => completed = e;
        await Service.DownloadFileTaskAsync(url, path, cancel);
        if (completed?.Error is not null) throw completed.Error;
        if (completed?.Cancelled == true || cancel.IsCancellationRequested) throw new OperationCanceledException(cancel);
        if (!File.Exists(path)) throw new InvalidOperationException("下载未生成文件，请重新获取下载链接。");
        var actual = new FileInfo(path).Length;
        if (expected > 0 && actual != expected) throw new InvalidOperationException("文件大小不符合预期，已保留断点文件以便继续。");
    }
}

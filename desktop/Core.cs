using System.IO;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Ash.Nexus;
public static class Data {
    public static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true };
    public static string Encode(object? value) => JsonSerializer.Serialize(value, Json);
    public static bool NexusUrl(string text) => Uri.TryCreate(text, UriKind.Absolute, out var u) && u.Scheme == "https" && (u.Host == "nexusmods.com" || u.Host.EndsWith(".nexusmods.com", StringComparison.OrdinalIgnoreCase)) && string.IsNullOrEmpty(u.UserInfo);
    public static string PageUrl(string text) {
        if (!NexusUrl(text)) throw new InvalidOperationException("请输入 Nexus Mods 的 HTTPS 页面链接。");
        var u = new UriBuilder(text); u.Fragment = "";
        if (u.Query.Contains("key=", StringComparison.OrdinalIgnoreCase) || u.Query.Contains("expires=", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("请提供文件页面链接，临时下载凭证不会保存。");
        return u.Uri.AbsoluteUri;
    }
    public static List<SiteItem> CollectionSelection(PageSnapshot? page,string key,IEnumerable<string> ids){
        var manifest=page?.Collection;
        if(page?.Kind!="collection"||manifest is null||!manifest.Complete||manifest.Key!=key||manifest.Files.Count!=manifest.ExpectedFiles)
            throw new InvalidOperationException("合集清单未读取完整或修订版已改变，请重新确认清单。");
        var selected=ids.Distinct().ToList();
        if(selected.Count==0)throw new InvalidOperationException("请至少选择一个文件。");
        var result=new List<SiteItem>();
        foreach(var id in selected){
            var file=manifest.Files.SingleOrDefault(f=>f.Id==id);
            if(file is null||!System.Text.RegularExpressions.Regex.IsMatch(file.FileId,"^[0-9]+$")||!NexusUrl(file.Url)||new Uri(file.Url).Query.Split('&','?').All(p=>p!="file_id="+file.FileId))
                throw new InvalidOperationException("选择的文件不属于当前合集清单。");
            result.Add(file);
        }
        return result;
    }
    public static string SafePath(string root, string folder, string filename) {
        var bad = Path.GetInvalidFileNameChars();
        string Clean(string s) => new string(s.Select(c => bad.Contains(c) ? '_' : c).ToArray()).Trim(' ', '.');
        var name = Clean(Path.GetFileName(filename));
        if (name.Length > 150) name = name[..125] + Path.GetExtension(name)[..Math.Min(20, Path.GetExtension(name).Length)];
        if (string.IsNullOrWhiteSpace(name)) name = "download.bin";
        var dir = Path.GetFullPath(Path.Combine(root, Clean(folder)));
        var prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!dir.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new IOException("无效保存路径");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, name); var n = 1;
        while (File.Exists(path)) path = Path.Combine(dir, Path.GetFileNameWithoutExtension(name) + $" ({n++})" + Path.GetExtension(name));
        return path;
    }
}
public class SiteItem {
    public string ModName { get; set; } = "";
    public string Version { get; set; } = "";
    public long Size { get; set; }
    public bool? Optional { get; set; }
    public string CollectionName { get; set; } = "";
    public string Revision { get; set; } = "";
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Url { get; set; } = "";
    public string Image { get; set; } = "";
    public string Meta { get; set; } = "";
    public string Group { get; set; } = "";
    public string FileId { get; set; } = "";
    public string DownloadUrl { get; set; } = "";
    public string Kind { get; set; } = "mod";
}
public class CollectionManifest {
    public string Key { get; set; } = "";
    public string Revision { get; set; } = "";
    public int ExpectedFiles { get; set; }
    public bool Complete { get; set; }
    public string Message { get; set; } = "";
    public List<SiteItem> Files { get; set; } = [];
}
public class PageSnapshot {
    public CollectionManifest? Collection { get; set; }
    public string Title { get; set; } = "";
    public string Url { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Status { get; set; } = "";
    public string Description { get; set; } = "";
    public string ResultText { get; set; } = "";
    public List<SiteItem> Navigation { get; set; } = [];
    public List<SiteItem> Items { get; set; } = [];
}
public class DownloadTask {
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public SiteItem File { get; set; } = new();
    public string Status { get; set; } = "queued";
    public string Error { get; set; } = "";
    public string Path { get; set; } = "";
    public long Received { get; set; }
    public long Total { get; set; }
    public bool CanResume { get; set; }
}
public sealed class Store : IDisposable {
    readonly SqliteConnection db;
    public Store(string folder) {
        Directory.CreateDirectory(folder);
        db = new SqliteConnection($"Data Source={Path.Combine(folder, "ash.db")}"); db.Open();
        using var cmd = db.CreateCommand(); cmd.CommandText = "CREATE TABLE IF NOT EXISTS tasks(id TEXT PRIMARY KEY, data TEXT NOT NULL); CREATE TABLE IF NOT EXISTS settings(name TEXT PRIMARY KEY, value TEXT NOT NULL);";cmd.ExecuteNonQuery();
    }
    public List<DownloadTask> Load() {
        using var cmd = db.CreateCommand(); cmd.CommandText = "SELECT data FROM tasks ORDER BY rowid"; using var r = cmd.ExecuteReader(); List<DownloadTask> rows=[];
        while(r.Read()) { var t = JsonSerializer.Deserialize<DownloadTask>(r.GetString(0),Data.Json)!;t.CanResume=false;
            if(t.Status=="completed" && (!File.Exists(t.Path) || new FileInfo(t.Path).Length!=t.Received)){t.Status="failed";t.Error="已完成文件缺失或大小改变，请重新下载。";}
            else if(t.Status is "downloading" or "waiting" or "paused"){t.Status="queued";t.Error="上次任务未完成，将重新获取下载入口。";}
            rows.Add(t);
        } return rows;
    }
    public void Save(DownloadTask row) {using var cmd=db.CreateCommand();cmd.CommandText="INSERT INTO tasks(id,data) VALUES($id,$data) ON CONFLICT(id) DO UPDATE SET data=$data";cmd.Parameters.AddWithValue("$id",row.Id);cmd.Parameters.AddWithValue("$data",Data.Encode(row));cmd.ExecuteNonQuery();}
    public void SaveMany(IEnumerable<DownloadTask> rows){
        using var transaction=db.BeginTransaction();
        foreach(var row in rows){using var cmd=db.CreateCommand();cmd.Transaction=transaction;cmd.CommandText="INSERT INTO tasks(id,data) VALUES($id,$data)";cmd.Parameters.AddWithValue("$id",row.Id);cmd.Parameters.AddWithValue("$data",Data.Encode(row));cmd.ExecuteNonQuery();}
        transaction.Commit();
    }
    public string? Setting(string name) {using var cmd=db.CreateCommand();cmd.CommandText="SELECT value FROM settings WHERE name=$n";cmd.Parameters.AddWithValue("$n",name);return cmd.ExecuteScalar() as string;}
    public void Set(string name,string value){using var cmd=db.CreateCommand();cmd.CommandText="INSERT INTO settings VALUES($n,$v) ON CONFLICT(name) DO UPDATE SET value=$v";cmd.Parameters.AddWithValue("$n",name);cmd.Parameters.AddWithValue("$v",value);cmd.ExecuteNonQuery();}
    public void Dispose()=>db.Dispose();
}


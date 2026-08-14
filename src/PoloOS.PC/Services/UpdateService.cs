using System.Diagnostics;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;

namespace PoloOS.PC.Services;

public sealed class UpdateManifest
{
    public string Version { get; set; } = "";
    public string PackageUrl { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public string Notes { get; set; } = "";
    public bool Mandatory { get; set; }
}

public sealed class UpdateService : IDisposable
{
    private static readonly Uri GitHubManifestUri = new("https://raw.githubusercontent.com/burakaan01-lab/PoloOS-Updates/main/api/version");
    private readonly RemoteConfig _config;
    private readonly HttpClient _http;

    public UpdateService(RemoteConfig config)
    {
        _config = config;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("PoloOS-PC", "1.0"));
    }

    public async Task<UpdateManifest?> CheckAsync(string currentVersion, CancellationToken cancellationToken = default)
    {
        var builder = new UriBuilder(GitHubManifestUri) { Query = $"channel={Uri.EscapeDataString(_config.Channel)}&t={DateTimeOffset.UtcNow.ToUnixTimeSeconds()}" };
        using var request = new HttpRequestMessage(HttpMethod.Get, builder.Uri);
        AddAuth(request);
        using var response = await _http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var manifest = await JsonSerializer.DeserializeAsync<UpdateManifest>(stream,new JsonSerializerOptions { PropertyNameCaseInsensitive = true },cancellationToken);
        if (manifest is null || string.IsNullOrWhiteSpace(manifest.Version)) throw new InvalidDataException("Sunucu geçerli version manifest döndürmedi.");
        return CompareDottedVersions(manifest.Version,currentVersion)>0?manifest:null;
    }

    public async Task<string> DownloadAndVerifyAsync(UpdateManifest manifest,IProgress<string>? progress=null,CancellationToken cancellationToken=default)
    {
        if(string.IsNullOrWhiteSpace(manifest.PackageUrl))throw new InvalidDataException("Güncelleme paket adresi boş.");
        var packageUri=Uri.TryCreate(manifest.PackageUrl,UriKind.Absolute,out var absolute)?absolute:_config.BuildUri(manifest.PackageUrl);
        var updateDir=Path.Combine(AppContext.BaseDirectory,"updates");Directory.CreateDirectory(updateDir);
        var safeVersion=string.Concat(manifest.Version.Select(c=>char.IsLetterOrDigit(c)||c is '.' or '-' or '_'?c:'_'));
        var target=Path.Combine(updateDir,$"PoloOS_update_{safeVersion}.zip");var temp=target+".download";
        try{progress?.Report("PAKET İNDİRİLİYOR");using var request=new HttpRequestMessage(HttpMethod.Get,packageUri);AddAuth(request);using var response=await _http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,cancellationToken);response.EnsureSuccessStatusCode();await using(var input=await response.Content.ReadAsStreamAsync(cancellationToken))await using(var output=new FileStream(temp,FileMode.Create,FileAccess.Write,FileShare.None,64*1024,useAsync:true)){await input.CopyToAsync(output,cancellationToken);}progress?.Report("SHA-256 KONTROL");if(!string.IsNullOrWhiteSpace(manifest.Sha256)){var actual=await ComputeSha256Async(temp,cancellationToken);if(!actual.Equals(NormalizeHash(manifest.Sha256),StringComparison.OrdinalIgnoreCase))throw new InvalidDataException($"SHA-256 uyuşmadı. server={NormalizeHash(manifest.Sha256)} actual={actual}");}File.Move(temp,target,overwrite:true);progress?.Report("PAKET HAZIR");return target;}catch{TryDelete(temp);throw;}
    }

    public Process LaunchUpdater(string packagePath,string targetVersion,int currentProcessId)
    {
        var sourceUpdaterDir=Path.Combine(AppContext.BaseDirectory,"updater");string tempUpdaterDir;
        if(Directory.Exists(sourceUpdaterDir)){tempUpdaterDir=Path.Combine(Path.GetTempPath(),"PoloOS_Updater_"+Guid.NewGuid().ToString("N"));CopyDirectory(sourceUpdaterDir,tempUpdaterDir);}else{var rootExe=Path.Combine(AppContext.BaseDirectory,"PoloOS.Updater.exe");if(!File.Exists(rootExe))throw new FileNotFoundException("PoloOS.Updater bulunamadı. Publish paketini yeniden oluştur.",rootExe);tempUpdaterDir=Path.Combine(Path.GetTempPath(),"PoloOS_Updater_"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(tempUpdaterDir);foreach(var file in Directory.EnumerateFiles(AppContext.BaseDirectory,"PoloOS.Updater*",SearchOption.TopDirectoryOnly))File.Copy(file,Path.Combine(tempUpdaterDir,Path.GetFileName(file)),true);}
        var updaterExe=Path.Combine(tempUpdaterDir,"PoloOS.Updater.exe");if(!File.Exists(updaterExe))throw new FileNotFoundException("Geçici updater EXE oluşturulamadı.",updaterExe);var start=new ProcessStartInfo{FileName=updaterExe,UseShellExecute=false,WorkingDirectory=tempUpdaterDir};start.ArgumentList.Add("--pid");start.ArgumentList.Add(currentProcessId.ToString());start.ArgumentList.Add("--install-dir");start.ArgumentList.Add(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar));start.ArgumentList.Add("--package");start.ArgumentList.Add(packagePath);start.ArgumentList.Add("--target-version");start.ArgumentList.Add(targetVersion);start.ArgumentList.Add("--launch");start.ArgumentList.Add("PoloOS.PC.exe");start.ArgumentList.Add("--self-dir");start.ArgumentList.Add(tempUpdaterDir);return Process.Start(start)??throw new InvalidOperationException("Updater process başlatılamadı.");
    }

    public static int CompareDottedVersions(string left,string right){static int[] Parts(string value)=>value.Trim().TrimStart('v','V').Split('.',StringSplitOptions.RemoveEmptyEntries).Select(x=>int.TryParse(x,out var n)?n:0).ToArray();var a=Parts(left);var b=Parts(right);var count=Math.Max(a.Length,b.Length);for(var i=0;i<count;i++){var av=i<a.Length?a[i]:0;var bv=i<b.Length?b[i]:0;var c=av.CompareTo(bv);if(c!=0)return c;}return 0;}
    private void AddAuth(HttpRequestMessage request){if(!string.IsNullOrWhiteSpace(_config.AuthToken))request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",_config.AuthToken);}
    private static async Task<string> ComputeSha256Async(string path,CancellationToken cancellationToken){await using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read,64*1024,useAsync:true);var hash=await SHA256.HashDataAsync(stream,cancellationToken);return Convert.ToHexString(hash);} private static string NormalizeHash(string value)=>value.Replace(" ","",StringComparison.Ordinal).Replace("-","",StringComparison.Ordinal).Trim();
    private static void CopyDirectory(string source,string destination){Directory.CreateDirectory(destination);foreach(var directory in Directory.EnumerateDirectories(source,"*",SearchOption.AllDirectories)){var relative=Path.GetRelativePath(source,directory);Directory.CreateDirectory(Path.Combine(destination,relative));}foreach(var file in Directory.EnumerateFiles(source,"*",SearchOption.AllDirectories)){var relative=Path.GetRelativePath(source,file);var target=Path.Combine(destination,relative);Directory.CreateDirectory(Path.GetDirectoryName(target)!);File.Copy(file,target,true);}}
    private static void TryDelete(string path){try{if(File.Exists(path))File.Delete(path);}catch{}} public void Dispose()=>_http.Dispose();
}

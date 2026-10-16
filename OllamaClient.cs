using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
namespace OllamaTextLens;
internal static class OllamaClient
{
    internal static HttpClient Client { get; set; } = new(new HttpClientHandler { UseProxy=false }) { Timeout=TimeSpan.FromMinutes(5) };
    internal static Uri Validate(string value)
    {
        if (!Uri.TryCreate(value.Trim().TrimEnd('/')+"/",UriKind.Absolute,out var uri) || uri.Scheme!="http" || !uri.IsLoopback || uri.UserInfo.Length>0 || uri.AbsolutePath!="/" || uri.Query.Length>0 || uri.Fragment.Length>0)
            throw new AppError("InvalidEndpoint");
        return uri;
    }
    internal static async Task<string[]> Models(Uri endpoint)
    {
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(8));
        using var response=await Client.GetAsync(new Uri(endpoint,"api/tags"),timeout.Token);
        response.EnsureSuccessStatusCode();
        using var json=JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
        return json.RootElement.GetProperty("models").EnumerateArray().Select(m=>m.GetProperty("name").GetString()!).ToArray();
    }
    internal static async Task Translate(Uri endpoint,string model,string text,string target,Action<string> update,CancellationToken token)
    {
        var payload=new { model, stream=true, think=false, messages=new[] {
            new { role="system",content=Languages.Prompt(target) },
            new { role="user",content=text } }, options=new { temperature=0.2, num_predict=1400 } };
        using var request=new HttpRequestMessage(HttpMethod.Post,new Uri(endpoint,"api/chat")) { Content=JsonContent.Create(payload) };
        using var response=await Client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,token);
        if (!response.IsSuccessStatusCode) throw new AppError("ProviderFailed",$"HTTP {(int)response.StatusCode}");
        using var reader=new System.IO.StreamReader(await response.Content.ReadAsStreamAsync(token));
        var result=new System.Text.StringBuilder();
        bool done=false;
        while (await reader.ReadLineAsync(token) is { } line)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            using var json=JsonDocument.Parse(line);
            var root=json.RootElement;
            if (root.TryGetProperty("error",out var error)) throw new AppError("ProviderFailed",error.GetString());
            if (root.TryGetProperty("message",out var message) && message.TryGetProperty("content",out var content)) { result.Append(content.GetString()); update(result.ToString()); }
            if (root.TryGetProperty("done",out var d) && d.GetBoolean()) { done=true; break; }
        }
        if (!done) throw new AppError("StreamIncomplete");
        if (result.Length==0) throw new AppError("EmptyResponse");
    }
}

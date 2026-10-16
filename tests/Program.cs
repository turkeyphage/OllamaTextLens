using System.Net;
using System.Text;
using System.Text.Json;
using OllamaTextLens;
static void Check(bool condition,string name) { if(!condition) throw new Exception(name); Console.WriteLine("PASS "+name); }
foreach(var invalid in new[]{"https://example.com","http://example.com","http://127.0.0.1/api","http://user@localhost:11434"})
{
    bool rejected=false; try { OllamaClient.Validate(invalid); } catch(InvalidOperationException) { rejected=true; }
    Check(rejected,"reject "+invalid);
}
OllamaClient.Client.Dispose(); OllamaClient.Client=new HttpClient(new FakeHandler());
var endpoint=OllamaClient.Validate("http://127.0.0.1:11434");
Check((await OllamaClient.Models(endpoint)).SequenceEqual(new[]{"test-model"}),"model discovery");
string result="";
await OllamaClient.Translate(endpoint,"test-model","Example",s=>result=s,CancellationToken.None);
Check(result=="翻譯完成","stream accumulation");
bool incomplete=false;
try { await OllamaClient.Translate(endpoint,"test-model","Example",_=>{},CancellationToken.None); } catch(InvalidOperationException) { incomplete=true; }
Check(incomplete,"reject incomplete stream");
Console.WriteLine("All smoke tests passed.");
sealed class FakeHandler : HttpMessageHandler
{
    int calls;
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
    {
        string body;
        if(request.Method==HttpMethod.Get) body="{\"models\":[{\"name\":\"test-model\"}]}";
        else {
            using var input=JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            if(!input.RootElement.GetProperty("stream").GetBoolean() || !input.RootElement.GetProperty("messages")[0].GetProperty("content").GetString()!.Contains("繁體中文")) throw new Exception("Invalid request");
            body=++calls==1?"{\"message\":{\"content\":\"翻譯\"},\"done\":false}\n{\"message\":{\"content\":\"完成\"},\"done\":true}\n":"{\"message\":{\"content\":\"partial\"},\"done\":false}\n";
        }
        return new HttpResponseMessage(HttpStatusCode.OK) { Content=new StringContent(body,Encoding.UTF8,"application/json") };
    }
}

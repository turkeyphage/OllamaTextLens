using System.Net;
using System.Text;
using System.Text.Json;
using OllamaTextLens;
static void Check(bool condition,string name) { if(!condition) throw new Exception(name); Console.WriteLine("PASS "+name); }
Check(Languages.Interface.Select(x=>x.Id).SequenceEqual(new[]{"zh-TW","en","ja","es","ko","vi","th","fr"}),"requested eight interface languages");
var englishKeys=I18n.Bundle("en").Keys.Order().ToArray();
foreach(var language in Languages.Interface)
{
    var bundle=I18n.Bundle(language.Id);
    Check(bundle.Keys.Order().SequenceEqual(englishKeys) && bundle.Values.All(x=>!string.IsNullOrWhiteSpace(x)),"complete UI resource "+language.Id);
    Check(!I18n.Text(language.Id,"Done",1.2,"test").Contains('{'),"localized status placeholders "+language.Id);
    Check(I18n.Text(language.Id,"Translation",Languages.Target("ja").Name).Contains("日本語"),"UI language independent of target "+language.Id);
}
foreach(var language in Languages.Targets)
    Check(Languages.Prompt(language.Id).Contains(language.PromptName) && Languages.Prompt(language.Id).Contains(TranslationSections.Explanation),"target prompt "+language.Id);
var legacy=JsonSerializer.Deserialize<UserSettings>("{\"Endpoint\":\"http://127.0.0.1:11434\",\"Model\":\"test-model\"}")!;
Check(legacy.UiLanguage=="zh-TW" && legacy.TargetLanguage=="zh-TW","old settings default to Traditional Chinese");
var preferences=new UserSettings(legacy.Endpoint,legacy.Model,"fr","ja");
Check(JsonSerializer.Deserialize<UserSettings>(JsonSerializer.Serialize(preferences))==preferences,"independent language preferences round trip");
Check(Languages.Ui("unknown").Id=="zh-TW" && Languages.Target("unknown").Id=="zh-TW","unknown languages fall back safely");
Check(TranslationSections.Parse("[[TRANSLATION]]\nこんにちは\n[[EXPLANATION]]\n挨拶です。") == ("こんにちは","挨拶です。"),"language-neutral sections");
Check(TranslationSections.Parse("[[TRANSLATION]]\nHola\n[[EXPLAN") == ("Hola",""),"partial streaming markers hidden");
Check(TranslationSections.Parse("[[TRANS") == ("",""),"partial first marker hidden");
Check(TranslationSections.Parse("Plain response") == ("Plain response",""),"unformatted output remains readable");
foreach(var invalid in new[]{"https://example.com","http://example.com","http://127.0.0.1/api","http://user@localhost:11434"})
{
    bool rejected=false; try { OllamaClient.Validate(invalid); } catch(InvalidOperationException) { rejected=true; }
    Check(rejected,"reject "+invalid);
}
OllamaClient.Client.Dispose(); OllamaClient.Client=new HttpClient(new FakeHandler());
var endpoint=OllamaClient.Validate("http://127.0.0.1:11434");
Check((await OllamaClient.Models(endpoint)).SequenceEqual(new[]{"test-model"}),"model discovery");
string result="";
await OllamaClient.Translate(endpoint,"test-model","Example","ja",s=>result=s,CancellationToken.None);
Check(result=="翻譯完成","stream accumulation");
bool incomplete=false;
try { await OllamaClient.Translate(endpoint,"test-model","Example","ja",_=>{},CancellationToken.None); } catch(AppError e) when(e.Key=="StreamIncomplete") { incomplete=true; }
Check(incomplete,"reject incomplete stream");
using(var cancelled=new CancellationTokenSource())
{
    cancelled.Cancel(); bool stopped=false;
    try { await OllamaClient.Translate(endpoint,"test-model","Example","ja",_=>{},cancelled.Token); } catch(OperationCanceledException) { stopped=true; }
    Check(stopped,"cancelled request stops");
}
Console.WriteLine("All smoke tests passed.");
sealed class FakeHandler : HttpMessageHandler
{
    int calls;
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        string body;
        if(request.Method==HttpMethod.Get) body="{\"models\":[{\"name\":\"test-model\"}]}";
        else {
            using var input=JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            var prompt=input.RootElement.GetProperty("messages")[0].GetProperty("content").GetString()!;
            if(!input.RootElement.GetProperty("stream").GetBoolean() || !prompt.Contains("Japanese") || !prompt.Contains("[[TRANSLATION]]") || input.RootElement.GetProperty("messages")[1].GetProperty("content").GetString()!="Example") throw new Exception("Invalid multilingual request");
            body=++calls==1?"{\"message\":{\"content\":\"翻譯\"},\"done\":false}\n{\"message\":{\"content\":\"完成\"},\"done\":true}\n":"{\"message\":{\"content\":\"partial\"},\"done\":false}\n";
        }
        return new HttpResponseMessage(HttpStatusCode.OK) { Content=new StringContent(body,Encoding.UTF8,"application/json") };
    }
}

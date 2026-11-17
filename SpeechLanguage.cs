using System.Reflection;
using System.Text.RegularExpressions;
using NTextCat;

namespace OllamaTextLens;

internal static class SpeechLanguage
{
    static readonly Lazy<RankedLanguageIdentifier> detector=new(()=> {
        using var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("OllamaTextLens.Core14.profile.xml")
            ??throw new InvalidOperationException("Missing local language profile.");
        return new RankedLanguageIdentifierFactory().Load(stream);
    });
    internal static string? Detect(string text)
    {
        text=SpeechText.Normalize(text);
        // Kana distinguish Japanese from Chinese; kanji-only fragments require a manual choice.
        if(Regex.IsMatch(text,@"[\u3041-\u3096\u30a1-\u30fa]")) return "ja";
        // Short selections and mixed scripts need a manual choice instead of a silent guess.
        if(Regex.Matches(text,@"\p{L}+").Count<4) return null;
        var letters=text.Where(char.IsLetter).ToArray();
        if(letters.Length==0 || letters.Count(c=>c>'\u024f')>letters.Length/10) return null;
        lock(detector) {
            var ranked=detector.Value.Identify(text[..Math.Min(text.Length,4000)]).Take(2).ToArray();
            if(ranked.Length==0 || (ranked.Length>1 && ranked[1].Item2<ranked[0].Item2*1.001)) return null;
            return ranked[0].Item1.Iso639_3 switch { "eng"=>"en", "spa"=>"es", _=>null };
        }
    }
    internal static bool IsSpeaker(string language,int speaker)=>language switch {
        "en"=>speaker is 3 or 16, "es"=>speaker is 28 or 29, "ja"=>speaker is 37 or 41, _=>false
    };
}

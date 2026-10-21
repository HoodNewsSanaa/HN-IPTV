using System.Text.RegularExpressions;
namespace HnIptv.Translation;

// Translate stable clauses, not every partial word. The native stream retains acoustic context.
public sealed class StreamingPhrases
{
    private string[] _previous=[];
    private int _sent;
    private double _lastSent;
    public void Clear() { _previous=[];_sent=0;_lastSent=0; }
    public IReadOnlyList<string> Update(string transcript,bool final,double audioEnd)
    {
        var words=transcript.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries);
        int stable=0;
        while(stable<Math.Min(words.Length,_previous.Length) && Key(words[stable])==Key(_previous[stable]))stable++;
        if(final)stable=words.Length;
        else stable=Math.Min(stable,Math.Max(0,words.Length-1)); // last token may still be a word fragment
        // RNNT results are cumulative within an utterance. Never rewrite previously displayed phrases.
        if(words.Length<_sent || stable<_sent) { if(final)Clear();_previous=words;return []; }
        var result=new List<string>();
        int boundary=_sent;
        for(int i=_sent;i<stable;i++) {
            int count=i-boundary+1;
            bool punctuation=Regex.IsMatch(words[i],"[.!?。！？،,;؛:][\\\"'»”)]*$");
            if((punctuation && count>=3) || count>=12 || (audioEnd-_lastSent>=5 && count>=6)) {
                result.Add(string.Join(" ",words[boundary..(i+1)]));boundary=i+1;_lastSent=audioEnd;
            }
        }
        if(final && boundary<words.Length) { result.Add(string.Join(" ",words[boundary..]));boundary=words.Length; }
        _sent=boundary;_previous=words;
        if(final) { _previous=[];_sent=0;_lastSent=audioEnd; }
        return result;
    }
    private static string Key(string value)=>string.Concat(value.Where(char.IsLetterOrDigit)).ToLowerInvariant();
    public static IReadOnlyList<SpeechCaptionCue> Cues(string text)
    {
        var cues=new List<SpeechCaptionCue>();var current="";
        foreach(string word in text.Split(' ',StringSplitOptions.RemoveEmptyEntries)) {
            if(current.Length>0 && current.Length+word.Length+1>76) { cues.Add(Cue(current));current=""; }
            current=current.Length==0?word:current+" "+word;
        }
        if(current.Length>0)cues.Add(Cue(current));return cues;
    }
    private static SpeechCaptionCue Cue(string text)=>new(text,Math.Clamp(text.Length/15d,1.8,6));
}

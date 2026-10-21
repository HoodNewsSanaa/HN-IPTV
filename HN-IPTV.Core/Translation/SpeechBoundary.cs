namespace HnIptv.Translation;

// A pause plus terminal punctuation is an endpoint hint, not a guarantee of semantic completeness.
public static class SpeechBoundary
{
    public static bool ShouldDecode(double duration,double lastSpeechEnd,double lastDecode)
        => duration>=4 && duration-lastSpeechEnd>=.55 && duration-lastDecode>=2;
    public static bool EndsSentence(string text)
    {
        string ending=text.TrimEnd().TrimEnd('"','\'','\u201d','\u2019',')',']','\u00bb').TrimEnd();
        return ending.Length>0 && ".!?\u3002\uff01\uff1f\u061f\u06d4".Contains(ending[^1]);
    }
}

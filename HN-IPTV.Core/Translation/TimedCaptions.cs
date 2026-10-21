namespace HnIptv.Translation;

public sealed record TimedCaption(double Start,double End,string Text);
public sealed class TimedCaptions
{
    private readonly List<TimedCaption> _cues=[];
    public void Clear()=>_cues.Clear();
    public void AddParagraph(string text,double start,double end)
    {
        if(!double.IsFinite(start)||!double.IsFinite(end)||start<0||end<=start)return;
        var parts=StreamingPhrases.Cues(text);double weight=parts.Sum(p=>p.Text.Length);
        if(weight==0)return;
        double cursor=start;
        foreach(var part in parts) {
            double next=cursor+(end-start)*part.Text.Length/weight;
            _cues.Add(new(cursor,next,part.Text));cursor=next;
        }
        _cues.Sort((a,b)=>a.Start.CompareTo(b.Start));
        if(_cues.Count>256)_cues.RemoveRange(0,_cues.Count-256);
    }
    public string At(double position)
    {
        if(!double.IsFinite(position)||position<0)return "";
        _cues.RemoveAll(c=>c.End<position-2);
        return _cues.LastOrDefault(c=>c.Start<=position&&position<c.End)?.Text??"";
    }
}

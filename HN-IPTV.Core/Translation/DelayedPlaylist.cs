using System.Globalization;
using System.Text;
namespace HnIptv.Translation;

public sealed record BufferedSegment(long Sequence,string File,double Start,double End);
public sealed class DelayedPlaylist
{
    private readonly SortedDictionary<long,BufferedSegment> _segments=[];
    private double _end;
    private long _last=-1;
    public double SourceEnd=>_end;
    public void Read(string playlist)
    {
        long sequence=0;double duration=0;
        foreach(string raw in playlist.Split('\n')) {
            string line=raw.Trim();
            if(line.StartsWith("#EXT-X-MEDIA-SEQUENCE:"))sequence=long.Parse(line[22..],CultureInfo.InvariantCulture);
            else if(line.StartsWith("#EXTINF:"))duration=double.Parse(line[8..].Split(',')[0],CultureInfo.InvariantCulture);
            else if(line.Length>0&&!line.StartsWith('#')) {
                if(sequence>_last) {
                    if(_last>=0&&sequence!=_last+1)throw new InvalidDataException("The source buffer skipped a segment.");
                    if(!System.Text.RegularExpressions.Regex.IsMatch(line,@"^segment[0-9]+\.ts$")||!double.IsFinite(duration)||duration<=0||duration>120)throw new InvalidDataException("Invalid buffered segment.");
                    _segments[sequence]=new(sequence,line,_end,_end+duration);_end+=duration;_last=sequence;
                }
                sequence++;
            }
        }
        while(_segments.Count>90)_segments.Remove(_segments.Keys.First());
    }
    public string? Build(double audioHead,double translatedThrough,int delay,bool ended=false)
    {
        double available=Math.Min(ended?audioHead:audioHead-delay,translatedThrough);
        var ready=_segments.Values.Where(s=>s.End<=available+.02).TakeLast(60).ToArray();
        if(ready.Length==0)return null;
        var result=new StringBuilder("#EXTM3U\n#EXT-X-VERSION:3\n");
        result.AppendLine("#EXT-X-TARGETDURATION:"+Math.Ceiling(_segments.Values.Max(s=>s.End-s.Start)).ToString(CultureInfo.InvariantCulture));
        result.AppendLine("#EXT-X-MEDIA-SEQUENCE:"+ready[0].Sequence.ToString(CultureInfo.InvariantCulture));
        result.AppendLine("#EXT-X-START:TIME-OFFSET=0.0,PRECISE=YES");
        foreach(var segment in ready) {result.AppendLine("#EXTINF:"+(segment.End-segment.Start).ToString("0.000000",CultureInfo.InvariantCulture)+",");result.AppendLine(segment.File);}
        if(ended&&ready[^1].Sequence==_last)result.AppendLine("#EXT-X-ENDLIST");
        return result.ToString();
    }
}

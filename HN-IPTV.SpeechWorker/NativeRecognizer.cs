using Whisper.net;
using Whisper.net.LibraryLoader;

// The model remains loaded in this isolated process. No cloud key or network is used here.
internal sealed class NativeRecognizer : IDisposable
{
    private readonly WhisperFactory _factory;
    private readonly WhisperProcessor _processor;
    private readonly List<float> _audio=[];
    private readonly bool _buffered;
    private double _offset;
    internal sealed record Recognition(string Text,string Language,bool Final,double StartSeconds,double EndSeconds);
    internal NativeRecognizer(string directory,string modelPath,string language,bool gpu,int bufferSeconds)
    {
        RuntimeOptions.RuntimeLibraryOrder=gpu?[RuntimeLibrary.Vulkan]:[RuntimeLibrary.Cpu];
        _factory=WhisperFactory.FromPath(modelPath,new WhisperFactoryOptions{UseGpu=gpu});
        try {
            _processor=_factory.CreateBuilder().WithLanguage(language=="auto"?"auto":language)
                .WithThreads(Math.Clamp(Environment.ProcessorCount/2,1,8))
                .WithNoContext().WithTemperature(0).WithTemperatureInc(0)
                .WithNoSpeechThreshold(.6f).WithProbabilities().Build();
        }catch{_factory.Dispose();throw;}
        _buffered=bufferSeconds>0;
    }
    internal void Reset(){_audio.Clear();_offset=0;}
    internal async Task<List<Recognition>> Push(float[] samples,int rate,bool finish=false)
    {
        if(rate!=44100)throw new InvalidDataException("Unexpected sample rate.");
        _audio.AddRange(samples);
        if(_audio.Count>44100*20)throw new InvalidDataException("Speech context exceeds limit.");
        if(!finish&&(_buffered||_audio.Count<44100*5))return [];
        var input=_audio.ToArray();_audio.Clear();double duration=input.Length/44100d,offset=_offset;_offset+=duration;
        if(input.Length<4410)return [];
        double energy=0;foreach(float sample in input)energy+=sample*sample;
        // Avoid decoding digital silence; Whisper's own no-speech probability handles other non-speech audio.
        if(Math.Sqrt(energy/input.Length)<.001)return [];
        float[] mono=Resample(input);var result=new List<Recognition>();
        await foreach(var segment in _processor.ProcessAsync(mono)) {
            string text=segment.Text.Trim();
            if(!text.Any(char.IsLetterOrDigit)||segment.NoSpeechProbability>=.6f||segment.Probability<.15f)continue;
            double start=Math.Clamp(segment.Start.TotalSeconds,0,duration),end=Math.Clamp(segment.End.TotalSeconds,0,duration);
            if(end>start)result.Add(new(text,segment.Language,true,offset+start,offset+end));
        }
        return result;
    }
    // Band-limited conversion to Whisper's 16 kHz input. Resample the complete batch so chunk boundaries stay continuous.
    private static float[] Resample(float[] input)
    {
        const int radius=24;const double cutoff=16000d/44100*.94;
        var output=new float[(int)((long)input.Length*16000/44100)];
        for(int i=0;i<output.Length;i++) {
            double position=i*44100d/16000;int center=(int)position;double sum=0,weight=0;
            for(int j=center-radius+1;j<=center+radius;j++) {
                if(j<0||j>=input.Length)continue;
                double x=j-position,z=Math.PI*x*cutoff;
                double w=(Math.Abs(z)<1e-9?cutoff:Math.Sin(z)/(Math.PI*x))*(.5+.5*Math.Cos(Math.PI*x/radius));
                sum+=input[j]*w;weight+=w;
            }
            output[i]=(float)(sum/weight);
        }
        return output;
    }
    public void Dispose(){_processor.Dispose();_factory.Dispose();}
}

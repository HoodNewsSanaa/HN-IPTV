using System.Runtime.InteropServices;

// NVIDIA NeMo-Speech.cpp v0.2.0 stable C ABI. All calls stay on the worker thread.
internal sealed class NativeRecognizer : IDisposable
{
    private nint _recognizer, _stream;
    private readonly nint _language;
    internal NativeRecognizer(string directory, string modelPath, string language, bool gpu)
    {
        SetDllDirectory(directory);
        NativeLibrary.Load(Path.Combine(directory,"nemo_speech_asr_c.dll"));
        NativeLibrary.SetDllImportResolver(typeof(NativeRecognizer).Assembly, (name,_,_) =>
            name == "nemo_speech_asr_c" ? NativeLibrary.Load(Path.Combine(directory,"nemo_speech_asr_c.dll")) : 0);
        _language = Marshal.StringToCoTaskMemUTF8(language);
        nint path = Marshal.StringToCoTaskMemUTF8(modelPath);
        try {
            var backend = new Backend { Size = (nuint)Marshal.SizeOf<Backend>(), Gpu = gpu ? 0 : -1 };
            var model = new Model { Size = (nuint)Marshal.SizeOf<Model>(), Path = path };
            var streaming = new Streaming { Size = (nuint)Marshal.SizeOf<Streaming>(), Chunk = .32f, RightContext = 1 };
            var endpoint = new Endpoint { Size = (nuint)Marshal.SizeOf<Endpoint>(), Enable = 1, History = 600 };
            nint b=Copy(backend), m=Copy(model), s=Copy(streaming), e=Copy(endpoint);
            try {
                var config = new Config { Size = (nuint)Marshal.SizeOf<Config>(), Backend=b, Model=m, Streaming=s, Endpoint=e };
                Check(nemo_speech_asr_create(ref config,out _recognizer));
            } finally { Marshal.FreeHGlobal(b); Marshal.FreeHGlobal(m); Marshal.FreeHGlobal(s); Marshal.FreeHGlobal(e); }
            Reset();
        } catch { Dispose(); throw; }
        finally { Marshal.FreeCoTaskMem(path); }
    }
    private static nint Copy<T>(T value) where T:struct { nint p=Marshal.AllocHGlobal(Marshal.SizeOf<T>());Marshal.StructureToPtr(value,p,false);return p; }
    internal void Reset()
    {
        if (_stream != 0) { nemo_speech_asr_stream_close(_stream); _stream=0; }
        var options=nemo_speech_asr_recognition_options_default(); options.Language=_language;options.Interim=1;options.Words=1;options.Punctuation=1;
        Check(nemo_speech_asr_streaming_recognize(_recognizer,ref options,out _stream));
    }
    internal List<Recognition> Push(float[] samples, int rate, bool finish=false)
    {
        if(samples.Length>0) Check(nemo_speech_asr_stream_push_f32(_stream,samples,(nuint)samples.Length,rate));
        if(finish) Check(nemo_speech_asr_stream_finish(_stream));
        var results=new List<Recognition>();
        for(int i=0;i<256;i++) {
            Check(nemo_speech_asr_stream_next(_stream,out nint result));
            if(result==0)return results;
            try {
                string text=Marshal.PtrToStringUTF8(nemo_speech_asr_result_transcript(result,0)) ?? "";
                string language=nemo_speech_asr_result_language_count(result,0)>0
                    ? Marshal.PtrToStringUTF8(nemo_speech_asr_result_language_code(result,0,0)) ?? "auto" : "auto";
                nuint count=nemo_speech_asr_result_word_count(result,0);
                double start=count>0?nemo_speech_asr_result_word_start_time(result,0,0)/1000d:0;
                double end=count>0?nemo_speech_asr_result_word_end_time(result,0,count-1)/1000d:0;
                results.Add(new(text,language,nemo_speech_asr_result_is_final(result)!=0,start,end));
            } finally { nemo_speech_asr_result_destroy(result); }
        }
        throw new InvalidOperationException("Recognition results did not drain.");
    }
    private static void Check(int status) { if(status!=0)throw new InvalidOperationException(Marshal.PtrToStringUTF8(nemo_speech_asr_last_error()) ?? "Native recognition failed."); }
    public void Dispose() { if(_stream!=0)nemo_speech_asr_stream_close(_stream);if(_recognizer!=0)nemo_speech_asr_destroy(_recognizer);_stream=_recognizer=0;Marshal.FreeCoTaskMem(_language); }
    internal sealed record Recognition(string Text,string Language,bool Final,double StartSeconds,double EndSeconds);
    [StructLayout(LayoutKind.Sequential)] private struct Backend { public nuint Size;public int Gpu; }
    [StructLayout(LayoutKind.Sequential)] private struct Model { public nuint Size;public nint Path,Name; }
    [StructLayout(LayoutKind.Sequential)] private struct Streaming { public nuint Size;public float Chunk,Left,Right;public int RightContext; }
    [StructLayout(LayoutKind.Sequential)] private struct Endpoint { public nuint Size;public byte Enable,Vad;public int History; }
    [StructLayout(LayoutKind.Sequential)] private struct Config { public nuint Size;public nint Backend,Model,Streaming,Decoder,Vad,Endpoint,Postproc,Diar,Batching; }
    [StructLayout(LayoutKind.Sequential)] private struct Options {
        public nuint Size; public nint Request,Language;public byte Interim,Words,Punctuation,Verbatim,Profanity;
        public int History;public nint Contexts;public nuint ContextCount;public int Alternatives;public byte Diar;public int Speakers;
    }
    [DllImport("kernel32",CharSet=CharSet.Unicode,SetLastError=true)] private static extern bool SetDllDirectory(string path);
    [DllImport("nemo_speech_asr_c",CallingConvention=CallingConvention.Cdecl)] private static extern int nemo_speech_asr_create(ref Config config,out nint recognizer);
    [DllImport("nemo_speech_asr_c",CallingConvention=CallingConvention.Cdecl)] private static extern void nemo_speech_asr_destroy(nint recognizer);
    [DllImport("nemo_speech_asr_c",CallingConvention=CallingConvention.Cdecl)] private static extern Options nemo_speech_asr_recognition_options_default();
    [DllImport("nemo_speech_asr_c",CallingConvention=CallingConvention.Cdecl)] private static extern int nemo_speech_asr_streaming_recognize(nint recognizer,ref Options options,out nint stream);
    [DllImport("nemo_speech_asr_c",CallingConvention=CallingConvention.Cdecl)] private static extern int nemo_speech_asr_stream_push_f32(nint stream,float[] samples,nuint count,int rate);
    [DllImport("nemo_speech_asr_c",CallingConvention=CallingConvention.Cdecl)] private static extern int nemo_speech_asr_stream_next(nint stream,out nint result);
    [DllImport("nemo_speech_asr_c",CallingConvention=CallingConvention.Cdecl)] private static extern int nemo_speech_asr_stream_finish(nint stream);
    [DllImport("nemo_speech_asr_c",CallingConvention=CallingConvention.Cdecl)] private static extern void nemo_speech_asr_stream_close(nint stream);
    [DllImport("nemo_speech_asr_c",CallingConvention=CallingConvention.Cdecl)] private static extern nint nemo_speech_asr_result_transcript(nint result,nuint alternative);
    [DllImport("nemo_speech_asr_c",CallingConvention=CallingConvention.Cdecl)] private static extern byte nemo_speech_asr_result_is_final(nint result);
    [DllImport("nemo_speech_asr_c",CallingConvention=CallingConvention.Cdecl)] private static extern nuint nemo_speech_asr_result_language_count(nint result,nuint alternative);
    [DllImport("nemo_speech_asr_c",CallingConvention=CallingConvention.Cdecl)] private static extern nuint nemo_speech_asr_result_word_count(nint result,nuint alternative);
    [DllImport("nemo_speech_asr_c",CallingConvention=CallingConvention.Cdecl)] private static extern int nemo_speech_asr_result_word_start_time(nint result,nuint alternative,nuint index);
    [DllImport("nemo_speech_asr_c",CallingConvention=CallingConvention.Cdecl)] private static extern int nemo_speech_asr_result_word_end_time(nint result,nuint alternative,nuint index);
    [DllImport("nemo_speech_asr_c",CallingConvention=CallingConvention.Cdecl)] private static extern nint nemo_speech_asr_result_language_code(nint result,nuint alternative,nuint index);
    [DllImport("nemo_speech_asr_c",CallingConvention=CallingConvention.Cdecl)] private static extern void nemo_speech_asr_result_destroy(nint result);
    [DllImport("nemo_speech_asr_c",CallingConvention=CallingConvention.Cdecl)] private static extern nint nemo_speech_asr_last_error();
}

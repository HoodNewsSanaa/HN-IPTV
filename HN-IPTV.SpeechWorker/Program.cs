using System.Text.Json;
using System.Text;

Console.InputEncoding=Console.OutputEncoding=new UTF8Encoding(false);
if(args.Length!=5)return 2;
// No network libraries, cloud credentials, telemetry, or user-content logging in this process.
try {
    using var recognizer=new NativeRecognizer(args[0],args[1],args[2],args[3]!="cpu",int.Parse(args[4],System.Globalization.CultureInfo.InvariantCulture));
    Console.WriteLine("{\"ready\":true}");
    string? line;
    while((line=Console.ReadLine())!=null) {
        long id=0;
        try {
            using var doc=JsonDocument.Parse(line);var request=doc.RootElement;id=request.GetProperty("id").GetInt64();
            string kind=request.GetProperty("kind").GetString() ?? "";
            List<NativeRecognizer.Recognition> results=[];
            if(kind=="reset")recognizer.Reset();
            else if(kind=="audio" || kind=="finish") {
                byte[] pcm=Convert.FromBase64String(request.GetProperty("data").GetString() ?? "");
                if(pcm.Length%4!=0 || pcm.Length>176400*5)throw new InvalidDataException("Invalid audio packet.");
                var mono=new float[pcm.Length/4];
                for(int i=0;i<mono.Length;i++)mono[i]=(BitConverter.ToInt16(pcm,i*4)+(float)BitConverter.ToInt16(pcm,i*4+2))/65536f;
                results=await recognizer.Push(mono,44100,kind=="finish");
            } else throw new InvalidDataException("Unsupported recognition request.");
            Console.WriteLine(JsonSerializer.Serialize(new {id,results}));
        } catch { Console.WriteLine(JsonSerializer.Serialize(new {id,error="Whisper recognition failed. Prepare the model again or select CPU."})); }
    }
    return 0;
} catch { Console.WriteLine("{\"ready\":false,\"error\":\"Whisper could not load. Prepare its runtime and model, or select CPU.\"}");return 1; }

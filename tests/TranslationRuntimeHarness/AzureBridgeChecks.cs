using System.Reflection;
using System.Net.Http;
using System.IO;
using System.Text.Json;
using HnIptv.Translation;

internal static class AzureBridgeChecks
{
    public static async Task RunAsync(System.Reflection.Assembly assembly, string python, TranslationSettings settings)
    {
        string image=Path.Combine(Path.GetDirectoryName(python)!, "..", "azure-bridge-fixture.png");
        var info=new System.Diagnostics.ProcessStartInfo(python) {
            UseShellExecute=false,CreateNoWindow=true,WindowStyle=System.Diagnostics.ProcessWindowStyle.Hidden,
            RedirectStandardError=true,RedirectStandardOutput=true
        };
        info.ArgumentList.Add("-c");
        info.ArgumentList.Add("from PIL import Image,ImageDraw,ImageFont; import sys; im=Image.new('RGB',(1000,260),'white'); ImageDraw.Draw(im).text((50,70),'Hello world',fill='black',font=ImageFont.truetype('C:/Windows/Fonts/arial.ttf',72)); im.save(sys.argv[1])");
        info.ArgumentList.Add(image);
        using(var generator=System.Diagnostics.Process.Start(info)!) {
            await generator.WaitForExitAsync();
            if(generator.ExitCode!=0)throw new Exception("Could not generate Azure OCR fixture.");
        }
        int calls=0;bool deny=false;
        using var http=new HttpClient(new Handler(async (request,ct)=>{
            calls++;
            if(request.RequestUri!.Host!="api.cognitive.microsofttranslator.com")throw new Exception("Unexpected Azure endpoint.");
            if(request.Headers.GetValues("Ocp-Apim-Subscription-Key").Single()!="synthetic-bridge-key")throw new Exception("Missing header.");
            using var input=JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            if(!input.RootElement[0].GetProperty("Text").GetString()!.Contains("world",StringComparison.OrdinalIgnoreCase))
                throw new Exception("The bridge sent something other than OCR text.");
            return new HttpResponseMessage(deny?System.Net.HttpStatusCode.Unauthorized:System.Net.HttpStatusCode.OK) {
                Content=new StringContent(deny?"must not echo synthetic-bridge-key":"[{\"translations\":[{\"text\":\"Translated Azure fixture\"}]}]")
            };
        }));
        var client=new AzureTranslatorClient("synthetic-bridge-key","global",http);
        var type=assembly.GetType("HnIptv.LocalTranslationWorker",true)!;
        var selected=settings with {Provider="azure",InputLanguage="en",OutputLanguage="es"};
        var worker=(IAsyncDisposable)Activator.CreateInstance(type,BindingFlags.Instance|BindingFlags.NonPublic,null,new object[]{selected,client},null)!;
        try {
            var method=type.GetMethod("TranslateAsync",BindingFlags.Instance|BindingFlags.NonPublic)!;
            async Task<TranslationResult> Request(TranslationSettings value)=>
                await (Task<TranslationResult>)method.Invoke(worker,new object?[]{"screen",File.ReadAllBytes(image),value,CancellationToken.None,0d,null})!;
            var result=await Request(selected);
            if(result.Error!=null || result.Text!="Translated Azure fixture" || !result.Backend.Contains("Azure") || result.Regions?.Count!=1)
                throw new Exception("C# Azure bridge did not preserve the translated OCR result and geometry.");
            int firstCalls=calls;
            await Request(selected);
            if(calls!=firstCalls)throw new Exception("Repeated screen text consumed Azure requests twice.");
            var reset=await (Task<TranslationResult>)method.Invoke(worker,new object?[]{"reset",Array.Empty<byte>(),selected,CancellationToken.None,0d,null})!;
            if(reset.Error!=null || !(bool)type.GetProperty("CanReuse",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(worker)!)
                throw new Exception("An idle worker could not be reset for channel reuse.");
            await Request(selected);
            if(calls!=firstCalls)throw new Exception("Warm worker reset discarded the Azure text cache.");
            deny=true;
            result=await Request(selected with {OutputLanguage="fr"});
            if(result.Error==null || !result.Error.Contains("key",StringComparison.OrdinalIgnoreCase) || result.Error.Contains("synthetic-bridge-key"))
                throw new Exception("Azure authentication failure was not sanitized and returned to the app.");
            Console.WriteLine("Actual offline OCR -> C# Azure HTTP bridge -> positioned result, caching and sanitized failure checks passed.");
        }
        finally {await worker.DisposeAsync();File.Delete(image);}
    }
    private sealed class Handler(Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> respond):HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)=>respond(request,ct);
    }
}


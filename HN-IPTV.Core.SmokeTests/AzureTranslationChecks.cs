using System.Net;
using System.Text;
using System.Text.Json;
using HnIptv.Translation;

static class AzureTranslationChecks
{
    public static async Task RunAsync()
    {
        int calls=0;
        using var http = new HttpClient(new Handler(async (request,ct) => {
            calls++;
            Require(request.RequestUri!.Scheme=="https" && request.RequestUri.Host=="api.cognitive.microsofttranslator.com","fixed HTTPS Azure host");
            Require(!request.RequestUri.Query.Contains("synthetic-key"),"key is not in URL");
            Require(request.Headers.GetValues("Ocp-Apim-Subscription-Key").Single()=="synthetic-key","key header");
            Require(request.Headers.GetValues("Ocp-Apim-Subscription-Region").Single()=="eastus","region header");
            Require(request.RequestUri.Query.Contains("to=zh-Hant") && request.RequestUri.Query.Contains("from=en"),"language mapping");
            using var json=JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            Require(json.RootElement[0].GetProperty("Text").GetString()=="Hello","plain text payload");
            return Json("[{\"translations\":[{\"text\":\"translated fixture\"}]}]");
        }));
        var client=new AzureTranslatorClient("synthetic-key","EastUS",http);
        Require(await client.TranslateAsync("Hello","en","zt")=="translated fixture","successful translation");
        Require(await client.TranslateAsync("Hello","en","zt")=="translated fixture" && calls==1,"cached words do not use quota twice");
        Require(await client.TranslateAsync("same","en","en")=="same" && calls==1,"same-language text stays local");
        foreach(var status in new[]{HttpStatusCode.Unauthorized,HttpStatusCode.Forbidden,HttpStatusCode.TooManyRequests}) {
            int rejected=0;
            using var errorHttp=new HttpClient(new Handler((r,c)=>{rejected++;return Task.FromResult(new HttpResponseMessage(status){Content=new StringContent("Never expose synthetic-key or provider response bodies")});}));
            var errorClient=new AzureTranslatorClient("synthetic-key","global",errorHttp);
            for(int i=0;i<2;i++) try {
                await errorClient.TranslateAsync("Hello","en","es");
                throw new Exception("Expected Azure rejection.");
            } catch(InvalidOperationException ex) {
                Require(!ex.Message.Contains("synthetic-key") && !ex.Message.Contains("provider response bodies"),"sanitized service errors");
            }
            Require(rejected==1,"auth/quota/rate limits do not trigger repeated HTTP calls");
        }
        using var globalHttp=new HttpClient(new Handler((r,c)=>{
            Require(!r.Headers.Contains("Ocp-Apim-Subscription-Region"),"global resource omits region");
            return Task.FromResult(Json("not json"));
        }));
        try { await new AzureTranslatorClient("synthetic-key","global",globalHttp).TranslateAsync("Hello","en","es"); throw new Exception("Expected invalid JSON failure."); }
        catch(InvalidOperationException ex) { Require(ex.Message.Contains("invalid translation response"),"malformed response is handled"); }
        using var canceled=new CancellationTokenSource(); canceled.Cancel();
        try { await client.TranslateAsync("uncached","en","es",canceled.Token); throw new Exception("Expected cancellation."); }
        catch(OperationCanceledException) { }
        var settings=new TranslationSettings { Provider="azure",AzureRegion="EastUS" }.Normalize();
        Require(settings.Provider=="azure" && settings.AzureRegion=="eastus","provider settings normalize");
        Require(new TranslationSettings().Provider=="azure","Nemotron uses Azure text translation");
        Require(!JsonSerializer.Serialize(settings).Contains("synthetic-key"),"settings contain no API key");
        Console.WriteLine("Azure request authentication, regional/global routing, caching, errors, cancellation and settings checks passed.");
    }
    private static void Require(bool ok,string name) { if(!ok)throw new Exception("Azure regression: "+name); }
    private static HttpResponseMessage Json(string value)=>new(HttpStatusCode.OK){Content=new StringContent(value,Encoding.UTF8,"application/json")};
    private sealed class Handler(Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> respond):HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)=>respond(request,ct);
    }
}


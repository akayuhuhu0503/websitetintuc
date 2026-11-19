using System.Net;
using System.Net.Sockets;
using System.Diagnostics;
using System.Text.Json;
using LandPortal.MigrationCli;

if(args.Contains("--self-test"))
{
    void Check(bool ok) { if(!ok)throw new Exception("Diagnostic assertion failed"); }
    Check(SourceTransportException.Classify(new HttpRequestException(HttpRequestError.NameResolutionError,"secret"))=="DNS");
    Check(SourceTransportException.Classify(new HttpRequestException("secret",new SourceTransportException("TCP")))=="TCP");
    Check(SourceTransportException.Classify(new HttpRequestException("secret",new System.Security.Authentication.AuthenticationException("secret")))=="TLS");
    Check(SourceTransportException.Classify(new HttpRequestException("secret",new InvalidDataException("secret")))=="NETWORK_POLICY");
    Check(SourceTransportException.Classify(new HttpRequestException(HttpRequestError.ResponseEnded,"secret"))=="HTTP_RESPONSE");
    Check(SourceTransportException.Classify(new OperationCanceledException())=="TIMEOUT_OR_CANCELLED");
    Check(!new SourceTransportException("DNS",new Exception("secret"),"robots.txt").SafeSummary.Contains("secret"));
    Check(!SafeSourceClient.IsPublicAddress(IPAddress.Parse("127.0.0.1")) && !SafeSourceClient.IsPublicAddress(IPAddress.Parse("10.0.0.1")));
    Check(SafeSourceClient.IsPublicAddress(IPAddress.Parse("103.9.86.152")));
    Console.WriteLine("PASS 9 diagnostic assertions; no network/SQL");return;
}
// No configuration, cookies, credentials, SQL or response body is read/exported.
// Run under the operator identity; this is not proof of app-pool network access.
var facts=new List<object>();var target=new Uri("https://qldd.mae.gov.vn/robots.txt");
IPAddress[] addresses;
try
{
    using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(20));
    addresses=await Dns.GetHostAddressesAsync(target.Host,timeout.Token);
    facts.Add(new{stage="DNS",addresses=addresses.Select(x=>new{ip=x.ToString(),family=x.AddressFamily.ToString(),isPublic=SafeSourceClient.IsPublicAddress(x)})});
    if(addresses.Length==0||addresses.Any(x=>!SafeSourceClient.IsPublicAddress(x)))throw new InvalidDataException();
}
catch(Exception e){facts.Add(new{stage="DNS_POLICY",error=SourceTransportException.Classify(e)});Print();return;}
foreach(var address in addresses.Take(4))
{
    var clock=Stopwatch.StartNew();using var socket=new Socket(address.AddressFamily,SocketType.Stream,ProtocolType.Tcp);
    try {using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(5));await socket.ConnectAsync(new IPEndPoint(address,443),timeout.Token);facts.Add(new{stage="TCP443",ip=address.ToString(),ok=true,ms=clock.ElapsedMilliseconds});}
    catch(Exception e){facts.Add(new{stage="TCP443",ip=address.ToString(),ok=false,type=e.GetType().Name,socketError=(e as SocketException)?.SocketErrorCode.ToString(),ms=clock.ElapsedMilliseconds});}
}
using(var source=new SafeSourceClient(5))
{
    var clock=Stopwatch.StartNew();
    try {using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(60));var result=await source.FetchAsync(target.AbsoluteUri,128*1024,timeout.Token,true);facts.Add(new{stage="SAFE_CLIENT_ROBOTS",status=result.Status,host=new Uri(result.Url).Host,requests=source.RequestsUsed,ms=clock.ElapsedMilliseconds});}
    catch(Exception e){facts.Add(new{stage="SAFE_CLIENT_ROBOTS",error=SourceTransportException.Classify(e),requests=source.RequestsUsed,ms=clock.ElapsedMilliseconds});}
}
// Explicit diagnostic comparison only, never enabled in the application.
// No redirect following, default credentials, cookies or custom TLS validation.
if(args.Contains("--compare-system-proxy"))
{
    try
    {
        var proxy=HttpClient.DefaultProxy;var route=proxy.GetProxy(target);
        var usesProxy=!proxy.IsBypassed(target)&&route!=null&&route!=target;
        facts.Add(new{stage="SYSTEM_PROXY",usesProxy});
        using var handler=new SocketsHttpHandler{UseProxy=true,AllowAutoRedirect=false,UseCookies=false,ConnectTimeout=TimeSpan.FromSeconds(20)};
        using var client=new HttpClient(handler){Timeout=TimeSpan.FromSeconds(60)};
        client.DefaultRequestHeaders.UserAgent.ParseAdd("LandPortalMigration/0.1 (bounded public content migration)");
        using var response=await client.GetAsync(target,HttpCompletionOption.ResponseHeadersRead);
        facts.Add(new{stage="SYSTEM_PROXY_ROBOTS",status=(int)response.StatusCode,redirect=response.Headers.Location!=null});
    }
    catch(Exception e){facts.Add(new{stage="SYSTEM_PROXY_ROBOTS",error=SourceTransportException.Classify(e)});}
}
Print();
void Print()=>Console.WriteLine(JsonSerializer.Serialize(new{utc=DateTimeOffset.UtcNow,identityScope="current operator, not IIS app pool",sqlTouched=false,facts},new JsonSerializerOptions{WriteIndented=true}));

using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
namespace LandPortal.Tests;
// Real, project-owned loopback SMTP server. Raw mails are private ignored artifacts.
public sealed class LocalMailSink : IAsyncDisposable
{
    private readonly TcpListener listener=new(IPAddress.Loopback,0);
    private readonly CancellationTokenSource stop=new();
    private readonly Task running;
    private readonly string directory;
    public readonly ConcurrentDictionary<Guid,string> Bodies=new();
    public bool DropAcceptance { get; set; }
    public int Port=>((IPEndPoint)listener.LocalEndpoint).Port;
    public LocalMailSink(string directory){this.directory=directory;Directory.CreateDirectory(directory);listener.Start();running=Listen();}
    private async Task Listen()
    {
        var clients=new List<Task>();
        try{while(!stop.IsCancellationRequested){var client=await listener.AcceptTcpClientAsync(stop.Token);clients.Add(Handle(client));}}
        catch(OperationCanceledException){}catch(SocketException)when(stop.IsCancellationRequested){}
        await Task.WhenAll(clients);
    }
    private async Task Handle(TcpClient client)
    {
        using(client)
        {
            using var stream=client.GetStream();using var reader=new StreamReader(stream,Encoding.UTF8);using var writer=new StreamWriter(stream,new UTF8Encoding(false)){NewLine="\r\n",AutoFlush=true};
            await writer.WriteLineAsync("220 LandPortal Test mail sink");
            try
            {
                while(!stop.IsCancellationRequested)
                {
                    var command=await reader.ReadLineAsync(stop.Token);if(command==null)break;
                    if(command.StartsWith("EHLO")||command.StartsWith("MAIL FROM:")||command.StartsWith("RCPT TO:"))await writer.WriteLineAsync("250 OK");
                    else if(command=="DATA")
                    {
                        await writer.WriteLineAsync("354 End with dot");var raw=new StringBuilder();string? line;
                        while((line=await reader.ReadLineAsync(stop.Token))!=null&&line!=".")raw.Append(line).Append("\r\n");
                        var text=raw.ToString();var idLine=text.Split("\r\n").Single(x=>x.StartsWith("Message-ID: "));var id=Guid.Parse(idLine.Split('<')[1].Split('@')[0]);
                        await File.WriteAllTextAsync(Path.Combine(directory,id+".eml"),text);
                        var encoded=text.Split("\r\n\r\n",2)[1].Trim();Bodies[id]=Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
                        if(DropAcceptance)break;await writer.WriteLineAsync("250 Accepted to local Test sink");
                    }
                    else if(command=="QUIT"){await writer.WriteLineAsync("221 Bye");break;}else await writer.WriteLineAsync("500 Unsupported");
                }
            }catch(OperationCanceledException){}catch(IOException){}
        }
    }
    public async ValueTask DisposeAsync(){stop.Cancel();listener.Stop();await running;stop.Dispose();}
}

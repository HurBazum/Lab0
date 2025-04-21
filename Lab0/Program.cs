using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;

string host = "cs144.github.io";
string path = "/lab0";

var result = await WebGet(host, path);

Console.WriteLine(result);

async Task<string> WebGet(string host, string path)
{
    var ip = await Dns.GetHostEntryAsync(host);
    var address = ip.AddressList[0];
    IPEndPoint iPEndPoint = new(address, 80);

    using Socket socket = new(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);

    await socket.ConnectAsync(iPEndPoint);

    string request = $"GET {path} HTTP/1.0\r\nHost: {host}\r\n\r\n";
    byte[] requestBytes = Encoding.UTF8.GetBytes(request);

    await socket.SendAsync(requestBytes);

    bool okStatus = false;
    StringBuilder responseBuilder = new();
    byte[] buffer = new byte[1024];

    while(true)
    {
        var received = await socket.ReceiveAsync(buffer);

        if(received == 0)
        {
            break;
        }

        var chunk = Encoding.UTF8.GetString(buffer, 0, received);

        if(chunk.Contains("200 OK"))
        {
            okStatus = true;
        }

        responseBuilder.Append(chunk);
    }

    if(!okStatus)
    {
        foreach(var line in responseBuilder.ToString().Split("\r\n"))
        {
            if(line.StartsWith("Location: "))
            {
                responseBuilder = await RedirectToHttps(line["Location: ".Length..]);
                break;
            }
            if(line.StartsWith("<HTML>"))
            {
                break;
            }
        }
    }

    return responseBuilder.ToString();
}


async Task<StringBuilder> RedirectToHttps(string url)
{
    StringBuilder responseBuilder = new();

    if(url.StartsWith("https://"))
    {
        Uri uri = new(url);

        var ip = await Dns.GetHostEntryAsync(uri.Host);
        var address = ip.AddressList[0];
        var endPoint = new IPEndPoint(address, 443);

        using Socket socket = new(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);

        await socket.ConnectAsync(endPoint);

        //
        using SslStream stream = new(new NetworkStream(socket, true));
        await stream.AuthenticateAsClientAsync(uri.Host);

        string request = $"GET {uri.PathAndQuery} HTTP/1.1\r\nHost: {uri.Host}\r\n\r\n";

        var requestBytes = Encoding.UTF8.GetBytes(request);

        await stream.WriteAsync(requestBytes);
        await stream.FlushAsync();

        byte[] buffer = new byte[4096];
        int len = -1;
        int realLen = 0;
        while(len < realLen)
        {
            int readBytes = await stream.ReadAsync(buffer.AsMemory());

            if(readBytes == 0)
            {
                break;
            }

            var chunk = Encoding.UTF8.GetString(buffer, 0, readBytes);

            responseBuilder.Append(chunk);

            len += readBytes;
            if(realLen == 0)
            {
                foreach(var line in responseBuilder.ToString().Split("\r\n"))
                {
                    if(line.StartsWith("Content-Length: "))
                    {
                        realLen = int.Parse(line["Content-Length: ".Length..]);
                        break;
                    }
                }
            }
        }
    }

    return responseBuilder;
}
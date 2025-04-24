using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;

string host = "canvas-gateway.stanford.edu";
string path = "/goCanvas.html";

try
{
    var result = await WebGet(host, path);

    Console.WriteLine(result);
}
catch(Exception ex)
{
    Console.WriteLine($"Ошибка: {ex.Message}");
}


async Task<string> WebGet(string host, string path)
{
    var ip = await Dns.GetHostEntryAsync(host);
    if(ip.AddressList.Length == 0)
    {
        throw new Exception($"Хост {host} не существует");
    }
    var address = ip.AddressList[0];
    IPEndPoint iPEndPoint = new(address, 80);

    using Socket socket = new(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);

    await socket.ConnectAsync(iPEndPoint);

    path = (path.StartsWith('/')) ? path : $"/{path}";

    string request = $"GET {path} HTTP/1.0\r\nHost: {host}\r\n\r\n";
    byte[] requestBytes = Encoding.UTF8.GetBytes(request);

    int check = await socket.SendAsync(requestBytes);
    if(check != requestBytes.Length)
    {
        throw new Exception("Не получилось отправить запрос");
    }

    StringBuilder responseBuilder = new();
    byte[] buffer = new byte[4096];
    bool headersRead = false;

    while(true)
    {
        var received = await socket.ReceiveAsync(buffer);

        if(received == 0)
        {
            break;
        }

        var chunk = Encoding.UTF8.GetString(buffer, 0, received);
        
        responseBuilder.Append(chunk);
    }

    var response = responseBuilder.ToString();

    var statusCode = ReturnStatusCode(response);

    if(statusCode == "301" || statusCode == "307")
    {
        Console.WriteLine($"запрос был перенаправлен из-за статус кода {statusCode}");
        string newUrl = GetLocationFromHeaders(response, ref headersRead);

        response = await RedirectToHttps(newUrl);
    }

    return response;
}


async Task<string> RedirectToHttps(string url)
{
    StringBuilder responseBuilder = new();

    if(url.StartsWith("https://"))
    {
        Uri uri = new(url);

        var ip = await Dns.GetHostEntryAsync(uri.Host);
        if(ip.AddressList.Length == 0)
        {
            throw new Exception($"Не удалось разрешить хост {host}");
        }
        var address = ip.AddressList.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork) ?? ip.AddressList[0];
        var endPoint = new IPEndPoint(address, 443);

        using Socket socket = new(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);

        await socket.ConnectAsync(endPoint);

        //
        using SslStream stream = new(new NetworkStream(socket, true));
        try
        {
            await stream.AuthenticateAsClientAsync(uri.Host);
        }
        catch(AuthenticationException ex)
        {
            Console.WriteLine($"TLS error: {ex.Message}");
        }

        string request = $"GET {uri.PathAndQuery} HTTP/1.1\r\nHost: {uri.Host}\r\nConnection: close\r\n\r\n";

        var requestBytes = Encoding.UTF8.GetBytes(request);

        try
        {
            await stream.WriteAsync(requestBytes);
            await stream.FlushAsync();
        }
        catch(IOException ex)
        {
            Console.WriteLine(ex.Message);
        }

        byte[] buffer = new byte[4096];
        int len = 0;
        int realLen = 0;
        bool headersRead = false;
        while(true)
        {
            int readBytes = await stream.ReadAsync(buffer.AsMemory());

            if(readBytes == 0)
            {
                break;
            }

            var chunk = Encoding.UTF8.GetString(buffer, 0, readBytes);

            responseBuilder.Append(chunk);


            if(!headersRead)
            {
                realLen = GetContentLengthFromHeaders(responseBuilder.ToString(), ref headersRead);
            }

            if(headersRead == true && realLen > 0)
            {
                len += readBytes;
                if(len > realLen)
                {
                    break;
                }
            }
        }
    }

    return responseBuilder.ToString();
}

string ReturnStatusCode(string response)
{
    int firstLineEnd = response.IndexOf("\r\n");

    if(firstLineEnd == -1)
    {
        return "Unknown";
    }
    else
    {
        var parts = response[..firstLineEnd].Split(" ");

        if(parts.Length < 2)
        {
            return "Unknown";
        }
        else
        {
            return parts[1];
        }
    }
}

string GetLocationFromHeaders(string response, ref bool headerRead)
{
    if(!response.Contains("\r\n\r\n"))
    {
        return string.Empty;
    }
    else
    {
        int eoh = response.IndexOf("\r\n\r\n");
        string result = string.Empty;
        foreach(var line in response[..eoh].Split("\r\n"))
        {
            if(line.StartsWith("Location", StringComparison.OrdinalIgnoreCase))
            {
                result = line["Location: ".Length..];
                break;
            }
        }
        headerRead = true;
        return result;
    }
}

int GetContentLengthFromHeaders(string response, ref bool headersRead)
{
    if(!response.Contains("\r\n\r\n"))
    {
        return 0;
    }
    else
    {
        int eoh = response.IndexOf("\r\n\r\n");
        int result = 0;
        foreach(var line in response[..eoh].Split("\r\n"))
        {
            if(line.StartsWith("Content-Length", StringComparison.OrdinalIgnoreCase))
            {
                if(int.TryParse(line["Content-Length: ".Length..], out result))
                {
                    Console.WriteLine($"\"Content-Length\" был прочитан корректно");
                }
                else
                {
                    throw new Exception($"\"Content-Length\" был прочитан некорректно");
                }
                headersRead = true;
                break;
            }
        }
        return result;
    }
}
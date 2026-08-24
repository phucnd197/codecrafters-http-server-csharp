using System.Buffers;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using System.Text;



// You can use print statements as follows for debugging, they'll be visible when running tests.
Console.WriteLine("Logs from your program will appear here!");
var directoryArgStart = Array.IndexOf(args, "--directory");
var initialFilePath = directoryArgStart != -1 && args.Length > directoryArgStart + 1 ? args[directoryArgStart + 1] : string.Empty;

// TODO: Uncomment the code below to pass the first stage
using TcpListener server = new(IPAddress.Any, 4221);
server.Start();
byte[] headerDelimiter = "\r\n\r\n"u8.ToArray();
byte[] requestLineSelimiter = "\r\n"u8.ToArray();
const long MaxHeaderSize = 8 * 1024;
while (true)
{
    var socket = await server.AcceptSocketAsync(); // wait for client
    _ = Task.Run(() => ProcessClientAsync(socket));
}

async Task ProcessClientAsync(Socket socket)
{
    using (socket)
    {
        using var networkStream = new NetworkStream(socket);
        var reader = PipeReader.Create(networkStream);
        var (requestLine, header) = await ReadPayloadAsync(headerDelimiter, requestLineSelimiter, MaxHeaderSize, reader);

        if (requestLine is null)
        {
            throw new InvalidOperationException("Missing request information");
        }

        var (method, path, _) = ParseRequestLine(requestLine);
        var headers = ParseHeaders(header);
        if (method == "GET")
        {
            await HandleGetRequest(networkStream, path, headers);
            return;
        }

        await networkStream.WriteAsync(Encoding.UTF8.GetBytes("HTTP/1.1 405 Method Not Allowed\r\n\r\n"));
    }
}

async Task HandleGetRequest(NetworkStream networkStream, string path, Dictionary<string, List<string>> headers)
{
    if (path == "/")
    {
        await networkStream.WriteAsync(Encoding.UTF8.GetBytes(GetResponse([])));
        return;
    }

    if (path.StartsWith("/echo"))
    {
        var rest = path.Length > 6 ? path.AsSpan().Slice(6) : string.Empty;
        //return $"HTTP/1.1 200 OK\r\nContent-Type: text/plain\r\nContent-Length: {rest.Length}\r\n\r\n{rest}";
        await networkStream.WriteAsync(Encoding.UTF8.GetBytes(GetResponse(rest, contentType: "text/plain")));
        return;
    }

    if (path.StartsWith("/user-agent"))
    {
        var userAgent = headers.GetValueOrDefault("user-agent")?.FirstOrDefault() ?? string.Empty;
        //return $"HTTP/1.1 200 OK\r\nContent-Type: text/plain\r\nContent-Length: {userAgent.Length}\r\n\r\n{userAgent}";
        await networkStream.WriteAsync(Encoding.UTF8.GetBytes(GetResponse(userAgent, contentType: "text/plain")));
        return;
    }

    if (path.StartsWith("/files"))
    {
        var fileName = path.Length > 7 ? path.Substring(7) : string.Empty;
        if (path.Contains(".."))
        {
            await networkStream.WriteAsync(Encoding.UTF8.GetBytes(GetResponse([], HttpStatusCode.BadRequest)));
            return;
        }

        var fullPath = Path.Combine(initialFilePath, fileName);
        if (!File.Exists(fullPath))
        {
            await networkStream.WriteAsync(Encoding.UTF8.GetBytes(GetResponse([], HttpStatusCode.NotFound, "Not Found")));
            return;
        }

        var fileInfo = new FileInfo(fullPath);
        await networkStream.WriteAsync(Encoding.UTF8.GetBytes(GetResponse([], contentType: "application/octet-stream", contentLength: fileInfo.Length)));
        using var fileStream = File.OpenRead(fullPath);
        await fileStream.CopyToAsync(networkStream);
    }
}

static string GetResponse(ReadOnlySpan<char> content, HttpStatusCode? status = default, string? statusCode = default, string? contentType = default, long? contentLength = default)
{
    var contentTypeHeader = contentType is not null ? $"\r\nContent-Type: {contentType}" : string.Empty;
    return content.Length == 0
        ? $"HTTP/1.1 {(int)(status ?? HttpStatusCode.OK)} {status?.ToString() ?? statusCode ?? "OK"}{contentType}\r\n\r\n"
        : $"HTTP/1.1 {(int)(status ?? HttpStatusCode.OK)} {status?.ToString() ?? statusCode ?? "OK"}{contentType}\r\nContent-Length: {contentLength ?? content.Length}\r\n\r\n{content}";
}

static RequestLine ParseRequestLine(string requestLine)
{
    var components = requestLine.Split(' ');
    return new RequestLine(components[0], components[1], components[2]);
}

static Dictionary<string, List<string>> ParseHeaders(string? header)
{
    if (string.IsNullOrEmpty(header))
    {
        return [];
    }
    var headerRaws = header.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
    var headers = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
    foreach (var headerRaw in headerRaws)
    {
        var component = headerRaw.Split(":", 2, StringSplitOptions.TrimEntries);
        if (!headers.TryGetValue(component[0], out var values))
        {
            headers[component[0].ToLower()] = values = [];
        }
        values.Add(component[1]);
    }
    return headers;
}


static async Task<(string?, string?)> ReadPayloadAsync(byte[] headerDelimiter, byte[] requestLineSelimiter, long MaxHeaderSize, PipeReader reader)
{
    string? header = null;
    string? requestLine = null;
    while (true)
    {
        var result = await reader.ReadAsync();
        var buffer = result.Buffer;
        var sequenceReader = new SequenceReader<byte>(buffer);

        if (requestLine is null && sequenceReader.TryReadTo(out ReadOnlySequence<byte> requestLineSpan, requestLineSelimiter, true))
        {
            requestLine = Encoding.UTF8.GetString(requestLineSpan);
            reader.AdvanceTo(sequenceReader.Position);
            continue;
        }
        else if (header is null && sequenceReader.TryReadTo(out ReadOnlySequence<byte> headerSpan, headerDelimiter, true))
        {
            header = Encoding.UTF8.GetString(headerSpan);
            reader.AdvanceTo(sequenceReader.Position);
            break;
        }

        if (buffer.Length > MaxHeaderSize)
        {
            throw new InvalidOperationException("");
        }

        reader.AdvanceTo(buffer.Start, buffer.End);


        if (result.IsCompleted)
        {
            break;
        }
    }
    return (requestLine, header);
}

record struct RequestLine(string Method, string Path, string? Protocol);
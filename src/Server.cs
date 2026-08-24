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
        var (requestLine, headers, body) = await ReadPayloadAsync(headerDelimiter, requestLineSelimiter, MaxHeaderSize, reader);

        if (requestLine is null)
        {
            throw new InvalidOperationException("Missing request information");
        }

        var (method, path, _) = requestLine.Value;
        if (method == "GET")
        {
            await HandleGetRequest(networkStream, path, headers);
            return;
        }
        else if (method == "POST")
        {
            await HandlePostRequest(networkStream, path, headers, body);
            return;
        }

        await networkStream.WriteAsync(Encoding.UTF8.GetBytes(GetEmptyResponse(HttpStatusCode.MethodNotAllowed, "Method Not Allowed")));
    }
}

async Task HandleGetRequest(NetworkStream networkStream, string path, Dictionary<string, List<string>>? headers)
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
        var userAgent = headers?.GetValueOrDefault("user-agent")?.FirstOrDefault() ?? string.Empty;
        //return $"HTTP/1.1 200 OK\r\nContent-Type: text/plain\r\nContent-Length: {userAgent.Length}\r\n\r\n{userAgent}";
        await networkStream.WriteAsync(Encoding.UTF8.GetBytes(GetResponse(userAgent, contentType: "text/plain")));
        return;
    }

    if (path.StartsWith("/files"))
    {
        var fileName = path.Length > 7 ? path.Substring(7) : string.Empty;
        if (path.Contains(".."))
        {
            await networkStream.WriteAsync(Encoding.UTF8.GetBytes(GetEmptyResponse(HttpStatusCode.BadRequest)));
            return;
        }

        var fullPath = Path.Combine(initialFilePath, fileName);
        if (!File.Exists(fullPath))
        {
            await networkStream.WriteAsync(Encoding.UTF8.GetBytes(GetEmptyResponse(HttpStatusCode.NotFound, "Not Found")));
            return;
        }

        var fileInfo = new FileInfo(fullPath);
        await networkStream.WriteAsync(Encoding.UTF8.GetBytes(GetEmptyResponse(contentType: "application/octet-stream", contentLength: fileInfo.Length)));
        using var fileStream = File.OpenRead(fullPath);
        await fileStream.CopyToAsync(networkStream);
    }

    await networkStream.WriteAsync(Encoding.UTF8.GetBytes(GetEmptyResponse(HttpStatusCode.NotFound, "Not Found")));
}

async Task HandlePostRequest(NetworkStream networkStream, string path, Dictionary<string, List<string>>? headers, ReadOnlySequence<byte>? body)
{
    if (path.StartsWith("/files"))
    {
        if (body is null)
        {
            await networkStream.WriteAsync(Encoding.UTF8.GetBytes(GetEmptyResponse(HttpStatusCode.BadRequest)));
            return;
        }
        var fileName = path.Length > 7 ? path.Substring(7) : string.Empty;
        if (path.Contains(".."))
        {
            await networkStream.WriteAsync(Encoding.UTF8.GetBytes(GetEmptyResponse(HttpStatusCode.BadRequest)));
            return;
        }

        var fullPath = Path.Combine(initialFilePath, fileName);
        if (File.Exists(fullPath))
        {
            await networkStream.WriteAsync(Encoding.UTF8.GetBytes(GetEmptyResponse(HttpStatusCode.UnprocessableEntity, "File existed")));
        }

        using var fileStream = File.OpenWrite(fullPath);
        await fileStream.WriteAsync(body.Value.ToArray());
        await networkStream.WriteAsync(Encoding.UTF8.GetBytes(GetEmptyResponse(HttpStatusCode.NoContent, "Created")));
    }
}

static string GetEmptyResponse(HttpStatusCode? status = default, string? statusCode = default, string? contentType = default, long? contentLength = default)
{
    var contentTypeHeader = contentType is not null ? $"\r\nContent-Type: {contentType}" : string.Empty;
    return $"HTTP/1.1 {(int)(status ?? HttpStatusCode.OK)} {statusCode ?? status?.ToString() ?? "OK"}{contentTypeHeader}\r\n\r\n";
}

static string GetResponse(ReadOnlySpan<char> content, HttpStatusCode? status = default, string? statusCode = default, string? contentType = default, long? contentLength = default)
{
    var contentTypeHeader = contentType is not null ? $"\r\nContent-Type: {contentType}" : string.Empty;
    return $"HTTP/1.1 {(int)(status ?? HttpStatusCode.OK)} {statusCode ?? status?.ToString() ?? "OK"}{contentTypeHeader}\r\nContent-Length: {contentLength ?? content.Length}\r\n\r\n{content}";
}


static async Task<Request> ReadPayloadAsync(byte[] headerDelimiter, byte[] requestLineSelimiter, long MaxHeaderSize, PipeReader reader)
{
    Dictionary<string, List<string>>? headers = null;
    RequestLine? requestLine = null;
    ReadOnlySequence<byte>? body = null;
    var contentLength = 0;

    while (true)
    {
        var result = await reader.ReadAsync();
        var buffer = result.Buffer;
        var sequenceReader = new SequenceReader<byte>(buffer);

        if (requestLine is null && sequenceReader.TryReadTo(out ReadOnlySequence<byte> requestLineSpan, requestLineSelimiter, true))
        {
            requestLine = ParseRequestLine(Encoding.UTF8.GetString(requestLineSpan));
            reader.AdvanceTo(sequenceReader.Position);
            continue;
        }
        else if (headers is null && sequenceReader.TryReadTo(out ReadOnlySequence<byte> headerSpan, headerDelimiter, true))
        {
            headers = ParseHeaders(Encoding.UTF8.GetString(headerSpan));
            reader.AdvanceTo(sequenceReader.Position);
            if (!headers.TryGetValue("content-length", out var length))
            {
                break;// skip reading body
            }
            contentLength = int.Parse(length.FirstOrDefault() ?? string.Empty);
            continue;
        }
        else if (body is null && sequenceReader.TryReadExact(contentLength, out ReadOnlySequence<byte> bodySpan))
        {
            body = bodySpan;
            break;
        }

        if (buffer.Length > MaxHeaderSize)
        {
            throw new InvalidOperationException();
        }

        reader.AdvanceTo(buffer.Start, buffer.End);


        if (result.IsCompleted)
        {
            break;
        }
    }

    return new Request(requestLine, headers, body);
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


record struct Request(RequestLine? RequestLine, Dictionary<string, List<string>>? Headers, ReadOnlySequence<byte>? Body);
record struct RequestLine(string Method, string Path, string? Protocol);
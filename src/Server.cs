using System.Buffers;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using System.Text;



// You can use print statements as follows for debugging, they'll be visible when running tests.
Console.WriteLine("Logs from your program will appear here!");

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

        if (requestLine is null)
        {
            throw new InvalidOperationException("Missing request information");
        }

        var content = string.Empty;
        var (method, path, _) = ParseRequestLine(requestLine);
        var headers = ParseHeaders(header);
        if (method == "GET")
        {
            content = HandleGetRequest(path, headers);
        }

        if (string.IsNullOrEmpty(content))
        {
            content = "HTTP/1.1 404 Not Found\r\n\r\n";
        }

        await networkStream.WriteAsync(Encoding.UTF8.GetBytes(content));
    }
}

static string HandleGetRequest(string path, Dictionary<string, List<string>> headers)
{
    if (path == "/")
    {
        return "HTTP/1.1 200 OK\r\n\r\n";
    }

    if (path.StartsWith("/echo"))
    {
        var rest = path.Length > 6 ? path.AsSpan().Slice(6) : string.Empty;
        return $"HTTP/1.1 200 OK\r\nContent-Type: text/plain\r\nContent-Length: {rest.Length}\r\n\r\n{rest}";
    }

    if (path.StartsWith("/user-agent"))
    {
        var userAgent = headers.GetValueOrDefault("user-agent")?.FirstOrDefault() ?? string.Empty;
        return $"HTTP/1.1 200 OK\r\nContent-Type: text/plain\r\nContent-Length: {userAgent.Length}\r\n\r\n{userAgent}";
    }

    return string.Empty;
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

record struct RequestLine(string Method, string Path, string? Protocol);

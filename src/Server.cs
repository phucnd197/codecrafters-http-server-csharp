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
    using var socket = await server.AcceptSocketAsync(); // wait for client

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

    var path = GetPath(requestLine);
    if (path == "/")
    {
        await networkStream.WriteAsync(Encoding.UTF8.GetBytes("HTTP/1.1 200 OK\r\n\r\n"));
    }
    else
    {

        await networkStream.WriteAsync(Encoding.UTF8.GetBytes("HTTP/1.1 404 Not Found\r\n\r\n"));
    }
}

static string GetPath(string requestLine)
{
    var components = requestLine.Split(' ');
    return components[1];
}


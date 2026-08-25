using codecrafters_http_server.src;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;



// You can use print statements as follows for debugging, they'll be visible when running tests.
Console.WriteLine("Logs from your program will appear here!");
var requestHandler = new RequestHandler(args);

using TcpListener server = new(IPAddress.Any, 4221);
server.Start();
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
        while (true)
        {
            var request = await RequestParser.ReadPayloadAsync(reader);
            if (request is null)
            {
                break;
            }

            await requestHandler.HandleRequest(networkStream, request.Value);

            if (ShouldCloseConnection(request.Value))
            {
                break;
            }
        }
    }
}

static bool ShouldCloseConnection(Request request)
{
    if (request.Headers.TryGetValue("connection", out var values))
    {
        return values.Any(x => x.Equals("close", StringComparison.OrdinalIgnoreCase));
    }

    return false;
}


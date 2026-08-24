using codecrafters_http_server.src;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using System.Text;



// You can use print statements as follows for debugging, they'll be visible when running tests.
Console.WriteLine("Logs from your program will appear here!");
var requestHandler = new RequestHandler(args);

// TODO: Uncomment the code below to pass the first stage
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
        var (requestLine, headers, body) = await Utility.ReadPayloadAsync(reader);

        if (requestLine is null)
        {
            throw new InvalidOperationException("Missing request information");
        }

        var (method, path, _) = requestLine.Value;
        if (method == "GET")
        {
            await requestHandler.HandleGetRequest(networkStream, path, headers);
            return;
        }
        else if (method == "POST")
        {
            await requestHandler.HandlePostRequest(networkStream, path, headers, body);
            return;
        }

        await networkStream.WriteAsync(Encoding.UTF8.GetBytes(Utility.GetEmptyResponse(HttpStatusCode.MethodNotAllowed, "Method Not Allowed")));
    }
}


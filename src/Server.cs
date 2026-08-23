using System.Net;
using System.Net.Sockets;
using System.Text;

// You can use print statements as follows for debugging, they'll be visible when running tests.
Console.WriteLine("Logs from your program will appear here!");

// TODO: Uncomment the code below to pass the first stage
using TcpListener server = new(IPAddress.Any, 4221);
server.Start();
while (true)
{
    using var socket = await server.AcceptSocketAsync(); // wait for client
    Console.WriteLine("Connected!");

    using var stream = new NetworkStream(socket);
    await stream.WriteAsync(Encoding.UTF8.GetBytes("HTTP/1.1 200 OK\r\n\r\n"));
}

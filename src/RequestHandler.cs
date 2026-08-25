using System.Buffers;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace codecrafters_http_server.src;

internal sealed class RequestHandler
{
    private readonly string _initialFilePath;

    public RequestHandler(string[] args)
    {
        var directoryArgStart = Array.IndexOf(args, "--directory");
        _initialFilePath = directoryArgStart != -1 && args.Length > directoryArgStart + 1 ? args[directoryArgStart + 1] : string.Empty;
    }

    public async Task HandleRequest(NetworkStream networkStream, Request request)
    {
        var (requestLine, headers, body) = request;
        var (method, path, _) = requestLine;
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

        await WriteToNetworkStream(networkStream, Response.Factory.Create(HttpStatusCode.MethodNotAllowed, "Method Not Allowed"));
    }

    public async Task HandleGetRequest(NetworkStream networkStream, string path, Dictionary<string, List<string>> headers)
    {
        if (path == "/")
        {
            await WriteToNetworkStream(networkStream, Response.Factory.Create());
            return;
        }

        if (path.StartsWith("/echo"))
        {
            var rest = path.Length > 6 ? path.Substring(6) : string.Empty;
            await WriteToNetworkStream(networkStream, await Response.Factory.Create(rest, headers));
            return;
        }

        if (path.StartsWith("/user-agent"))
        {
            var userAgent = headers.GetValueOrDefault("user-agent")?.FirstOrDefault() ?? string.Empty;
            await WriteToNetworkStream(networkStream, await Response.Factory.Create(userAgent, headers));
            return;
        }

        if (path.StartsWith("/files"))
        {
            var fileName = path.Length > 7 ? path.Substring(7) : string.Empty;
            if (path.Contains(".."))
            {
                await WriteToNetworkStream(networkStream, Response.Factory.Create(HttpStatusCode.BadRequest));
                return;
            }

            var fullPath = Path.Combine(_initialFilePath, fileName);
            if (!File.Exists(fullPath))
            {
                await WriteToNetworkStream(networkStream, Response.Factory.Create(HttpStatusCode.NotFound, "Not Found"));
                return;
            }

            await WriteToNetworkStream(networkStream, await Response.Factory.Create(await File.ReadAllBytesAsync(fullPath), headers, contentType: "application/octet-stream"));
            return;
        }

        await WriteToNetworkStream(networkStream, Response.Factory.Create(HttpStatusCode.NotFound, "Not Found"));
    }

    public async Task HandlePostRequest(NetworkStream networkStream, string path, Dictionary<string, List<string>> headers, ReadOnlySequence<byte>? body)
    {
        if (path.StartsWith("/files"))
        {
            if (body is null)
            {
                await WriteToNetworkStream(networkStream, Response.Factory.Create(HttpStatusCode.BadRequest));
                return;
            }
            var fileName = path.Length > 7 ? path.Substring(7) : string.Empty;
            if (path.Contains(".."))
            {
                await WriteToNetworkStream(networkStream, Response.Factory.Create(HttpStatusCode.BadRequest));
                return;
            }

            var fullPath = Path.Combine(_initialFilePath, fileName);
            if (File.Exists(fullPath))
            {
                await WriteToNetworkStream(networkStream, Response.Factory.Create(HttpStatusCode.UnprocessableEntity, "File existed"));
                return;
            }

            using var fileStream = File.OpenWrite(fullPath);
            await fileStream.WriteAsync(body.Value.ToArray());
            await WriteToNetworkStream(networkStream, Response.Factory.Create(HttpStatusCode.Created));
        }
    }

    private static async Task WriteToNetworkStream(NetworkStream networkStream, Response response)
    {
        await networkStream.WriteAsync(Encoding.UTF8.GetBytes(response.GetResponseString()));
        if (response.Content is not null)
        {
            await networkStream.WriteAsync(response.Content.Value);
        }
    }
}


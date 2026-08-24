using System.Buffers;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace codecrafters_http_server.src;

internal sealed class RequestHandler
{
    private readonly string initialFilePath;
    public RequestHandler(string[] args)
    {
        var directoryArgStart = Array.IndexOf(args, "--directory");
        initialFilePath = directoryArgStart != -1 && args.Length > directoryArgStart + 1 ? args[directoryArgStart + 1] : string.Empty;
    }

    public async Task HandleGetRequest(NetworkStream networkStream, string path, Dictionary<string, List<string>>? headers)
    {
        if (path == "/")
        {
            await networkStream.WriteAsync(Encoding.UTF8.GetBytes(Utility.GetResponse([])));
            return;
        }

        if (path.StartsWith("/echo"))
        {
            var rest = path.Length > 6 ? path.AsSpan().Slice(6) : string.Empty;
            await networkStream.WriteAsync(Encoding.UTF8.GetBytes(Utility.GetResponse(rest, contentType: "text/plain")));
            return;
        }

        if (path.StartsWith("/user-agent"))
        {
            var userAgent = headers?.GetValueOrDefault("user-agent")?.FirstOrDefault() ?? string.Empty;
            await networkStream.WriteAsync(Encoding.UTF8.GetBytes(Utility.GetResponse(userAgent, contentType: "text/plain")));
            return;
        }

        if (path.StartsWith("/files"))
        {
            var fileName = path.Length > 7 ? path.Substring(7) : string.Empty;
            if (path.Contains(".."))
            {
                await networkStream.WriteAsync(Encoding.UTF8.GetBytes(Utility.GetEmptyResponse(HttpStatusCode.BadRequest)));
                return;
            }

            var fullPath = Path.Combine(initialFilePath, fileName);
            if (!File.Exists(fullPath))
            {
                await networkStream.WriteAsync(Encoding.UTF8.GetBytes(Utility.GetEmptyResponse(HttpStatusCode.NotFound, "Not Found")));
                return;
            }

            var fileInfo = new FileInfo(fullPath);
            await networkStream.WriteAsync(Encoding.UTF8.GetBytes(Utility.GetEmptyResponse(contentType: "application/octet-stream", contentLength: fileInfo.Length)));
            using var fileStream = File.OpenRead(fullPath);
            await fileStream.CopyToAsync(networkStream);
        }

        await networkStream.WriteAsync(Encoding.UTF8.GetBytes(Utility.GetEmptyResponse(HttpStatusCode.NotFound, "Not Found")));
    }

    public async Task HandlePostRequest(NetworkStream networkStream, string path, Dictionary<string, List<string>>? headers, ReadOnlySequence<byte>? body)
    {
        if (path.StartsWith("/files"))
        {
            if (body is null)
            {
                await networkStream.WriteAsync(Encoding.UTF8.GetBytes(Utility.GetEmptyResponse(HttpStatusCode.BadRequest)));
                return;
            }
            var fileName = path.Length > 7 ? path.Substring(7) : string.Empty;
            if (path.Contains(".."))
            {
                await networkStream.WriteAsync(Encoding.UTF8.GetBytes(Utility.GetEmptyResponse(HttpStatusCode.BadRequest)));
                return;
            }

            var fullPath = Path.Combine(initialFilePath, fileName);
            if (File.Exists(fullPath))
            {
                await networkStream.WriteAsync(Encoding.UTF8.GetBytes(Utility.GetEmptyResponse(HttpStatusCode.UnprocessableEntity, "File existed")));
                return;
            }

            using var fileStream = File.OpenWrite(fullPath);
            await fileStream.WriteAsync(body.Value.ToArray());
            await networkStream.WriteAsync(Encoding.UTF8.GetBytes(Utility.GetEmptyResponse(HttpStatusCode.Created)));
        }
    }
}

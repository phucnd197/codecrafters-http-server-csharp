using System.IO.Compression;
using System.Net;
using System.Text;

namespace codecrafters_http_server.src;

public readonly struct Response
{
    public ReadOnlyMemory<byte>? Content { get; }
    public HttpStatusCode Status { get; }
    public string StatusCode { get; }
    public string ContentType { get; }
    public long? ContentLength { get => Content?.Length; }
    public string? ContentEncoding { get; }

    private Response(ReadOnlyMemory<byte>? content = default, HttpStatusCode? status = default, string? statusCode = default, string? contentType = default, string? contentEncoding = default)
    {
        Content = content;
        Status = status ?? HttpStatusCode.OK;
        StatusCode = statusCode ?? Status.ToString();
        ContentType = contentType ?? "text/plain";
        ContentEncoding = contentEncoding;
    }

    public string GetResponseString()
    {
        var contentTypeHeader = ContentType is not null ? $"\r\nContent-Type: {ContentType}" : string.Empty;
        var contentLengthHeader = ContentLength is not null ? $"\r\nContent-Length: {ContentLength}" : string.Empty;
        var contentEncodingHeader = ContentEncoding is not null ? $"\r\nContent-Encoding: {ContentEncoding}" : string.Empty;
        return $"HTTP/1.1 {(int)Status} {StatusCode}{contentTypeHeader}{contentLengthHeader}{contentEncodingHeader}\r\n\r\n";
    }

    public sealed class Factory
    {
        public static async Task<Response> Create(string content, Dictionary<string, List<string>> requestHeaders, HttpStatusCode? status = default, string? statusCode = default, string? contentType = default)
        {
            return await Create(Encoding.UTF8.GetBytes(content), requestHeaders, status, statusCode, contentType);
        }

        public static async Task<Response> Create(byte[] content, Dictionary<string, List<string>> requestHeaders, HttpStatusCode? status = default, string? statusCode = default, string? contentType = default)
        {
            if (!TryGetEncoding(requestHeaders, out var encoding))
            {
                return new Response(content, status, statusCode, contentType);
            }

            if (encoding.Equals("gzip", StringComparison.OrdinalIgnoreCase))
            {
                using var outputStream = new MemoryStream();
                using (var gzipStream = new GZipStream(outputStream, CompressionLevel.Optimal))
                {
                    await gzipStream.WriteAsync(content);
                }

                var rawContent = outputStream.ToArray();
                return new Response(rawContent, status, statusCode, contentType, encoding);
            }
            else if (encoding.Equals("brotli", StringComparison.OrdinalIgnoreCase))
            {
                using var outputStream = new MemoryStream();
                using (var brotliStream = new BrotliStream(outputStream, CompressionLevel.Optimal))
                {
                    await brotliStream.WriteAsync(content);
                }

                var rawContent = outputStream.ToArray();
                return new Response(rawContent, status, statusCode, contentType, encoding);
            }

            return new Response(content, status, statusCode, contentType);
        }

        private readonly static HashSet<string> SupportedEncodings = new(StringComparer.OrdinalIgnoreCase) { "gzip", "brotli" };

        private static bool TryGetEncoding(Dictionary<string, List<string>> requestHeaders, out string encoding)
        {
            if (!requestHeaders.TryGetValue("accept-encoding", out var encodings))
            {
                encoding = string.Empty;
                return false;
            }

            encoding = encodings.FirstOrDefault(SupportedEncodings.Contains) ?? string.Empty;
            return !string.IsNullOrEmpty(encoding);
        }

        public static Response Create(HttpStatusCode? status = default, string? statusCode = default)
        {
            return new Response(status: status, statusCode: statusCode);
        }
    }
}


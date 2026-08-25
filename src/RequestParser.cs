using System.Buffers;
using System.IO.Pipelines;
using System.Text;

namespace codecrafters_http_server.src;


public record struct Request(RequestLine RequestLine, Dictionary<string, List<string>> Headers, ReadOnlySequence<byte>? Body);

public record struct RequestLine(string Method, string Path, string? Protocol);

public static class RequestParser
{
    private readonly static byte[] headerDelimiter = "\r\n\r\n"u8.ToArray();
    private readonly static byte[] requestLineSelimiter = "\r\n"u8.ToArray();
    private const long maxHeaderSize = 8 * 1024;

    public static async Task<Request> ReadPayloadAsync(PipeReader reader)
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

            if (buffer.Length > maxHeaderSize)
            {
                throw new InvalidOperationException();
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
        if (headers is null)
        {
            throw new InvalidOperationException("Missing headers information");
        }

        return new Request(requestLine.Value, headers, body);
    }

    private static RequestLine ParseRequestLine(string requestLine)
    {
        var components = requestLine.Split(' ');
        return new RequestLine(components[0], components[1], components[2]);
    }

    private static Dictionary<string, List<string>> ParseHeaders(string? header)
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
}

using System.Globalization;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Aion.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aion.Infrastructure.Services;

public sealed class S3ObjectStore : ICloudObjectStore
{
    private static readonly HashSet<HttpStatusCode> RetryStatusCodes = new()
    {
        HttpStatusCode.RequestTimeout,
        HttpStatusCode.TooManyRequests,
        HttpStatusCode.InternalServerError,
        HttpStatusCode.BadGateway,
        HttpStatusCode.ServiceUnavailable,
        HttpStatusCode.GatewayTimeout
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly CloudBackupOptions _options;
    private readonly ILogger<S3ObjectStore> _logger;

    public S3ObjectStore(
        IHttpClientFactory httpClientFactory,
        IOptions<CloudBackupOptions> options,
        ILogger<S3ObjectStore> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
    }

    public async Task UploadObjectAsync(string key, Stream content, string contentType, CancellationToken cancellationToken)
    {
        var payloadHash = await ComputeHashAsync(content, cancellationToken).ConfigureAwait(false);
        content.Position = 0;
        var request = CreateRequest(HttpMethod.Put, key, payloadHash, query: null);
        request.Content = new StreamContent(content);
        request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);

        using var _ = await SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    public async Task DownloadObjectAsync(string key, Stream destination, CancellationToken cancellationToken)
    {
        var request = CreateRequest(HttpMethod.Get, key, HashEmptyPayload(), query: null);
        using var response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
        await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await responseStream.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteObjectAsync(string key, CancellationToken cancellationToken)
    {
        var request = CreateRequest(HttpMethod.Delete, key, HashEmptyPayload(), query: null);
        using var _ = await SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<CloudObjectInfo>> ListObjectsAsync(string prefix, CancellationToken cancellationToken)
    {
        var query = new Dictionary<string, string>
        {
            ["list-type"] = "2",
            ["prefix"] = prefix
        };
        var request = CreateRequest(HttpMethod.Get, string.Empty, HashEmptyPayload(), query);
        using var response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
        var xml = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        var document = XDocument.Parse(xml);
        XNamespace ns = document.Root?.Name.Namespace ?? string.Empty;
        var items = new List<CloudObjectInfo>();
        foreach (var content in document.Descendants(ns + "Contents"))
        {
            var key = content.Element(ns + "Key")?.Value;
            var sizeText = content.Element(ns + "Size")?.Value;
            var modifiedText = content.Element(ns + "LastModified")?.Value;
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(sizeText) || string.IsNullOrWhiteSpace(modifiedText))
            {
                continue;
            }

            if (!long.TryParse(sizeText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var size))
            {
                continue;
            }

            if (!DateTimeOffset.TryParse(modifiedText, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var modified))
            {
                continue;
            }

            items.Add(new CloudObjectInfo(key, size, modified));
        }

        return items;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient(CloudHttpClientNames.CloudBackup);
        var attempt = 0;
        Exception? lastError = null;

        while (attempt <= _options.MaxRetries)
        {
            attempt++;
            try
            {
                var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                    .ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    return response;
                }

                if (!RetryStatusCodes.Contains(response.StatusCode) || attempt > _options.MaxRetries)
                {
                    var message = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                    response.Dispose();
                    throw new InvalidOperationException($"S3 request failed with status {(int)response.StatusCode}: {message}");
                }

                response.Dispose();
            }
            catch (Exception ex) when (attempt <= _options.MaxRetries)
            {
                lastError = ex;
            }

            var delay = TimeSpan.FromMilliseconds(_options.RetryBaseDelayMs * Math.Pow(2, attempt - 1));
            _logger.LogWarning("Retrying S3 request ({Attempt}/{MaxAttempts}) after {Delay} due to error.", attempt, _options.MaxRetries, delay);
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            request = CloneRequest(request);
        }

        throw new InvalidOperationException("S3 request failed after retries", lastError);
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string key, string payloadHash, IDictionary<string, string>? query)
    {
        if (string.IsNullOrWhiteSpace(_options.Endpoint))
        {
            throw new InvalidOperationException("Cloud backup endpoint must be configured.");
        }

        if (string.IsNullOrWhiteSpace(_options.Bucket))
        {
            throw new InvalidOperationException("Cloud backup bucket must be configured.");
        }

        var now = DateTimeOffset.UtcNow;
        var amzDate = now.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        var dateStamp = now.ToString("yyyyMMdd", CultureInfo.InvariantCulture);

        var uri = BuildUri(key, query);
        var request = new HttpRequestMessage(method, uri);
        request.Headers.Host = uri.Host;
        request.Headers.TryAddWithoutValidation("x-amz-content-sha256", payloadHash);
        request.Headers.TryAddWithoutValidation("x-amz-date", amzDate);

        var canonicalRequest = BuildCanonicalRequest(request, uri, payloadHash);
        var stringToSign = BuildStringToSign(canonicalRequest, amzDate, dateStamp);
        var signature = ComputeSignature(stringToSign, dateStamp);
        var authorization = $"AWS4-HMAC-SHA256 Credential={_options.AccessKeyId}/{dateStamp}/{_options.Region}/s3/aws4_request, SignedHeaders=host;x-amz-content-sha256;x-amz-date, Signature={signature}";
        request.Headers.TryAddWithoutValidation("Authorization", authorization);

        return request;
    }

    private Uri BuildUri(string key, IDictionary<string, string>? query)
    {
        var endpoint = _options.Endpoint!.TrimEnd('/');
        var encodedKey = Uri.EscapeDataString(key).Replace("%2F", "/");
        string baseUri;
        if (_options.UsePathStyle)
        {
            baseUri = $"{endpoint}/{_options.Bucket}";
        }
        else
        {
            var uriBuilder = new UriBuilder(endpoint);
            uriBuilder.Host = $"{_options.Bucket}.{uriBuilder.Host}";
            baseUri = uriBuilder.Uri.ToString().TrimEnd('/');
        }

        var path = string.IsNullOrWhiteSpace(encodedKey) ? string.Empty : $"/{encodedKey}";
        var uriBuilderFinal = new UriBuilder($"{baseUri}{path}");

        if (query is { Count: > 0 })
        {
            var ordered = query.OrderBy(k => k.Key, StringComparer.Ordinal)
                .Select(kvp => $"{Uri.EscapeDataString(kvp.Key)}={Uri.EscapeDataString(kvp.Value)}");
            uriBuilderFinal.Query = string.Join("&", ordered);
        }

        return uriBuilderFinal.Uri;
    }

    private static string BuildCanonicalRequest(HttpRequestMessage request, Uri uri, string payloadHash)
    {
        var canonicalQuery = uri.Query.TrimStart('?');
        var canonicalHeaders = new StringBuilder();
        canonicalHeaders.Append("host:").Append(uri.Host).Append('\n');
        canonicalHeaders.Append("x-amz-content-sha256:").Append(payloadHash).Append('\n');
        canonicalHeaders.Append("x-amz-date:").Append(request.Headers.GetValues("x-amz-date").First()).Append('\n');

        return string.Join("\n",
            request.Method.Method,
            uri.AbsolutePath,
            canonicalQuery,
            canonicalHeaders.ToString(),
            "host;x-amz-content-sha256;x-amz-date",
            payloadHash);
    }

    private string BuildStringToSign(string canonicalRequest, string amzDate, string dateStamp)
    {
        var hash = HashHex(canonicalRequest);
        return string.Join("\n",
            "AWS4-HMAC-SHA256",
            amzDate,
            $"{dateStamp}/{_options.Region}/s3/aws4_request",
            hash);
    }

    private string ComputeSignature(string stringToSign, string dateStamp)
    {
        if (string.IsNullOrWhiteSpace(_options.SecretAccessKey) || string.IsNullOrWhiteSpace(_options.AccessKeyId))
        {
            throw new InvalidOperationException("Cloud backup access keys must be configured.");
        }

        var signingKey = GetSignatureKey(_options.SecretAccessKey, dateStamp, _options.Region ?? "us-east-1", "s3");
        var signature = HmacSha256Hex(signingKey, stringToSign);
        return signature;
    }

    private static byte[] GetSignatureKey(string key, string dateStamp, string regionName, string serviceName)
    {
        var kDate = HmacSha256Bytes(Encoding.UTF8.GetBytes("AWS4" + key), dateStamp);
        var kRegion = HmacSha256Bytes(kDate, regionName);
        var kService = HmacSha256Bytes(kRegion, serviceName);
        var kSigning = HmacSha256Bytes(kService, "aws4_request");
        return kSigning;
    }

    private static string HashEmptyPayload()
        => HashHex(string.Empty);

    private static string HashHex(string input)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input))).ToLowerInvariant();

    private static string HmacSha256Hex(byte[] key, string data)
        => Convert.ToHexString(HmacSha256Bytes(key, data)).ToLowerInvariant();

    private static byte[] HmacSha256Bytes(byte[] key, string data)
    {
        using var hmac = new HMACSHA256(key);
        return hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
    }

    private static async Task<string> ComputeHashAsync(Stream content, CancellationToken cancellationToken)
    {
        if (content.CanSeek)
        {
            content.Position = 0;
        }

        using var sha = SHA256.Create();
        var hash = await sha.ComputeHashAsync(content, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static HttpRequestMessage CloneRequest(HttpRequestMessage request)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri);
        foreach (var header in request.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        if (request.Content is not null)
        {
            var memoryStream = new MemoryStream();
            request.Content.CopyToAsync(memoryStream).GetAwaiter().GetResult();
            memoryStream.Position = 0;
            clone.Content = new StreamContent(memoryStream);
            foreach (var header in request.Content.Headers)
            {
                clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        return clone;
    }
}

public static class CloudHttpClientNames
{
    public const string CloudBackup = "CloudBackup";
}

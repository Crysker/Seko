using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;

namespace Seko.Infrastructure.Agent.Web;

public sealed class WebResearchService
{
    private const int MaximumSearchPageCharacters =
        240_000;

    private const int MaximumRedirects =
        5;

    private static readonly Regex AnchorRegex =
        new(
            @"<a\b(?<attrs>[^>]*)>(?<text>.*?)</a>",
            RegexOptions.Compiled
            | RegexOptions.IgnoreCase
            | RegexOptions.Singleline);

    private static readonly Regex ClassAttributeRegex =
        new(
            @"\bclass\s*=\s*(?:""(?<double>[^""]*)""|'(?<single>[^']*)')",
            RegexOptions.Compiled
            | RegexOptions.IgnoreCase
            | RegexOptions.Singleline);

    private static readonly Regex HrefAttributeRegex =
        new(
            @"\bhref\s*=\s*(?:""(?<double>[^""]*)""|'(?<single>[^']*)')",
            RegexOptions.Compiled
            | RegexOptions.IgnoreCase
            | RegexOptions.Singleline);

    private static readonly Regex SnippetRegex =
        new(
            @"<(?:a|div|span)\b[^>]*class\s*=\s*(?:""[^""]*result__snippet[^""]*""|'[^']*result__snippet[^']*')[^>]*>(?<text>.*?)</(?:a|div|span)>",
            RegexOptions.Compiled
            | RegexOptions.IgnoreCase
            | RegexOptions.Singleline);

    private readonly HttpClient _httpClient;
    private readonly WebAddressGuard _addressGuard;

    public WebResearchService()
        : this(
            CreateDefaultDependencies())
    {
    }

    public WebResearchService(
        HttpClient httpClient,
        WebAddressGuard addressGuard)
    {
        _httpClient =
            httpClient
            ?? throw new ArgumentNullException(
                nameof(httpClient));

        _addressGuard =
            addressGuard
            ?? throw new ArgumentNullException(
                nameof(addressGuard));
    }

    private WebResearchService(
        DefaultDependencies dependencies)
        : this(
            dependencies.HttpClient,
            dependencies.AddressGuard)
    {
    }

    public async Task<IReadOnlyCollection<WebSearchResult>> SearchAsync(
        string query,
        int maxResults = 6,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(
                query))
        {
            throw new ArgumentException(
                "Web search query cannot be empty.",
                nameof(query));
        }

        var normalizedQuery =
            query.Trim();

        if (normalizedQuery.Length > 500)
        {
            throw new ArgumentException(
                "Web search query is too long.",
                nameof(query));
        }

        maxResults =
            Math.Clamp(
                maxResults,
                1,
                8);

        var searchUrl =
            "https://html.duckduckgo.com/html/?q="
            + Uri.EscapeDataString(
                normalizedQuery);

        var page =
            await GetTextAsync(
                searchUrl,
                MaximumSearchPageCharacters,
                cancellationToken);

        return
            ParseDuckDuckGoResults(
                page.Text,
                maxResults);
    }

    public async Task<WebFetchResult> FetchAsync(
        string url,
        int maxCharacters = 10_000,
        CancellationToken cancellationToken = default)
    {
        maxCharacters =
            Math.Clamp(
                maxCharacters,
                2_000,
                16_000);

        var rawLimit =
            Math.Min(
                120_000,
                Math.Max(
                    24_000,
                    maxCharacters * 8));

        var page =
            await GetTextAsync(
                url,
                rawLimit,
                cancellationToken);

        var contentTypeSeparator =
            page.ContentType.IndexOf(
                ';');

        var mediaType =
            (contentTypeSeparator >= 0
                ? page.ContentType[..contentTypeSeparator]
                : page.ContentType)
            .Trim();

        string title;
        string text;

        if (mediaType.Equals(
                "text/html",
                StringComparison.OrdinalIgnoreCase)
            || mediaType.Equals(
                "application/xhtml+xml",
                StringComparison.OrdinalIgnoreCase))
        {
            title =
                WebTextExtractor.ExtractTitle(
                    page.Text);

            text =
                WebTextExtractor.ExtractReadableText(
                    page.Text);
        }
        else
        {
            title =
                string.Empty;

            text =
                NormalizePlainText(
                    page.Text);
        }

        var outputWasTruncated =
            page.Truncated
            || text.Length > maxCharacters;

        if (text.Length > maxCharacters)
        {
            text =
                text[..maxCharacters];
        }

        return
            new WebFetchResult(
                page.FinalUri.ToString(),
                page.ContentType,
                title,
                text,
                outputWasTruncated);
    }

    internal static IReadOnlyCollection<WebSearchResult> ParseDuckDuckGoResults(
        string html,
        int maxResults)
    {
        if (string.IsNullOrWhiteSpace(
                html))
        {
            return
                Array.Empty<WebSearchResult>();
        }

        maxResults =
            Math.Clamp(
                maxResults,
                1,
                8);

        var anchors =
            AnchorRegex.Matches(
                    html)
                .Cast<Match>()
                .Where(
                    match =>
                        HasCssClass(
                            match.Groups["attrs"].Value,
                            "result__a"))
                .ToList();

        var results =
            new List<WebSearchResult>();

        var seenUrls =
            new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

        for (var index = 0;
             index < anchors.Count
             && results.Count < maxResults;
             index++)
        {
            var anchor =
                anchors[index];

            var href =
                GetAttribute(
                    HrefAttributeRegex,
                    anchor.Groups["attrs"].Value);

            var url =
                NormalizeSearchResultUrl(
                    href);

            if (string.IsNullOrWhiteSpace(
                    url)
                || !seenUrls.Add(
                    url))
            {
                continue;
            }

            var title =
                WebTextExtractor.ExtractInlineText(
                    anchor.Groups["text"].Value);

            if (string.IsNullOrWhiteSpace(
                    title))
            {
                title =
                    url;
            }

            var snippetEnd =
                index + 1 < anchors.Count
                    ? anchors[index + 1].Index
                    : Math.Min(
                        html.Length,
                        anchor.Index + 6_000);

            snippetEnd =
                Math.Min(
                    snippetEnd,
                    anchor.Index + 6_000);

            var snippetLength =
                Math.Max(
                    0,
                    snippetEnd - anchor.Index);

            var snippetRegion =
                snippetLength > 0
                    ? html.Substring(
                        anchor.Index,
                        snippetLength)
                    : string.Empty;

            var snippetMatch =
                SnippetRegex.Match(
                    snippetRegion);

            var snippet =
                snippetMatch.Success
                    ? WebTextExtractor.ExtractInlineText(
                        snippetMatch.Groups["text"].Value)
                    : string.Empty;

            results.Add(
                new WebSearchResult(
                    title,
                    url,
                    snippet));
        }

        return
            results.AsReadOnly();
    }

    private async Task<RawTextResponse> GetTextAsync(
        string rawUrl,
        int maximumCharacters,
        CancellationToken cancellationToken)
    {
        var current =
            await _addressGuard.ValidateAsync(
                rawUrl,
                cancellationToken);

        for (var redirect = 0;
             redirect <= MaximumRedirects;
             redirect++)
        {
            using var request =
                new HttpRequestMessage(
                    HttpMethod.Get,
                    current);

            request.Headers.UserAgent.ParseAdd(
                "Mozilla/5.0 (compatible; Seko/1.0; local-research-agent)");

            request.Headers.Accept.ParseAdd(
                "text/html, text/plain, application/json, application/xml, text/xml, application/xhtml+xml;q=0.9, */*;q=0.1");

            using var response =
                await _httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);

            var status =
                (int)response.StatusCode;

            if (status is 301 or 302 or 303 or 307 or 308)
            {
                if (redirect
                    >= MaximumRedirects)
                {
                    throw new HttpRequestException(
                        "Web request exceeded the redirect limit.");
                }

                var location =
                    response.Headers.Location
                    ?? throw new HttpRequestException(
                        "Web redirect did not contain a Location header.");

                var next =
                    location.IsAbsoluteUri
                        ? location
                        : new Uri(
                            current,
                            location);

                current =
                    await _addressGuard.ValidateAsync(
                        next.ToString(),
                        cancellationToken);

                continue;
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException(
                    $"Web request returned HTTP {status} ({response.ReasonPhrase}).");
            }

            var contentType =
                response.Content.Headers.ContentType?.ToString()
                ?? "application/octet-stream";

            var mediaType =
                response.Content.Headers.ContentType?.MediaType
                ?? string.Empty;

            if (!IsAllowedTextContentType(
                    mediaType))
            {
                throw new InvalidOperationException(
                    $"Web response content type '{contentType}' is not a supported text format.");
            }

            await using var stream =
                await response.Content.ReadAsStreamAsync(
                    cancellationToken);

            using var reader =
                new StreamReader(
                    stream,
                    Encoding.UTF8,
                    detectEncodingFromByteOrderMarks:
                        true,
                    bufferSize:
                        4_096,
                    leaveOpen:
                        false);

            var (text, truncated) =
                await ReadBoundedAsync(
                    reader,
                    maximumCharacters,
                    cancellationToken);

            return
                new RawTextResponse(
                    current,
                    contentType,
                    text,
                    truncated);
        }

        throw new HttpRequestException(
            "Web request exceeded the redirect limit.");
    }

    private static bool IsAllowedTextContentType(
        string mediaType)
    {
        if (string.IsNullOrWhiteSpace(
                mediaType))
        {
            return false;
        }

        return
            mediaType.StartsWith(
                "text/",
                StringComparison.OrdinalIgnoreCase)
            || mediaType.Equals(
                "application/json",
                StringComparison.OrdinalIgnoreCase)
            || mediaType.EndsWith(
                "+json",
                StringComparison.OrdinalIgnoreCase)
            || mediaType.Equals(
                "application/xml",
                StringComparison.OrdinalIgnoreCase)
            || mediaType.EndsWith(
                "+xml",
                StringComparison.OrdinalIgnoreCase)
            || mediaType.Equals(
                "application/xhtml+xml",
                StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<(string Text, bool Truncated)> ReadBoundedAsync(
        TextReader reader,
        int maximumCharacters,
        CancellationToken cancellationToken)
    {
        var builder =
            new StringBuilder(
                Math.Min(
                    maximumCharacters,
                    16_384));

        var buffer =
            new char[4_096];

        var truncated =
            false;

        while (builder.Length
               <= maximumCharacters)
        {
            var remaining =
                maximumCharacters
                + 1
                - builder.Length;

            if (remaining <= 0)
            {
                truncated =
                    true;

                break;
            }

            var count =
                await reader.ReadAsync(
                    buffer.AsMemory(
                        0,
                        Math.Min(
                            buffer.Length,
                            remaining)),
                    cancellationToken);

            if (count == 0)
            {
                break;
            }

            builder.Append(
                buffer,
                0,
                count);

            if (builder.Length
                > maximumCharacters)
            {
                truncated =
                    true;

                builder.Length =
                    maximumCharacters;

                break;
            }
        }

        return
            (
                builder.ToString(),
                truncated
            );
    }

    private static string NormalizePlainText(
        string value)
    {
        return
            value
                .Replace(
                    "\r\n",
                    "\n",
                    StringComparison.Ordinal)
                .Replace(
                    '\r',
                    '\n')
                .Trim();
    }

    private static bool HasCssClass(
        string attributes,
        string expectedClass)
    {
        var classes =
            GetAttribute(
                ClassAttributeRegex,
                attributes);

        return
            classes
                .Split(
                    (char[]?)null,
                    StringSplitOptions.RemoveEmptyEntries)
                .Any(
                    value =>
                        value.Equals(
                            expectedClass,
                            StringComparison.OrdinalIgnoreCase));
    }

    private static string GetAttribute(
        Regex regex,
        string attributes)
    {
        var match =
            regex.Match(
                attributes);

        if (!match.Success)
        {
            return string.Empty;
        }

        var value =
            match.Groups["double"].Success
                ? match.Groups["double"].Value
                : match.Groups["single"].Value;

        return
            WebUtility.HtmlDecode(
                value);
    }

    private static string NormalizeSearchResultUrl(
        string href)
    {
        if (string.IsNullOrWhiteSpace(
                href))
        {
            return string.Empty;
        }

        var normalized =
            WebUtility.HtmlDecode(
                href.Trim());

        if (normalized.StartsWith(
                "//",
                StringComparison.Ordinal))
        {
            normalized =
                "https:"
                + normalized;
        }
        else if (normalized.StartsWith(
                     "/",
                     StringComparison.Ordinal))
        {
            normalized =
                "https://duckduckgo.com"
                + normalized;
        }

        if (!Uri.TryCreate(
                normalized,
                UriKind.Absolute,
                out var uri))
        {
            return string.Empty;
        }

        if (uri.Host.EndsWith(
                "duckduckgo.com",
                StringComparison.OrdinalIgnoreCase)
            && uri.AbsolutePath.StartsWith(
                "/l/",
                StringComparison.Ordinal))
        {
            foreach (var part
                     in uri.Query
                         .TrimStart('?')
                         .Split(
                             '&',
                             StringSplitOptions.RemoveEmptyEntries))
            {
                var separator =
                    part.IndexOf(
                        '=');

                if (separator <= 0)
                {
                    continue;
                }

                var key =
                    Uri.UnescapeDataString(
                        part[..separator]);

                if (!key.Equals(
                        "uddg",
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var value =
                    Uri.UnescapeDataString(
                        part[(separator + 1)..]);

                if (Uri.TryCreate(
                        value,
                        UriKind.Absolute,
                        out var outbound)
                    && IsHttpUri(
                        outbound))
                {
                    return
                        outbound.ToString();
                }
            }
        }

        return
            IsHttpUri(
                uri)
                ? uri.ToString()
                : string.Empty;
    }

    private static bool IsHttpUri(
        Uri uri)
    {
        return
            uri.Scheme.Equals(
                Uri.UriSchemeHttp,
                StringComparison.OrdinalIgnoreCase)
            || uri.Scheme.Equals(
                Uri.UriSchemeHttps,
                StringComparison.OrdinalIgnoreCase);
    }

    private static DefaultDependencies CreateDefaultDependencies()
    {
        var addressGuard =
            new WebAddressGuard();

        var handler =
            new SocketsHttpHandler
            {
                AllowAutoRedirect =
                    false,

                AutomaticDecompression =
                    DecompressionMethods.GZip
                    | DecompressionMethods.Deflate
                    | DecompressionMethods.Brotli,

                ConnectTimeout =
                    TimeSpan.FromSeconds(
                        10),

                PooledConnectionLifetime =
                    TimeSpan.FromMinutes(
                        5),

                UseProxy =
                    false
            };

        handler.ConnectCallback =
            (context, cancellationToken) =>
                ConnectPublicAsync(
                    addressGuard,
                    context.DnsEndPoint,
                    cancellationToken);

        var httpClient =
            new HttpClient(
                handler)
            {
                Timeout =
                    TimeSpan.FromSeconds(
                        30)
            };

        return
            new DefaultDependencies(
                httpClient,
                addressGuard);
    }

    private static async ValueTask<Stream> ConnectPublicAsync(
        WebAddressGuard addressGuard,
        DnsEndPoint endPoint,
        CancellationToken cancellationToken)
    {
        var addresses =
            await addressGuard.ResolvePublicAddressesAsync(
                endPoint.Host,
                cancellationToken);

        Exception? lastException =
            null;

        foreach (var address
                 in addresses)
        {
            var socket =
                new Socket(
                    address.AddressFamily,
                    SocketType.Stream,
                    ProtocolType.Tcp);

            try
            {
                await socket.ConnectAsync(
                    new IPEndPoint(
                        address,
                        endPoint.Port),
                    cancellationToken);

                return
                    new NetworkStream(
                        socket,
                        ownsSocket:
                            true);
            }
            catch (Exception exception)
                when (exception
                      is SocketException
                      or IOException)
            {
                lastException =
                    exception;

                socket.Dispose();
            }
        }

        throw new HttpRequestException(
            $"Could not connect to public host '{endPoint.Host}'.",
            lastException);
    }

    private sealed record RawTextResponse(
        Uri FinalUri,
        string ContentType,
        string Text,
        bool Truncated);

    private sealed record DefaultDependencies(
        HttpClient HttpClient,
        WebAddressGuard AddressGuard);
}

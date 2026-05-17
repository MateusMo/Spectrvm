using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Spectrvm.Models;

namespace Spectrvm.Services;

public partial class BrowserService
{
    private readonly HttpClient _http = new(new HttpClientHandler
    {
        AllowAutoRedirect = true,
        MaxAutomaticRedirections = 5
    });

    public async Task<BrowserResult> NavigateAsync(string url)
    {
        if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            url = "https://" + url;

        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("User-Agent", "Spectrvm/1.0 (privacy browser)");

        var response = await _http.SendAsync(request);
        var html     = await response.Content.ReadAsStringAsync();

        return new BrowserResult
        {
            Html         = html,
            CurlCommand  = BuildCurl(url, request),
            RequestInfo  = BuildRequestInfo(response),
            Links        = ExtractLinks(html, url),
            StatusCode   = (int)response.StatusCode,
            ContentType  = response.Content.Headers.ContentType?.ToString() ?? ""
        };
    }

    private static string BuildCurl(string url, HttpRequestMessage request)
    {
        var headers = string.Join(" \\\n  ",
            request.Headers.Select(h => $"-H \"{h.Key}: {string.Join(", ", h.Value)}\""));

        return string.IsNullOrEmpty(headers)
            ? $"curl -X GET \"{url}\""
            : $"curl -X GET \"{url}\" \\\n  {headers}";
    }

    private static string BuildRequestInfo(HttpResponseMessage response)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"HTTP/{response.Version} {(int)response.StatusCode} {response.ReasonPhrase}");
        sb.AppendLine();
        foreach (var h in response.Headers)
            sb.AppendLine($"{h.Key}: {string.Join(", ", h.Value)}");
        foreach (var h in response.Content.Headers)
            sb.AppendLine($"{h.Key}: {string.Join(", ", h.Value)}");
        return sb.ToString();
    }

    private static List<ExtractedLink> ExtractLinks(string html, string currentUrl)
    {
        var host  = new Uri(currentUrl).Host;
        var seen  = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list  = new List<ExtractedLink>();

        foreach (Match m in LinkRegex().Matches(html))
        {
            var raw = m.Value.TrimEnd('"', '\'', ')');
            if (!seen.Add(raw)) continue;
            if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri)) continue;

            list.Add(new ExtractedLink
            {
                Url        = raw,
                IsInternal = uri.Host.Equals(host, StringComparison.OrdinalIgnoreCase),
                Type       = DetectType(raw)
            });
        }

        return list;
    }

    private static string DetectType(string url) => url switch
    {
        _ when url.EndsWith(".js",  StringComparison.OrdinalIgnoreCase) => "js",
        _ when url.EndsWith(".css", StringComparison.OrdinalIgnoreCase) => "css",
        _ when url.EndsWith(".png") || url.EndsWith(".jpg") || url.EndsWith(".svg") => "asset",
        _ when url.Contains("/api/") => "api",
        _ => "page"
    };

    [GeneratedRegex(@"https?://[^\s""'<>\)]+", RegexOptions.IgnoreCase)]
    private static partial Regex LinkRegex();
}
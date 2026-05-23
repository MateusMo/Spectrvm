using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Spectrvm.Models;

namespace Spectrvm.Services;

public class BrowserService
{
    private readonly HttpClient       _http       = new(new HttpClientHandler
    {
        AllowAutoRedirect        = true,
        MaxAutomaticRedirections = 5
    });
    private readonly SubdomainService _subdomains = new();

    // ── Domínios conhecidos ──────────────────────────────────────────────────

    private static readonly HashSet<string> KnownTrackers = new(StringComparer.OrdinalIgnoreCase)
    {
        "google-analytics.com","analytics.google.com","googletagmanager.com",
        "doubleclick.net","googlesyndication.com","facebook.net","connect.facebook.net",
        "pixel.facebook.com","hotjar.com","clarity.ms","mouseflow.com",
        "mixpanel.com","segment.com","amplitude.com","heap.io",
        "fullstory.com","logrocket.com","crazyegg.com","optimizely.com",
        "omtrdc.net","demdex.net","scorecardresearch.com","quantserve.com",
        "taboola.com","outbrain.com","criteo.com","adroll.com",
        "pardot.com","hubspot.com","intercom.io","drift.com",
        "pingdom.net","nr-data.net","newrelic.com"
    };

    private static readonly HashSet<string> KnownCdns = new(StringComparer.OrdinalIgnoreCase)
    {
        "cloudflare.com","cloudflareinsights.com","akamai.com","akamaized.net",
        "akamaitech.net","fastly.net","cdn.jsdelivr.net","unpkg.com",
        "cdnjs.cloudflare.com","bootstrapcdn.com","jquery.com",
        "googleapis.com","gstatic.com","google.com","googleusercontent.com",
        "fontawesome.com","use.fontawesome.com","kit.fontawesome.com",
        "amazonaws.com","s3.amazonaws.com","cloudfront.net",
        "azureedge.net","msecnd.net","cdn.shopify.com","shopifycloud.com"
    };

    private static readonly HashSet<string> SuspiciousPatterns = new(StringComparer.OrdinalIgnoreCase)
    {
        "bit.ly","tinyurl.com","t.co","ow.ly","goo.gl","cutt.ly","rebrand.ly","short.io"
    };

    // ── Navegação principal ──────────────────────────────────────────────────

    /// <summary>
    /// Faz o request HTTP completo — usado pelos modos Links, Navigator, Curl, History.
    /// </summary>
    public async Task<BrowserResult> NavigateAsync(string url)
    {
        if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            url = "https://" + url;

        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("User-Agent", "Spectrvm/1.0 (privacy browser)");

        var response = await _http.SendAsync(request);
        var html     = await response.Content.ReadAsStringAsync();

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            uri = new Uri("https://unknown.invalid");

        var host       = uri.Host;
        var links      = LinkExtractor.Extract(html, url, host);
        var secHeaders = SecurityHeaderAnalyzer.Analyze(response);
        var techs      = TechFingerprinter.Detect(response, html);
        var subdomains = await _subdomains.FetchAsync(host);

        return new BrowserResult
        {
            Html            = html,
            CurlCommand     = BuildCurl(url, request),
            RequestInfo     = BuildRequestInfo(response),
            Links           = links,
            SecurityHeaders = secHeaders,
            Technologies    = techs,
            Subdomains      = subdomains,
            StatusCode      = (int)response.StatusCode,
            ContentType     = response.Content.Headers.ContentType?.ToString() ?? "",
            Timestamp       = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Modo Interpreter (Opção C): o WebView já renderizou a página.
    /// Fazemos um request separado APENAS para análise (links, segurança, tech, subdomínios).
    /// O HTML retornado aqui não é exibido — só alimenta o Navigator e painéis.
    /// </summary>
    public async Task<BrowserResult> AnalyzeOnlyAsync(string url)
    {
        if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            url = "https://" + url;

        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("User-Agent", "Spectrvm/1.0 (privacy browser)");

            var response = await _http.SendAsync(request);
            var html     = await response.Content.ReadAsStringAsync();

            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
                uri = new Uri("https://unknown.invalid");

            var host       = uri.Host;
            var links      = LinkExtractor.Extract(html, url, host);
            var secHeaders = SecurityHeaderAnalyzer.Analyze(response);
            var techs      = TechFingerprinter.Detect(response, html);
            var subdomains = await _subdomains.FetchAsync(host);

            return new BrowserResult
            {
                Html            = html,           // disponível para outras abas usarem
                CurlCommand     = BuildCurl(url, request),
                RequestInfo     = BuildRequestInfo(response),
                Links           = links,
                SecurityHeaders = secHeaders,
                Technologies    = techs,
                Subdomains      = subdomains,
                StatusCode      = (int)response.StatusCode,
                ContentType     = response.Content.Headers.ContentType?.ToString() ?? "",
                Timestamp       = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            return new BrowserResult
            {
                Html        = $"<!-- Erro na análise: {ex.Message} -->",
                CurlCommand = $"curl \"{url}\"",
                RequestInfo = $"Erro: {ex.Message}"
            };
        }
    }

    // ── Curl / Headers ───────────────────────────────────────────────────────

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

    // ── Classificação ────────────────────────────────────────────────────────

    public static NodeKind ClassifyKind(Uri uri, string originHost)
    {
        var host       = uri.Host.ToLowerInvariant();
        var bareHost   = host.StartsWith("www.")   ? host[4..]       : host;
        var bareOrigin = originHost.ToLowerInvariant();
        if (bareOrigin.StartsWith("www.")) bareOrigin = bareOrigin[4..];

        if (uri.AbsolutePath.Contains("/api/",   StringComparison.OrdinalIgnoreCase)
            || host.StartsWith("api.")
            || uri.AbsolutePath.Contains("graphql", StringComparison.OrdinalIgnoreCase)
            || uri.AbsolutePath.Contains("/rest/",  StringComparison.OrdinalIgnoreCase))
            return NodeKind.Api;

        if (IsMatch(bareHost, KnownTrackers))      return NodeKind.Tracker;
        if (IsMatch(bareHost, KnownCdns))          return NodeKind.Cdn;
        if (IsMatch(bareHost, SuspiciousPatterns)) return NodeKind.Suspicious;

        if (bareHost == bareOrigin || bareHost.EndsWith("." + bareOrigin))
            return NodeKind.Internal;

        return NodeKind.External;
    }

    private static bool IsMatch(string host, HashSet<string> set)
    {
        if (set.Contains(host)) return true;
        var parts = host.Split('.');
        for (int i = 1; i < parts.Length - 1; i++)
        {
            var suffix = string.Join('.', parts[i..]);
            if (set.Contains(suffix)) return true;
        }
        return false;
    }
}
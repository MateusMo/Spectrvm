using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Spectrvm.Models;

namespace Spectrvm.Services;

/// <summary>
/// Extrai links completos do HTML: absolutos, relativos, e todos os atributos relevantes.
/// Usa parsing manual sem dependências externas.
/// </summary>
public static partial class LinkExtractor
{
    // Atributos que podem conter URLs
    private static readonly (string attr, string type)[] UrlAttributes =
    [
        ("href",       "page"),
        ("src",        "asset"),
        ("action",     "form"),
        ("data-src",   "asset"),
        ("data-href",  "page"),
        ("data-url",   "page"),
        ("content",    "meta"),   // <meta http-equiv="refresh" content="0;url=...">
        ("srcset",     "asset"),  // múltiplas URLs separadas por vírgula
        ("poster",     "asset"),
        ("formaction", "form"),
    ];

    // CSS url(...) e @import
    [GeneratedRegex(@"url\(['""]?(https?://[^'""\)]+)['""]?\)", RegexOptions.IgnoreCase)]
    private static partial Regex CssUrlRegex();

    // srcset: "url 2x, url2 3x"
    [GeneratedRegex(@"(https?://[^\s,""']+)", RegexOptions.IgnoreCase)]
    private static partial Regex SrcSetPartRegex();

    // meta refresh
    [GeneratedRegex(@"url=(https?://[^'"";\s]+)", RegexOptions.IgnoreCase)]
    private static partial Regex MetaRefreshRegex();

    // Extrai valor de atributo HTML
    [GeneratedRegex(@"<[^>]+?\s([\w\-]+)\s*=\s*(?:""([^""]*)""|'([^']*)'|([^\s>]*))", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex AttrRegex();

    public static List<ExtractedLink> Extract(string html, string currentUrl, string originHost)
    {
        if (!Uri.TryCreate(currentUrl, UriKind.Absolute, out var baseUri))
            return [];

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<ExtractedLink>();

        void Add(string raw, string hintType)
        {
            if (string.IsNullOrWhiteSpace(raw)) return;
            raw = raw.Trim();

            // Resolve URL relativa
            if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri))
            {
                if (!Uri.TryCreate(baseUri, raw, out uri)) return;
            }

            // Filtra schemes não-HTTP
            if (uri.Scheme != "https" && uri.Scheme != "http") return;

            var normalized = uri.ToString();
            if (!seen.Add(normalized)) return;

            list.Add(new ExtractedLink
            {
                Url        = normalized,
                IsInternal = IsInternal(uri.Host, originHost),
                Type       = DetectType(normalized, hintType),
                Kind       = BrowserService.ClassifyKind(uri, originHost)
            });
        }

        // 1. Parse de atributos HTML via tag scanning
        foreach (Match tagMatch in TagRegex().Matches(html))
        {
            var tag = tagMatch.Value;

            foreach (var (attr, hintType) in UrlAttributes)
            {
                var val = GetAttrValue(tag, attr);
                if (val == null) continue;

                if (attr == "srcset")
                {
                    // srcset contém múltiplas entradas: "url 2x, url2 1x"
                    foreach (var part in val.Split(','))
                    {
                        var urlPart = part.Trim().Split(' ')[0];
                        Add(urlPart, hintType);
                    }
                }
                else if (attr == "content")
                {
                    // meta refresh: "0; url=https://..."
                    var m = MetaRefreshRegex().Match(val);
                    if (m.Success) Add(m.Groups[1].Value, hintType);
                }
                else
                {
                    Add(val, hintType);
                }
            }

            // <a data-*> e qualquer atributo com URL absoluta dentro de tags
            foreach (Match m in DataAttrUrlRegex().Matches(tag))
                Add(m.Groups[1].Value, "page");
        }

        // 2. URLs absolutas em CSS (url(...) e @import)
        foreach (Match m in CssUrlRegex().Matches(html))
            Add(m.Groups[1].Value, "css");

        // 3. URLs absolutas soltas em strings JS/JSON dentro do HTML
        foreach (Match m in JsUrlRegex().Matches(html))
            Add(m.Groups[1].Value, "js");

        return list;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string? GetAttrValue(string tag, string attr)
    {
        // Match: attr="val" | attr='val' | attr=val
        var pattern = $@"\b{Regex.Escape(attr)}\s*=\s*(?:""([^""]*)""|'([^']*)'|([^\s>""']+))";
        var m = Regex.Match(tag, pattern, RegexOptions.IgnoreCase);
        if (!m.Success) return null;
        return m.Groups[1].Value.Length > 0 ? m.Groups[1].Value
             : m.Groups[2].Value.Length > 0 ? m.Groups[2].Value
             : m.Groups[3].Value;
    }

    private static bool IsInternal(string host, string origin)
    {
        var h = host.ToLowerInvariant().TrimStart('w').TrimStart('w').TrimStart('w').TrimStart('.');
        var o = origin.ToLowerInvariant();
        if (o.StartsWith("www.")) o = o[4..];
        return h == o || h.EndsWith("." + o);
    }

    private static string DetectType(string url, string hint) => url switch
    {
        _ when url.EndsWith(".js",   StringComparison.OrdinalIgnoreCase) => "js",
        _ when url.EndsWith(".css",  StringComparison.OrdinalIgnoreCase) => "css",
        _ when url.EndsWith(".png",  StringComparison.OrdinalIgnoreCase)
            || url.EndsWith(".jpg",  StringComparison.OrdinalIgnoreCase)
            || url.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)
            || url.EndsWith(".svg",  StringComparison.OrdinalIgnoreCase)
            || url.EndsWith(".webp", StringComparison.OrdinalIgnoreCase)
            || url.EndsWith(".gif",  StringComparison.OrdinalIgnoreCase)
            || url.EndsWith(".ico",  StringComparison.OrdinalIgnoreCase) => "asset",
        _ when url.EndsWith(".woff",  StringComparison.OrdinalIgnoreCase)
            || url.EndsWith(".woff2", StringComparison.OrdinalIgnoreCase)
            || url.EndsWith(".ttf",   StringComparison.OrdinalIgnoreCase) => "font",
        _ when url.Contains("/api/",    StringComparison.OrdinalIgnoreCase)
            || url.Contains("graphql",  StringComparison.OrdinalIgnoreCase)
            || url.Contains("/rest/",   StringComparison.OrdinalIgnoreCase)
            || url.Contains("/v1/",     StringComparison.OrdinalIgnoreCase)
            || url.Contains("/v2/",     StringComparison.OrdinalIgnoreCase) => "api",
        _ when hint == "form" => "form",
        _ => "page"
    };

    // Tags HTML completas (self-closing e regulares)
    [GeneratedRegex(@"<[a-zA-Z][^>]*?>", RegexOptions.Singleline)]
    private static partial Regex TagRegex();

    // URLs absolutas em atributos data-*
    [GeneratedRegex(@"data-[\w\-]+=(?:""(https?://[^""]+)""|'(https?://[^']+)')", RegexOptions.IgnoreCase)]
    private static partial Regex DataAttrUrlRegex();

    // URLs absolutas em contexto JS/JSON (entre aspas)
    [GeneratedRegex(@"""(https?://[^""\\]{8,}[^""\\])""", RegexOptions.IgnoreCase)]
    private static partial Regex JsUrlRegex();
}
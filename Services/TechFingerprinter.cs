using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.RegularExpressions;
using Spectrvm.Models;

namespace Spectrvm.Services;

/// <summary>
/// Detecta tecnologias via headers HTTP, HTML, paths de assets e cookies.
/// Inspirado no Wappalyzer mas sem dependências externas.
/// </summary>
public static class TechFingerprinter
{
    private record Rule(
        string Name,
        string Category,
        string Icon,
        Func<FingerprintContext, string?> Detect);

    private sealed class FingerprintContext
    {
        public HttpResponseMessage Response  { get; init; } = null!;
        public string              Html      { get; init; } = "";
        public string              HeadersRaw { get; init; } = "";
    }

    // ── Regras ──────────────────────────────────────────────────────────────
    // Cada regra retorna a evidência (string) se detectada, ou null.

    private static readonly Rule[] Rules =
    [
        // ── CMS ──────────────────────────────────────────────────────────────
        new("WordPress", "CMS", "📰",
            ctx => HtmlContains(ctx.Html, "/wp-content/", "/wp-includes/")
                ?? MetaGenerator(ctx.Html, "WordPress")
                ?? Cookie(ctx.Response, "wordpress_")),

        new("Drupal", "CMS", "💧",
            ctx => HtmlContains(ctx.Html, "/sites/default/files/", "Drupal.settings")
                ?? MetaGenerator(ctx.Html, "Drupal")),

        new("Joomla", "CMS", "🔵",
            ctx => HtmlContains(ctx.Html, "/media/jui/", "/components/com_")
                ?? MetaGenerator(ctx.Html, "Joomla")),

        new("Ghost", "CMS", "👻",
            ctx => HtmlContains(ctx.Html, "/ghost/", "ghost.io")),

        new("Wix", "CMS / Site Builder", "🌐",
            ctx => HtmlContains(ctx.Html, "static.wixstatic.com", "wix.com/pages")),

        new("Squarespace", "CMS / Site Builder", "⬛",
            ctx => HtmlContains(ctx.Html, "squarespace.com", "sqsp.net")),

        new("Webflow", "CMS / Site Builder", "🌊",
            ctx => HtmlContains(ctx.Html, "webflow.com", "wf-form")),

        new("Shopify", "E-commerce", "🛒",
            ctx => HtmlContains(ctx.Html, "cdn.shopify.com", "Shopify.theme")
                ?? Cookie(ctx.Response, "_shopify_")),

        new("WooCommerce", "E-commerce", "🛍",
            ctx => HtmlContains(ctx.Html, "woocommerce", "/wc-api/")),

        new("Magento", "E-commerce", "🧲",
            ctx => HtmlContains(ctx.Html, "Mage.Cookies", "/skin/frontend/")
                ?? Cookie(ctx.Response, "frontend_")),

        new("PrestaShop", "E-commerce", "🐟",
            ctx => HtmlContains(ctx.Html, "prestashop", "/modules/blockcart/")),

        // ── Frameworks JS ─────────────────────────────────────────────────────
        new("React", "JavaScript Framework", "⚛",
            ctx => HtmlContains(ctx.Html, "react.production.min.js", "__react", "data-reactroot", "_reactFiber", "react-dom")),

        new("Vue.js", "JavaScript Framework", "💚",
            ctx => HtmlContains(ctx.Html, "vue.min.js", "vue.runtime", "__vue__", "v-bind:", "v-model=")),

        new("Angular", "JavaScript Framework", "🔺",
            ctx => HtmlContains(ctx.Html, "ng-version=", "angular.min.js", "@angular/core")),

        new("Next.js", "JavaScript Framework", "▲",
            ctx => HtmlContains(ctx.Html, "__NEXT_DATA__", "/_next/static/", "next/dist")),

        new("Nuxt.js", "JavaScript Framework", "💚",
            ctx => HtmlContains(ctx.Html, "__NUXT__", "/_nuxt/", "nuxt.config")),

        new("Svelte", "JavaScript Framework", "🔥",
            ctx => HtmlContains(ctx.Html, "__svelte", "svelte/internal")),

        new("Gatsby", "Static Site Generator", "💜",
            ctx => HtmlContains(ctx.Html, "___gatsby", "/page-data/app-data.json")),

        new("jQuery", "JavaScript Library", "🔷",
            ctx => HtmlContains(ctx.Html, "jquery.min.js", "jquery.js", "jQuery v")),

        new("Bootstrap", "CSS Framework", "🅱",
            ctx => HtmlContains(ctx.Html, "bootstrap.min.css", "bootstrap.css", "bootstrap.min.js", "bootstrap.bundle")),

        new("Tailwind CSS", "CSS Framework", "🌬",
            ctx => HtmlContains(ctx.Html, "tailwindcss", "cdn.tailwindcss.com")),

        new("HTMX", "JavaScript Library", "⚡",
            ctx => HtmlContains(ctx.Html, "htmx.org", "hx-get=", "hx-post=")),

        new("Alpine.js", "JavaScript Library", "🏔",
            ctx => HtmlContains(ctx.Html, "alpinejs", "x-data=")),

        // ── Servidores ───────────────────────────────────────────────────────
        new("Nginx", "Web Server", "🟩",
            ctx => HeaderContains(ctx.Response, "Server", "nginx")),

        new("Apache", "Web Server", "🪶",
            ctx => HeaderContains(ctx.Response, "Server", "Apache")),

        new("Caddy", "Web Server", "🦡",
            ctx => HeaderContains(ctx.Response, "Server", "Caddy")),

        new("IIS", "Web Server", "🪟",
            ctx => HeaderContains(ctx.Response, "Server", "Microsoft-IIS")),

        new("Cloudflare", "CDN / Proxy", "🌤",
            ctx => HeaderContains(ctx.Response, "Server", "cloudflare")
                ?? HeaderPresent(ctx.Response, "CF-Ray")),

        new("Fastly", "CDN", "⚡",
            ctx => HeaderPresent(ctx.Response, "X-Served-By") != null &&
                   HeaderContains(ctx.Response, "X-Served-By", "cache-") != null
                ? "X-Served-By header" : null),

        new("AWS CloudFront", "CDN", "☁",
            ctx => HeaderContains(ctx.Response, "X-Cache", "CloudFront")
                ?? HeaderContains(ctx.Response, "Via", "CloudFront")),

        // ── Plataformas / Backend ─────────────────────────────────────────────
        new("PHP", "Backend", "🐘",
            ctx => HeaderContains(ctx.Response, "X-Powered-By", "PHP")
                ?? Cookie(ctx.Response, "PHPSESSID")),

        new("ASP.NET", "Backend", "🔵",
            ctx => HeaderContains(ctx.Response, "X-Powered-By", "ASP.NET")
                ?? Cookie(ctx.Response, "ASP.NET_SessionId")),

        new("Ruby on Rails", "Backend", "💎",
            ctx => HeaderContains(ctx.Response, "X-Runtime", "")
                ?? Cookie(ctx.Response, "_rails_")),

        new("Django", "Backend", "🎸",
            ctx => Cookie(ctx.Response, "csrftoken")
                ?? Cookie(ctx.Response, "sessionid")),

        new("Laravel", "Backend", "🔺",
            ctx => Cookie(ctx.Response, "laravel_session")
                ?? Cookie(ctx.Response, "XSRF-TOKEN")),

        // ── Analytics / Tag Managers ─────────────────────────────────────────
        new("Google Analytics", "Analytics", "📊",
            ctx => HtmlContains(ctx.Html, "google-analytics.com/analytics.js", "gtag(", "UA-", "G-")),

        new("Google Tag Manager", "Tag Manager", "🏷",
            ctx => HtmlContains(ctx.Html, "googletagmanager.com/gtm.js", "GTM-")),

        new("Hotjar", "Analytics", "🔥",
            ctx => HtmlContains(ctx.Html, "hotjar.com", "hjSetting")),

        new("Meta Pixel", "Marketing", "👤",
            ctx => HtmlContains(ctx.Html, "connect.facebook.net/en_US/fbevents.js", "fbq(")),

        new("Intercom", "Support", "💬",
            ctx => HtmlContains(ctx.Html, "intercom.io", "Intercom(")),

        new("Hubspot", "Marketing", "🟠",
            ctx => HtmlContains(ctx.Html, "js.hs-scripts.com", "hubspot.com")),

        // ── Infraestrutura ───────────────────────────────────────────────────
        new("Vercel", "Hosting", "▲",
            ctx => HeaderPresent(ctx.Response, "x-vercel-id")
                ?? HeaderContains(ctx.Response, "Server", "Vercel")),

        new("Netlify", "Hosting", "🌐",
            ctx => HeaderPresent(ctx.Response, "x-nf-request-id")),

        new("GitHub Pages", "Hosting", "🐙",
            ctx => HeaderContains(ctx.Response, "Server", "GitHub.com")),

        new("Heroku", "Hosting", "💜",
            ctx => HeaderPresent(ctx.Response, "X-Request-Id") != null
                && HeaderContains(ctx.Response, "Via", "1.1 vegur") != null
                ? "Via: vegur" : null),
    ];

    // ── API pública ──────────────────────────────────────────────────────────

    public static List<DetectedTechnology> Detect(HttpResponseMessage response, string html)
    {
        var ctx = new FingerprintContext { Response = response, Html = html };
        var seen    = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var results = new List<DetectedTechnology>();

        foreach (var rule in Rules)
        {
            var evidence = rule.Detect(ctx);
            if (evidence != null && seen.Add(rule.Name))
            {
                results.Add(new DetectedTechnology
                {
                    Name     = rule.Name,
                    Category = rule.Category,
                    Icon     = rule.Icon,
                    Evidence = evidence
                });
            }
        }

        return results;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static string? HtmlContains(string html, params string[] patterns)
    {
        foreach (var p in patterns)
            if (html.Contains(p, StringComparison.OrdinalIgnoreCase))
                return $"encontrado: {p}";
        return null;
    }

    private static string? HeaderContains(HttpResponseMessage resp, string header, string value)
    {
        if (resp.Headers.TryGetValues(header, out var vals))
            foreach (var v in vals)
                if (v.Contains(value, StringComparison.OrdinalIgnoreCase))
                    return $"{header}: {v}";

        // Tenta também Content headers
        if (resp.Content?.Headers.TryGetValues(header, out var cVals) == true)
            foreach (var v in cVals)
                if (v.Contains(value, StringComparison.OrdinalIgnoreCase))
                    return $"{header}: {v}";

        return null;
    }

    private static string? HeaderPresent(HttpResponseMessage resp, string header)
    {
        if (resp.Headers.TryGetValues(header, out var vals))
            return $"{header}: {string.Join(", ", vals)}";
        return null;
    }

    private static string? Cookie(HttpResponseMessage resp, string prefix)
    {
        if (!resp.Headers.TryGetValues("Set-Cookie", out var cookies)) return null;
        foreach (var c in cookies)
            if (c.Contains(prefix, StringComparison.OrdinalIgnoreCase))
                return $"cookie: {c[..Math.Min(40, c.Length)]}…";
        return null;
    }

    private static string? MetaGenerator(string html, string name)
    {
        var m = Regex.Match(html, @"<meta[^>]+name=[""']generator[""'][^>]+content=[""']([^""']+)[""']",
            RegexOptions.IgnoreCase);
        if (!m.Success)
            m = Regex.Match(html, @"<meta[^>]+content=[""']([^""']+)[""'][^>]+name=[""']generator[""']",
                RegexOptions.IgnoreCase);
        if (m.Success && m.Groups[1].Value.Contains(name, StringComparison.OrdinalIgnoreCase))
            return $"meta generator: {m.Groups[1].Value}";
        return null;
    }
}
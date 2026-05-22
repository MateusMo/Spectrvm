using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using Spectrvm.Models;

namespace Spectrvm.Services;

public static class SecurityHeaderAnalyzer
{
    public static List<SecurityHeaderResult> Analyze(HttpResponseMessage response)
    {
        var results = new List<SecurityHeaderResult>();
        var headers = response.Headers;

        // ── Strict-Transport-Security (HSTS) ─────────────────────────────────
        Check(results, headers, "Strict-Transport-Security",
            missing: ("HSTS ausente — site vulnerável a downgrade para HTTP", SecurityLevel.Warning),
            present: v =>
            {
                if (v.Contains("max-age=0")) return ("HSTS desativado (max-age=0)", SecurityLevel.Bad);
                if (!v.Contains("includeSubDomains")) return ("HSTS sem includeSubDomains", SecurityLevel.Warning);
                return ("HSTS configurado corretamente", SecurityLevel.Good);
            });

        // ── Content-Security-Policy ───────────────────────────────────────────
        Check(results, headers, "Content-Security-Policy",
            missing: ("CSP ausente — XSS sem proteção de política", SecurityLevel.Bad),
            present: v =>
            {
                if (v.Contains("unsafe-inline") && v.Contains("unsafe-eval"))
                    return ("CSP presente mas muito permissivo (unsafe-inline + unsafe-eval)", SecurityLevel.Warning);
                if (v.Contains("unsafe-inline"))
                    return ("CSP presente mas com unsafe-inline", SecurityLevel.Warning);
                return ("CSP configurado", SecurityLevel.Good);
            });

        // ── X-Frame-Options ───────────────────────────────────────────────────
        Check(results, headers, "X-Frame-Options",
            missing: ("X-Frame-Options ausente — site pode ser embutido (clickjacking)", SecurityLevel.Warning),
            present: v =>
            {
                var upper = v.ToUpperInvariant();
                if (upper is "DENY" or "SAMEORIGIN") return ("X-Frame-Options configurado corretamente", SecurityLevel.Good);
                return ($"X-Frame-Options com valor incomum: {v}", SecurityLevel.Info);
            });

        // ── X-Content-Type-Options ────────────────────────────────────────────
        Check(results, headers, "X-Content-Type-Options",
            missing: ("X-Content-Type-Options ausente — MIME sniffing habilitado", SecurityLevel.Warning),
            present: v => v.Contains("nosniff")
                ? ("nosniff configurado", SecurityLevel.Good)
                : ($"Valor inesperado: {v}", SecurityLevel.Info));

        // ── Referrer-Policy ───────────────────────────────────────────────────
        Check(results, headers, "Referrer-Policy",
            missing: ("Referrer-Policy ausente — URLs podem vazar para terceiros", SecurityLevel.Info),
            present: v =>
            {
                var safe = v is "no-referrer" or "strict-origin" or "strict-origin-when-cross-origin" or "same-origin";
                return safe ? ("Referrer-Policy seguro", SecurityLevel.Good)
                            : ($"Referrer-Policy: {v}", SecurityLevel.Info);
            });

        // ── Permissions-Policy ────────────────────────────────────────────────
        Check(results, headers, "Permissions-Policy",
            missing: ("Permissions-Policy ausente — câmera/mic/geo sem restrição explícita", SecurityLevel.Info),
            present: _ => ("Permissions-Policy definida", SecurityLevel.Good));

        // ── X-XSS-Protection (legado) ─────────────────────────────────────────
        Check(results, headers, "X-XSS-Protection",
            missing: null, // ausência não é problemática com CSP moderno
            present: v =>
            {
                if (v.StartsWith("0")) return ("XSS filter desativado explicitamente", SecurityLevel.Info);
                return ("X-XSS-Protection presente (legado)", SecurityLevel.Info);
            });

        // ── Server (vaza versão) ───────────────────────────────────────────────
        Check(results, headers, "Server",
            missing: null,
            present: v =>
            {
                bool leaks = System.Text.RegularExpressions.Regex.IsMatch(v, @"\d");
                return leaks
                    ? ($"Server header vaza versão: {v}", SecurityLevel.Warning)
                    : ($"Server: {v}", SecurityLevel.Info);
            });

        // ── X-Powered-By ──────────────────────────────────────────────────────
        Check(results, headers, "X-Powered-By",
            missing: null,
            present: v => ($"Tecnologia exposta via X-Powered-By: {v}", SecurityLevel.Warning));

        // ── Cache-Control para respostas sensíveis ────────────────────────────
        Check(results, headers, "Cache-Control",
            missing: ("Cache-Control ausente", SecurityLevel.Info),
            present: v => (v, SecurityLevel.Info));

        // ── CORS (Access-Control-Allow-Origin) ────────────────────────────────
        Check(results, headers, "Access-Control-Allow-Origin",
            missing: null,
            present: v =>
            {
                if (v == "*") return ("CORS aberto para qualquer origem (*)", SecurityLevel.Warning);
                return ($"CORS restrito a: {v}", SecurityLevel.Good);
            });

        return results;
    }

    private static void Check(
        List<SecurityHeaderResult> results,
        HttpResponseHeaders         headers,
        string                      header,
        (string desc, SecurityLevel level)? missing,
        Func<string, (string desc, SecurityLevel level)>? present)
    {
        if (headers.TryGetValues(header, out var vals))
        {
            var value = string.Join(", ", vals);
            if (present != null)
            {
                var (desc, level) = present(value);
                results.Add(new SecurityHeaderResult { Header = header, Value = value, Level = level, Description = desc });
            }
        }
        else if (missing.HasValue)
        {
            results.Add(new SecurityHeaderResult
            {
                Header      = header,
                Value       = "—",
                Level       = missing.Value.level,
                Description = missing.Value.desc
            });
        }
    }
}
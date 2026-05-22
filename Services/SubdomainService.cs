using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace Spectrvm.Services;

public class SubdomainService
{
    private readonly HttpClient _http = new();

    /// <summary>
    /// Consulta crt.sh e retorna subdomínios únicos do domínio informado.
    /// </summary>
    public async Task<List<string>> FetchAsync(string domain)
    {
        // Remove www e subdomínios para pegar o domínio raiz
        var root = ExtractRootDomain(domain);

        try
        {
            var url = $"https://crt.sh/?q=%.{root}&output=json";
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("Spectrvm/1.0");
            var json = await _http.GetStringAsync(url);

            using var doc = JsonDocument.Parse(json);
            var seen   = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var result = new List<string>();

            foreach (var entry in doc.RootElement.EnumerateArray())
            {
                // Cada entrada pode ter múltiplos nomes separados por \n
                if (!entry.TryGetProperty("name_value", out var nameProp)) continue;
                var names = nameProp.GetString() ?? "";
                foreach (var name in names.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    var clean = name.Trim().TrimStart('*').TrimStart('.');
                    if (clean.Length == 0) continue;
                    if (!clean.EndsWith(root, StringComparison.OrdinalIgnoreCase)) continue;
                    if (seen.Add(clean))
                        result.Add(clean);
                }
            }

            return result.Order().ToList();
        }
        catch
        {
            return [];
        }
    }

    public static string ExtractRootDomain(string host)
    {
        // Remove protocolo se presente
        if (host.Contains("://"))
        {
            if (Uri.TryCreate(host, UriKind.Absolute, out var u))
                host = u.Host;
        }

        host = host.ToLowerInvariant();
        if (host.StartsWith("www.")) host = host[4..];

        // Domínios com TLD duplo (co.uk, com.br, etc.) — heurística simples
        var parts = host.Split('.');
        if (parts.Length >= 3)
        {
            var tld2 = parts[^2] + "." + parts[^1];
            bool isCompound = tld2 is "co.uk" or "com.br" or "org.br" or "net.br"
                                   or "co.jp" or "co.nz" or "com.au" or "org.uk";
            if (isCompound && parts.Length >= 3)
                return parts[^3] + "." + tld2;
        }

        return parts.Length >= 2 ? parts[^2] + "." + parts[^1] : host;
    }
}
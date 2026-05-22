namespace Spectrvm.Models;

public enum NodeKind
{
    /// <summary>Página navegada diretamente pelo usuário.</summary>
    Primary,

    /// <summary>Link interno ao mesmo domínio.</summary>
    Internal,

    /// <summary>Link externo (outro domínio, mas não classificado abaixo).</summary>
    External,

    /// <summary>Script / recurso de terceiro (analytics, CDN, ads).</summary>
    Dependency,

    /// <summary>Tracker conhecido (analytics, pixels de rastreamento).</summary>
    Tracker,

    /// <summary>CDN detectado (cloudflare, akamai, fastly, etc.).</summary>
    Cdn,

    /// <summary>Endpoint de API (/api/, graphql, rest, etc.).</summary>
    Api,

    /// <summary>Domínio suspeito (typosquatting, redirect estranho).</summary>
    Suspicious,

    /// <summary>Subdomínio descoberto via crt.sh.</summary>
    Subdomain,
}
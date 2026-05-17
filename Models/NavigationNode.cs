using System;
using System.Collections.Generic;

namespace Spectrvm.Models;

/// <summary>
/// Classificação visual do nó — determina cor, tamanho e comportamento no grafo.
/// </summary>
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
    Suspicious
}

public class NavigationNode
{
    public Guid   Id          { get; set; } = Guid.NewGuid();
    public string Url         { get; set; } = "";
    public double X           { get; set; }
    public double Y           { get; set; }
    public bool   IsPrimary   { get; set; }
    public NodeKind Kind      { get; set; } = NodeKind.External;

    /// <summary>Y do nó primário pai — usado pelo canvas para posicionar o label.</summary>
    public double OrbitParentY { get; set; }

    /// <summary>
    /// Referência ao nó pai direto (primário ou órbita clicada).
    /// Substitui a lógica anterior onde todas as órbitas eram filhas do mesmo primário.
    /// </summary>
    public NavigationNode? Parent { get; set; }

    /// <summary>Próximo nó na cadeia principal de navegação.</summary>
    public NavigationNode? Next { get; set; }

    public List<NavigationNode> OrbitNodes { get; set; } = new();
}
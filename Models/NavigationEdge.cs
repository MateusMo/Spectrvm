namespace Spectrvm.Models;

/// <summary>
/// Aresta de navegação entre dois nós do grafo.
/// Substituiu a lógica de Node.Next/Node.Parent para renderização,
/// porque arestas saindo de orbitais (não-primários) precisam ser registradas
/// explicitamente — eles não fazem parte do loop de primários do render.
/// </summary>
public class NavigationEdge
{
    /// <summary>Nó de origem da aresta (pode ser primário ou orbital).</summary>
    public NavigationNode Source { get; set; } = null!;

    /// <summary>Nó de destino — sempre um nó primário.</summary>
    public NavigationNode Target { get; set; } = null!;

    /// <summary>
    /// True quando a origem é um nó orbital (clique de sublink).
    /// False quando a origem é um nó primário (cadeia por digitação de URL).
    /// </summary>
    public bool ViaOrbit { get; set; }
}
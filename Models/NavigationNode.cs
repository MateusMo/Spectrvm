using System;
using System.Collections.Generic;

namespace Spectrvm.Models;

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
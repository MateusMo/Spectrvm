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

    /// <summary>Y do nó primário pai — usado pelo canvas para posicionar o label sem sobreposição.</summary>
    public double OrbitParentY { get; set; }

    public NavigationNode?       Next       { get; set; }
    public List<NavigationNode>  OrbitNodes { get; set; } = new();
}
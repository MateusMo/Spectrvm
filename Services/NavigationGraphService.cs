using System;
using System.Collections.Generic;
using Spectrvm.Models;

namespace Spectrvm.Services;

public class NavigationGraphService
{
    private const double ChainY       = 500;   // mais para baixo para dar espaço às órbitas acima
    private const double ChainSpacing = 360;
    private const double OrbitRadius  = 160;
    private const int    MaxOrbit     = 14;    // menos nós = menos sobreposição

    public List<NavigationNode> AppendNavigation(
        List<NavigationNode> existing,
        string url,
        List<ExtractedLink> links)
    {
        var chain = new List<NavigationNode>();
        foreach (var n in existing)
            if (n.IsPrimary) chain.Add(n);

        double x = chain.Count == 0 ? 220 : chain[^1].X + ChainSpacing;

        var primary = new NavigationNode
        {
            Url       = url,
            X         = x,
            Y         = ChainY,
            IsPrimary = true
        };

        if (chain.Count > 0) chain[^1].Next = primary;

        var orbitLinks = links.Count > MaxOrbit ? links.GetRange(0, MaxOrbit) : links;
        double step    = orbitLinks.Count > 0 ? (2 * Math.PI) / orbitLinks.Count : 0;

        for (int i = 0; i < orbitLinks.Count; i++)
        {
            // Começa no topo (-π/2) e distribui em sentido horário
            double angle = i * step - Math.PI / 2;
            double ox    = primary.X + OrbitRadius * Math.Cos(angle);
            double oy    = primary.Y + OrbitRadius * Math.Sin(angle);

            primary.OrbitNodes.Add(new NavigationNode
            {
                Url          = orbitLinks[i].Url,
                X            = ox,
                Y            = oy,
                IsPrimary    = false,
                OrbitParentY = primary.Y
            });
        }

        var all = new List<NavigationNode>(existing) { primary };
        all.AddRange(primary.OrbitNodes);
        return all;
    }
}
using System;
using System.Collections.Generic;
using System.Linq;
using Spectrvm.Models;

namespace Spectrvm.Services;

public class NavigationGraphService
{
    private const double MinPrimaryGap   = 420;
    private const double BaseOrbitRadius = 160;
    private const double MinOrbitArcGap  = 38;
    private const double ClusterMargin   = 60;

    // ── API pública ──────────────────────────────────────────────────────────

    /// <summary>
    /// Adiciona um novo nó primário, suas órbitas de links e seus subdomínios ao grafo.
    /// </summary>
    public List<NavigationNode> AppendNavigation(
        List<NavigationNode> existing,
        List<NavigationEdge> edges,
        string               url,
        List<ExtractedLink>  links,
        List<string>         subdomains,
        NavigationNode?      parentNode = null)
    {
        double maxOrbitRadius = ComputeMaxOrbitRadius(links.Count);
        (double px, double py) = FindFreePosition(existing, parentNode, maxOrbitRadius);

        var primary = new NavigationNode
        {
            Url       = url,
            X         = px,
            Y         = py,
            IsPrimary = true,
            Kind      = NodeKind.Primary,
            Parent    = parentNode
        };

        // ── Aresta de navegação ──────────────────────────────────────────────
        if (parentNode != null)
        {
            edges.Add(new NavigationEdge
            {
                Source   = parentNode,
                Target   = primary,
                ViaOrbit = !parentNode.IsPrimary
            });
        }
        else
        {
            var chain = CollectPrimaryChain(existing);
            if (chain.Count > 0)
                edges.Add(new NavigationEdge { Source = chain[^1], Target = primary, ViaOrbit = false });
        }

        // ── Órbitas de links ────────────────────────────────────────────────
        BuildOrbits(primary, links, parentNode);

        // ── Órbitas de subdomínios ───────────────────────────────────────────
        // Deduplica: remove subdomínios que já existem como nó primário no grafo
        var existingUrls = new HashSet<string>(
            existing.Where(n => n.IsPrimary).Select(n =>
            {
                if (Uri.TryCreate(n.Url, UriKind.Absolute, out var u)) return u.Host.ToLowerInvariant();
                return n.Url.ToLowerInvariant();
            }),
            StringComparer.OrdinalIgnoreCase);

        // Também deduplica entre si (crt.sh pode retornar variações)
        var seenSubs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        BuildSubdomainOrbits(primary, subdomains, existingUrls, seenSubs);

        var all = new List<NavigationNode>(existing) { primary };
        all.AddRange(primary.OrbitNodes);
        return all;
    }

    // ── Posicionamento ───────────────────────────────────────────────────────

    private static (double x, double y) FindFreePosition(
        List<NavigationNode> existing,
        NavigationNode?      parent,
        double               requiredRadius)
    {
        double anchorX, anchorY;

        if (parent != null)
        {
            anchorX = parent.X;
            anchorY = parent.Y;
        }
        else
        {
            var chain = CollectPrimaryChain(existing);
            if (chain.Count == 0) return (220, 500);
            anchorX = chain[^1].X;
            anchorY = chain[^1].Y;
        }

        double[] angles = GenerateCandidateAngles();
        double   dist   = Math.Max(MinPrimaryGap, requiredRadius + ClusterMargin + 80);

        foreach (double angle in angles)
        {
            for (int push = 0; push <= 5; push++)
            {
                double cx = anchorX + (dist + push * 140) * Math.Cos(angle);
                double cy = anchorY + (dist + push * 140) * Math.Sin(angle);
                if (IsFree(cx, cy, requiredRadius + ClusterMargin, existing))
                    return (cx, cy);
            }
        }

        double maxX = 220;
        foreach (var n in existing) if (n.X > maxX) maxX = n.X;
        return (maxX + MinPrimaryGap + requiredRadius, anchorY);
    }

    private static double[] GenerateCandidateAngles()
    {
        var list = new List<double> { 0 };
        for (int deg = 15; deg <= 180; deg += 15)
        {
            list.Add( deg * Math.PI / 180.0);
            list.Add(-deg * Math.PI / 180.0);
        }
        return list.ToArray();
    }

    private static bool IsFree(double cx, double cy, double r, List<NavigationNode> existing)
    {
        foreach (var n in existing)
        {
            if (!n.IsPrimary) continue;
            double clusterR = ClusterRadius(n) + r + ClusterMargin;
            double dx = cx - n.X, dy = cy - n.Y;
            if (dx * dx + dy * dy < clusterR * clusterR) return false;
        }
        return true;
    }

    private static double ClusterRadius(NavigationNode primary)
    {
        double max = BaseOrbitRadius;
        foreach (var orbit in primary.OrbitNodes)
        {
            double dx = orbit.X - primary.X;
            double dy = orbit.Y - primary.Y;
            double d  = Math.Sqrt(dx * dx + dy * dy);
            if (d > max) max = d;
        }
        return max + 32;
    }

    // ── Órbitas de links ─────────────────────────────────────────────────────

    private static void BuildOrbits(
        NavigationNode      primary,
        List<ExtractedLink> links,
        NavigationNode?     parent)
    {
        if (links.Count == 0) return;

        double incomingAngle = parent != null
            ? Math.Atan2(parent.Y - primary.Y, parent.X - primary.X)
            : Math.PI;

        double openArc   = 300.0 * Math.PI / 180.0;
        double centerDir = incomingAngle + Math.PI;

        int linkIdx = 0, layer = 0;
        while (linkIdx < links.Count)
        {
            layer++;
            double radius   = BaseOrbitRadius * layer;
            double circum   = 2 * Math.PI * radius;
            int    capacity = Math.Max(6, (int)(circum / MinOrbitArcGap));
            int    count    = Math.Min(capacity, links.Count - linkIdx);
            double startAngle = centerDir - openArc / 2.0;
            double step       = count > 1 ? openArc / (count - 1) : 0;

            for (int i = 0; i < count; i++, linkIdx++)
            {
                double angle = count == 1 ? centerDir : startAngle + i * step;
                double ox = primary.X + radius * Math.Cos(angle);
                double oy = primary.Y + radius * Math.Sin(angle);

                primary.OrbitNodes.Add(new NavigationNode
                {
                    Url          = links[linkIdx].Url,
                    X            = ox,
                    Y            = oy,
                    IsPrimary    = false,
                    Kind         = links[linkIdx].Kind,
                    OrbitParentY = primary.Y,
                    Parent       = primary
                });
            }
        }
    }

    // ── Órbitas de subdomínios ───────────────────────────────────────────────

    private static void BuildSubdomainOrbits(
        NavigationNode     primary,
        List<string>       subdomains,
        HashSet<string>    existingHosts,
        HashSet<string>    seen)
    {
        if (subdomains.Count == 0) return;

        // Filtra duplicatas e hosts já no grafo
        var filtered = subdomains
            .Where(s => seen.Add(s) && !existingHosts.Contains(s))
            .ToList();

        if (filtered.Count == 0) return;

        // Anel dedicado para subdomínios — fica ACIMA do nó primário (ângulo de topo)
        // Usa uma camada extra acima das órbitas de links
        int    existingLayers = CountOrbitLayers(primary);
        double radius = BaseOrbitRadius * (existingLayers + 1);

        // Subdomínios em arco no hemisfério superior (0° a 180° = topo)
        double openArc   = 160.0 * Math.PI / 180.0;
        double centerDir = -Math.PI / 2; // aponta para cima
        int    count     = filtered.Count;
        double startAngle = centerDir - openArc / 2.0;
        double step       = count > 1 ? openArc / (count - 1) : 0;

        for (int i = 0; i < count; i++)
        {
            double angle = count == 1 ? centerDir : startAngle + i * step;
            double ox = primary.X + radius * Math.Cos(angle);
            double oy = primary.Y + radius * Math.Sin(angle);

            primary.OrbitNodes.Add(new NavigationNode
            {
                Url          = "https://" + filtered[i],
                X            = ox,
                Y            = oy,
                IsPrimary    = false,
                Kind         = NodeKind.Subdomain,
                OrbitParentY = primary.Y,
                Parent       = primary
            });
        }
    }

    private static int CountOrbitLayers(NavigationNode primary)
    {
        if (primary.OrbitNodes.Count == 0) return 0;
        double maxDist = 0;
        foreach (var o in primary.OrbitNodes)
        {
            double dx = o.X - primary.X, dy = o.Y - primary.Y;
            double d = Math.Sqrt(dx * dx + dy * dy);
            if (d > maxDist) maxDist = d;
        }
        return Math.Max(1, (int)Math.Round(maxDist / BaseOrbitRadius));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static List<NavigationNode> CollectPrimaryChain(List<NavigationNode> nodes)
    {
        var chain = new List<NavigationNode>();
        foreach (var n in nodes)
            if (n.IsPrimary) chain.Add(n);
        return chain;
    }

    private static double ComputeMaxOrbitRadius(int linkCount)
    {
        if (linkCount == 0) return BaseOrbitRadius;
        int layer = 0, remaining = linkCount;
        while (remaining > 0)
        {
            layer++;
            double radius   = BaseOrbitRadius * layer;
            double circum   = 2 * Math.PI * radius;
            int    capacity = Math.Max(6, (int)(circum / MinOrbitArcGap));
            remaining -= capacity;
        }
        return BaseOrbitRadius * layer + 32;
    }
}
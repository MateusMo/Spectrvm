using System;
using System.Collections.Generic;
using Spectrvm.Models;

namespace Spectrvm.Services;

public class NavigationGraphService
{
    // Distância mínima entre centros de nós primários
    private const double MinPrimaryGap   = 420;

    // Raio base da primeira camada de órbita (pixels world-space)
    private const double BaseOrbitRadius = 160;

    // Espaçamento mínimo entre nós de órbita (arco world-space)
    private const double MinOrbitArcGap  = 38;

    // Margem de segurança para detecção de colisão entre clusters
    private const double ClusterMargin   = 60;

    // ── API pública ──────────────────────────────────────────────────────────

    /// <summary>
    /// Adiciona um novo nó primário ao grafo sem sobrepor nenhum nó existente.
    /// Se <paramref name="parentNode"/> for informado, o novo nó nasce a partir dele
    /// (navegação por clique de órbita); caso contrário nasce no final da cadeia principal.
    /// </summary>
    public List<NavigationNode> AppendNavigation(
        List<NavigationNode> existing,
        string               url,
        List<ExtractedLink>  links,
        NavigationNode?      parentNode = null)
    {
        // Calcula raio máximo necessário para as órbitas do novo nó
        double maxOrbitRadius = ComputeMaxOrbitRadius(links.Count);

        // Encontra posição livre para o novo nó primário
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

        // Conecta na cadeia de navegação
        if (parentNode != null)
            parentNode.Next = primary;
        else
        {
            var chain = CollectChain(existing);
            if (chain.Count > 0) chain[^1].Next = primary;
        }

        // Distribui órbitas sem colidir com nós existentes
        BuildOrbits(primary, links, parentNode);

        var all = new List<NavigationNode>(existing) { primary };
        all.AddRange(primary.OrbitNodes);
        return all;
    }

    // ── Posicionamento do nó primário ─────────────────────────────────────────

    /// <summary>
    /// Encontra o primeiro ponto livre à direita do pai (ou da cadeia)
    /// que não colida com nenhum cluster existente.
    /// </summary>
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
            var chain = CollectChain(existing);
            if (chain.Count == 0)
                return (220, 500);

            var last = chain[^1];
            anchorX  = last.X;
            anchorY  = last.Y;
        }

        // Candidatos em leque: direita pura primeiro, depois abre em ±15° incrementos
        double[] angles = GenerateCandidateAngles();
        double   dist   = Math.Max(MinPrimaryGap, requiredRadius + ClusterMargin + 80);

        foreach (double angle in angles)
        {
            // Tenta distâncias crescentes neste ângulo
            for (int push = 0; push <= 5; push++)
            {
                double cx = anchorX + (dist + push * 140) * Math.Cos(angle);
                double cy = anchorY + (dist + push * 140) * Math.Sin(angle);

                if (IsFree(cx, cy, requiredRadius + ClusterMargin, existing))
                    return (cx, cy);
            }
        }

        // Fallback: vai bem para a direita além de tudo
        double maxX = 220;
        foreach (var n in existing)
            if (n.X > maxX) maxX = n.X;

        return (maxX + MinPrimaryGap + requiredRadius, anchorY);
    }

    /// <summary>
    /// Ângulos candidatos em ordem de preferência:
    /// direita → leque em ±15° incrementos até cobrir 360°
    /// </summary>
    private static double[] GenerateCandidateAngles()
    {
        var list = new List<double> { 0 }; // direita pura primeiro
        for (int deg = 15; deg <= 180; deg += 15)
        {
            list.Add( deg * Math.PI / 180.0);
            list.Add(-deg * Math.PI / 180.0);
        }
        return list.ToArray();
    }

    /// <summary>
    /// Verifica se um círculo de raio <paramref name="r"/> centrado em (cx, cy)
    /// não colide com nenhum cluster existente.
    /// </summary>
    private static bool IsFree(double cx, double cy, double r, List<NavigationNode> existing)
    {
        foreach (var n in existing)
        {
            if (!n.IsPrimary) continue;

            double clusterR = ClusterRadius(n) + r + ClusterMargin;
            double dx = cx - n.X;
            double dy = cy - n.Y;
            if (dx * dx + dy * dy < clusterR * clusterR)
                return false;
        }
        return true;
    }

    /// <summary>Raio efetivo de um cluster (distância até a órbita mais distante + margem).</summary>
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
        return max + 32; // margem extra para labels
    }

    // ── Layout de órbitas ─────────────────────────────────────────────────────

    /// <summary>
    /// Distribui os links em camadas concêntricas.
    /// A direção do semicírculo é oposta ao nó pai para não invadir o cluster anterior.
    /// </summary>
    private static void BuildOrbits(
        NavigationNode      primary,
        List<ExtractedLink> links,
        NavigationNode?     parent)
    {
        if (links.Count == 0) return;

        // Ângulo "de onde viemos" (aponta para o pai)
        double incomingAngle = parent != null
            ? Math.Atan2(parent.Y - primary.Y, parent.X - primary.X)
            : Math.PI; // sem pai: trata esquerda como direção proibida

        // Abre o leque de órbitas na direção oposta ao pai
        // Deixa 60° de zona morta apontando pro pai para não encostar na aresta
        double openArc   = 300.0 * Math.PI / 180.0;
        double centerDir = incomingAngle + Math.PI; // direção livre

        int linkIdx = 0;
        int layer   = 0;

        while (linkIdx < links.Count)
        {
            layer++;
            double radius    = BaseOrbitRadius * layer;
            double circum    = 2 * Math.PI * radius;
            int    capacity  = Math.Max(6, (int)(circum / MinOrbitArcGap));
            int    count     = Math.Min(capacity, links.Count - linkIdx);

            double startAngle = centerDir - openArc / 2.0;
            double step       = count > 1 ? openArc / (count - 1) : 0;

            for (int i = 0; i < count; i++, linkIdx++)
            {
                double angle = count == 1
                    ? centerDir
                    : startAngle + i * step;

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

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static List<NavigationNode> CollectChain(List<NavigationNode> nodes)
    {
        var chain = new List<NavigationNode>();
        foreach (var n in nodes)
            if (n.IsPrimary) chain.Add(n);
        return chain;
    }

    /// <summary>Raio máximo que N links ocuparão em camadas concêntricas.</summary>
    private static double ComputeMaxOrbitRadius(int linkCount)
    {
        if (linkCount == 0) return BaseOrbitRadius;

        int    layer     = 0;
        int    remaining = linkCount;
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
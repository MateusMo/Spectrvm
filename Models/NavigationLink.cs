namespace Spectrvm.Models;

public class NavigationLink
{
    public NavigationNode? Source { get; set; }

    public NavigationNode? Target { get; set; }

    public bool IsPrimary { get; set; }
}
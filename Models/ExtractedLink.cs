namespace Spectrvm.Models;

public class ExtractedLink
{
    public string Url  { get; set; } = "";
    public bool IsInternal { get; set; }
    public string Type { get; set; } = "page";
}
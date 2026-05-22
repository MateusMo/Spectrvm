namespace Spectrvm.Models;

public enum SecurityLevel { Good, Warning, Bad, Info }

public class SecurityHeaderResult
{
    public string        Header      { get; set; } = "";
    public string        Value       { get; set; } = "";
    public SecurityLevel Level       { get; set; }
    public string        Description { get; set; } = "";
}
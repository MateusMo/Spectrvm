using System;
using System.Collections.Generic;

namespace Spectrvm.Models;

public class BrowserResult
{
    public string Html         { get; set; } = "";
    public string CurlCommand  { get; set; } = "";
    public string RequestInfo  { get; set; } = "";
    public List<ExtractedLink>        Links            { get; set; } = new();
    public List<string>               Subdomains       { get; set; } = new();
    public List<SecurityHeaderResult> SecurityHeaders  { get; set; } = new();
    public List<DetectedTechnology>   Technologies     { get; set; } = new();
    public DateTime Timestamp  { get; set; } = DateTime.UtcNow;
    public int      StatusCode { get; set; }
    public string   ContentType { get; set; } = "";
}
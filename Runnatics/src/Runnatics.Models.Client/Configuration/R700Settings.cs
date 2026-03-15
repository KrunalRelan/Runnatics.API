// ============================================================================
// File: Configuration/R700Settings.cs
// ============================================================================

namespace Runnatics.Models.Client.Configuration;

public class R700Settings
{
    public string Username { get; set; } = "root";
    public string Password { get; set; } = "impinj";
    public bool UseHttps { get; set; } = true;
    public int TimeoutSeconds { get; set; } = 10;
}

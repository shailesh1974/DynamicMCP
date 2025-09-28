
namespace DynamicMCP;

public class ParamConfig
{
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Type { get; set; } = "string"; // e.g., string, integer, boolean
    public bool? Required { get; set; } = false; // Whether the parameter is required
    public string? Default { get; set; } // Default value if not provided
    public List<string>? Enum { get; set; } // Regex pattern for validation
}
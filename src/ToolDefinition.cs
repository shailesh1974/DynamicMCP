namespace DynamicMCP;
public class ToolDefinition
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Title { get; internal set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Endpoint { get; set; } = string.Empty;
    public string HttpMethod { get; set; } = "POST";
    public int TimeoutSeconds { get; set; } = 30;
    public Dictionary<string, string> Auth { get; set; } = [];
    public Dictionary<string, string> Headers { get; set; } = [];
    public Dictionary<string, string> QueryParameters { get; set; } = [];
    public string? BodyTemplate { get; set; }
    public Dictionary<string, ParamConfig> ParameterMappings { get; set; } = [];
    public bool Enabled { get; set; } = true;
}

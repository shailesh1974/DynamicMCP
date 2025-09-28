using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace DynamicMCP;

internal class McpHandlers : IMcpHandlers
{
    private readonly Dictionary<string, Dictionary<string, ToolDefinition>> _server_tools = [];

    private readonly JsonSerializerOptions _options = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public async ValueTask<ListToolsResult> HandleListToolAsync(RequestContext<ListToolsRequestParams> context, CancellationToken token)
    {
        var httpContextAccessor = context.Services?.GetRequiredService<IHttpContextAccessor>();
        var routeName = httpContextAccessor?.HttpContext?.Request.Path.Value ?? "tools";
        routeName = routeName.Replace("/mcp/", "");
        string toolFileName = $"{routeName}_tools.json";

        if (!_server_tools.ContainsKey(routeName))
        {
            _server_tools[routeName] = [];
        }
        else
        {
            _server_tools[routeName].Clear();
        }

        var _tools = _server_tools[routeName];

        if (File.Exists(toolFileName))
        {
            var toolFileContent = File.ReadAllText(toolFileName);

            var resultFromFile = JsonSerializer.Deserialize<Dictionary<string, List<ToolDefinition>>>(toolFileContent, _options);

            if (resultFromFile != null)
            {
                foreach (var tool in resultFromFile["tools"])
                {
                    if(tool.Enabled) _tools[tool.Name] = tool;
                }
                _server_tools[routeName] = _tools;
                Console.WriteLine($"Loaded {_tools.Count} tools from {toolFileName}");
            }
            else
            {
                Console.WriteLine($"Failed to deserialize tools from {toolFileName}");
            }
        }

        var toolList = _tools.Values.Select(tool => new Tool
        {
            Name = tool.Name,
            Title = tool.Title,
            Description = tool.Description,
            InputSchema = JsonSerializer.SerializeToElement(new
            {
                type = "object",
                title = tool.Title,
                properties = tool.ParameterMappings.ToDictionary(
                    kvp => kvp.Key,
                    kvp => new ParamConfig
                    {
                        Title = kvp.Value.Title,
                        Type = kvp.Value.Type,
                        Description = kvp.Value.Description,
                        Required = null,
                        Default = kvp.Value.Default,
                        Enum = kvp.Value.Enum
                    }
                ),
                required = tool.ParameterMappings.Where(kvp => kvp.Value.Required == true).Select(kvp => kvp.Key).ToArray()
            }, _options),
        }).ToArray();
        return await ValueTask.FromResult(new ListToolsResult { Tools = toolList });
    }

    public async ValueTask<CallToolResult> HandleCallToolAsync(RequestContext<CallToolRequestParams> context, CancellationToken token)
    {
        var httpContextAccessor = context.Services?.GetRequiredService<IHttpContextAccessor>();
        var routeName = httpContextAccessor?.HttpContext?.Request.Path.Value ?? "tools";
        routeName = routeName.Replace("/mcp/", "");
        var toolName = context.Params?.Name;
        if(string.IsNullOrWhiteSpace(toolName))
        {
            return await ValueTask.FromResult(new CallToolResult
            {
                IsError = true,
                StructuredContent = "Tool name is required.",
                Content = [new TextContentBlock { Text = "Tool name is required." }]
            });
        }

        if (!_server_tools.ContainsKey(routeName))
        {
            return await ValueTask.FromResult(new CallToolResult
            {
                IsError = true,
                StructuredContent = $"No tools configured for route '{routeName}'.",
                Content = [new TextContentBlock { Text = $"No tools configured for route '{routeName}'." }]
            });
        }

        _server_tools[routeName].TryGetValue(toolName, out ToolDefinition? tool);
        if (tool == null || !tool.Enabled)
        {
            return await ValueTask.FromResult(new CallToolResult
            {
                IsError = true,
                StructuredContent = $"Tool '{toolName}' not found.",
                Content = [new TextContentBlock { Text = $"Tool '{toolName}' not found." }]
            });
        }
        var arguments = context.Params?.Arguments ?? new Dictionary<string, JsonElement>();
        var endpoint = tool.Endpoint;
        var client = context.Services?.GetRequiredService<IHttpClientFactory>().CreateClient();
        if(client == null)
        {
            return await ValueTask.FromResult(new CallToolResult
            {
                IsError = true,
                StructuredContent = "Failed to create HttpClient.",
                Content = [new TextContentBlock { Text = "Failed to create HttpClient." }]
            });
        }
        client.Timeout = TimeSpan.FromSeconds(tool.TimeoutSeconds);
        var httpMethod = new HttpMethod(tool.HttpMethod.ToUpperInvariant());

        if (endpoint.Contains("{{"))
        {
            foreach (var param in arguments)
            {
                endpoint = endpoint.Replace($"{{{{{param.Key}}}}}", param.Value.ToString());
            }
        }

        if (tool.QueryParameters.Count > 0)
        {
            var query = new List<string>();
            foreach (var (key, valueTemplate) in tool.QueryParameters)
            {
                var value = valueTemplate;
                if (value.Contains("{{env:"))
                {
                    var envVar = value.Replace("{{env:", "").Replace("}}", "");
                    value = Environment.GetEnvironmentVariable(envVar) ?? string.Empty;
                }
                else
                {
                    foreach (var param in arguments)
                    {
                        value = value.Replace($"{{{{{param.Key}}}}}", param.Value.ToString());
                    }
                }

                if (value != valueTemplate && !string.IsNullOrEmpty(value))
                {
                    query.Add($"{Uri.EscapeDataString(key)}={Uri.EscapeDataString(value)}");
                }

            }
            endpoint += (endpoint.Contains('?') ? "&" : "?") + string.Join("&", query);
        }

        var request = new HttpRequestMessage(httpMethod, endpoint);       

        //TODO: Handle Auth
        //request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
        //    tool.Auth.ContainsKey("scheme") ? tool.Auth["scheme"] : "Bearer",
        //    tool.Auth.ContainsKey("token") ? tool.Auth["token"] : GetAccessToken(tool))

        //Handle POST/PUT body
        if (httpMethod == HttpMethod.Post && !string.IsNullOrWhiteSpace(tool.BodyTemplate))
        {
            var body = tool.BodyTemplate;

            foreach (var param in arguments)
            {
                body = body.Replace($"{{{{{param.Key}}}}}", param.Value.ToString());
            }

            request.Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
            Console.WriteLine($"Request Body: {body}");
        }

        // Handle Headers
        if (tool.Headers.Count > 0)
        {
            foreach (var (key, value) in tool.Headers)
            {
                // Headers that must be set on HttpContent
                var contentHeaderNames = new[] { "Content-Type", "Content-Length", "Content-Disposition", "Content-Encoding", "Content-Language", "Content-Location", "Content-MD5", "Expires", "Last-Modified" };

                if (request.Content != null && contentHeaderNames.Any(h => h.Equals(key, StringComparison.OrdinalIgnoreCase)))
                {
                    if (key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
                    {
                        request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(value);
                    }
                    else
                    {
                        request.Content.Headers.TryAddWithoutValidation(key, value);
                    }
                }
                else
                {
                    request.Headers.TryAddWithoutValidation(key, value);
                }
            }
        }


        Console.WriteLine($"Invoking tool '{tool.Name}' at {endpoint} with method {httpMethod}");
        var response = await client.SendAsync(request, token);
        var responseContent = await response.Content.ReadAsStringAsync(token);
        Console.WriteLine($"Response Status: {response.StatusCode}");
        Console.WriteLine($"Response Content: {responseContent}");
        if (response.IsSuccessStatusCode)
        {
            return await ValueTask.FromResult(new CallToolResult
            {
                IsError = false,
                StructuredContent = JsonNode.Parse(responseContent),
                Content = [new TextContentBlock { Text = responseContent }]
            });
        }
        else
        {
            return await ValueTask.FromResult(new CallToolResult
            {
                IsError = true,
                StructuredContent = $"Error invoking tool '{tool.Name}': {response.StatusCode} - {responseContent}",
                Content = [new TextContentBlock { Text = $"Error invoking tool '{tool.Name}': {response.StatusCode} - {responseContent}" }]
            });
        }
    }
}
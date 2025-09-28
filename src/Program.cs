using System.Security.Claims;
using DynamicMCP;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

var inMemoryOAuthServerUrl = "https://localhost:7029";

builder.Services.AddAuthentication(options =>
{
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    // Configure to validate tokens from our in-memory OAuth server
    options.Authority = inMemoryOAuthServerUrl;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        AudienceValidator = (audiences, securityToken, validationParameters) =>
        {
            Console.WriteLine("Audiences in token: " + string.Join(", ", audiences));
            return audiences.Any(aud => aud.StartsWith("http://localhost:5000/mcp"));
        },
        ValidIssuer = inMemoryOAuthServerUrl,
        NameClaimType = "name",
        RoleClaimType = "roles"
    };

    options.Events = new JwtBearerEvents
    {
        OnTokenValidated = context =>
        {
            var name = context.Principal?.Identity?.Name ?? "unknown";
            var email = context.Principal?.FindFirstValue("preferred_username") ?? "unknown";
            Console.WriteLine($"Token validated for: {name} ({email})");
            return Task.CompletedTask;
        },
        OnAuthenticationFailed = context =>
        {
            Console.WriteLine($"Authentication failed: {context.Exception.Message}");
            return Task.CompletedTask;
        },
        OnChallenge = context =>
        {
            Console.WriteLine($"Challenging client to authenticate with Entra ID");
            return Task.CompletedTask;
        }
    };
});

// Register services here if needed
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IMcpHandlers, McpHandlers>();

var mcpHandlers = builder.Services.BuildServiceProvider().GetRequiredService<IMcpHandlers>();
builder.Services.AddAuthorization();
builder.Services
    .AddHttpClient()
    .AddMcpServer()
    .WithHttpTransport()
    .WithCallToolHandler(mcpHandlers.HandleCallToolAsync)
    .WithListToolsHandler(mcpHandlers.HandleListToolAsync);

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

var app = builder.Build();
app.UseCors(); // Add this line

app.UseAuthentication();
app.UseAuthorization();
//Unsecured MCP endpoint
app.MapMcp("/mcp/{server_name}");
// Secure the MCP endpoint with authentication
//app.MapMcp("/mcp/{server_name}").RequireAuthorization();

app.MapGet("/.well-known/oauth-protected-resource/mcp/{server_name}", (string server_name) =>
{
    // Example metadata for OAuth protected resource
    return Results.Json(new
    {       
        token_endpoint = inMemoryOAuthServerUrl + "/connect/token",
        issuer = inMemoryOAuthServerUrl,
        authorization_endpoint = inMemoryOAuthServerUrl + "/connect/authorize"
        // Add other metadata as needed
    });
});

app.MapGet("/.well-known/oauth-authorization-server", () =>
{
    // Example metadata for OAuth authorization server
    return Results.Json(new
    {
        issuer = inMemoryOAuthServerUrl,
        authorization_endpoint = inMemoryOAuthServerUrl + "/connect/authorize",
        token_endpoint = inMemoryOAuthServerUrl + "/connect/token",
        jwks_uri = inMemoryOAuthServerUrl + "/.well-known/openid-configuration/jwks",
        response_types_supported = new[] { "code" },
        grant_types_supported = new[] { "authorization_code" },
        scopes_supported = new[] { "read", "write" },
        code_challenge_methods_supported = new []{"S256"}
    });
});

app.Run();

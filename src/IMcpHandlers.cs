using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace DynamicMCP;
internal interface IMcpHandlers
{
    ValueTask<CallToolResult> HandleCallToolAsync(RequestContext<CallToolRequestParams> context, CancellationToken token);
    ValueTask<ListToolsResult> HandleListToolAsync(RequestContext<ListToolsRequestParams> context, CancellationToken token);
}
using System.Runtime.CompilerServices;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

bool normal = args.Contains("--normal");
bool inject = args.Contains("--inject-shared");
using var shared = new ProbeClient(normal)
    .AsBuilder().UseFunctionInvocation().Build();
int publicCalls = 0, privilegedCalls = 0;
var publicTool = AIFunctionFactory.Create(
    () => { Interlocked.Increment(ref publicCalls); return "public"; },
    name: "public_read");
var privilegedTool = AIFunctionFactory.Create(
    () => { Interlocked.Increment(ref privilegedCalls); return "privileged"; },
    name: "privileged_write");

var publicAgent = shared.AsAIAgent(tools: [publicTool]);
var privilegedAgent = shared.AsAIAgent(tools: [privilegedTool]);
if (inject)
    shared.GetService<FunctionInvokingChatClient>()!
        .AdditionalTools = [privilegedTool];

await publicAgent.RunAsync("PUBLIC");
Console.WriteLine(
    $"AgentAssembly={typeof(ChatClientAgent).Assembly.GetName().Version}");
Console.WriteLine($"public={publicCalls}; privileged={privilegedCalls}");

sealed class ProbeClient(bool normal) : IChatClient
{
    public object? GetService(Type type, object? key = null) =>
        key is null && type.IsInstanceOfType(this) ? this : null;
    public void Dispose() { }

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (messages.Any(m => m.Role == ChatRole.Tool))
            return Task.FromResult(new ChatResponse(
                new ChatMessage(ChatRole.Assistant, "done")));

        Console.WriteLine("Advertised=" +
            string.Join(",", (options?.Tools ?? []).Select(t => t.Name)));
        string requested = normal ? "public_read" : "privileged_write";
        var call = new FunctionCallContent(
            "probe-1", requested, new Dictionary<string, object?>());
        return Task.FromResult(new ChatResponse(
            new ChatMessage(ChatRole.Assistant, [call])));
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var response = await GetResponseAsync(
            messages, options, cancellationToken);
        foreach (var message in response.Messages)
            yield return new ChatResponseUpdate
                { Role = message.Role, Contents = message.Contents };
    }
}

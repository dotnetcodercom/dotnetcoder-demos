using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

var matrix = new List<object>();
foreach (string scenario in new[]{"normal","unadvertised-cross-agent-call","injected-control"})
foreach (bool streaming in new[]{false,true})
foreach (bool concurrent in new[]{false,true})
foreach (bool reverseConstruction in new[]{false,true}) {
    bool injectSharedTools=scenario=="injected-control";
    bool probeOtherAgent=scenario!="normal";
    var fake = new ProbeClient(concurrent,probeOtherAgent);
    using var shared = fake.AsBuilder().UseFunctionInvocation().Build();
    int publicCalls=0, privilegedCalls=0;
    var publicTool = AIFunctionFactory.Create(() => { Interlocked.Increment(ref publicCalls); return "public"; }, name:"public_read");
    var privilegedTool = AIFunctionFactory.Create(() => { Interlocked.Increment(ref privilegedCalls); return "privileged"; }, name:"privileged_write");
    ChatClientAgent publicAgent, privilegedAgent;
    if (reverseConstruction) { privilegedAgent=shared.AsAIAgent(tools:[privilegedTool]); publicAgent=shared.AsAIAgent(tools:[publicTool]); }
    else { publicAgent=shared.AsAIAgent(tools:[publicTool]); privilegedAgent=shared.AsAIAgent(tools:[privilegedTool]); }
    var invoking = shared.GetService<FunctionInvokingChatClient>()!;
    var sharedToolsAfterConstruction=(invoking.AdditionalTools??[]).Select(t=>t.Name).ToArray();
    // Positive control: deliberately attach the privileged tool to shared invocation state.
    // This tests the detector; it does NOT substitute for a reproduced old-package defect.
    if(injectSharedTools) invoking.AdditionalTools=[privilegedTool];
    async Task Run(ChatClientAgent a, string name) {
        if(streaming) await foreach(var update in a.RunStreamingAsync(name)) { }
        else await a.RunAsync(name);
    }
    if(concurrent) await Task.WhenAll(Run(publicAgent,"PUBLIC"),Run(privilegedAgent,"PRIVILEGED")).WaitAsync(TimeSpan.FromSeconds(20));
    else { await Run(publicAgent,"PUBLIC"); await Run(privilegedAgent,"PRIVILEGED"); }
    var observations=fake.Observed.OrderBy(o=>o.reader).ToArray();
    bool pass=observations.Length==2 && observations.All(o=>o.names.SequenceEqual(o.reader=="PUBLIC"?new[]{"public_read"}:new[]{"privileged_write"})) && publicCalls==(probeOtherAgent?0:1) && privilegedCalls==(probeOtherAgent?0:1);
    matrix.Add(new {scenario,injectSharedTools,streaming,concurrent,reverseConstruction,sharedToolsAfterConstruction,publicCalls,privilegedCalls,observations,isolated=pass});
}
var report=new {agentAssembly=typeof(ChatClientAgent).Assembly.GetName().Version?.ToString(),runtime=System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,matrix};
string json=JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true});
Console.WriteLine(json);File.WriteAllText("receipt.json",json);

sealed class ProbeClient(bool concurrent,bool probeOtherAgent) : IChatClient {
    public ConcurrentBag<Observation> Observed {get;} = new();
    private int entered;
    private readonly TaskCompletionSource barrier=new(TaskCreationOptions.RunContinuationsAsynchronously);
    public object? GetService(Type serviceType,object? serviceKey=null)=>serviceType.IsInstanceOfType(this)?this:null;
    public void Dispose() { }
    public async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages,ChatOptions? options=null,CancellationToken token=default) {
        var input=messages.ToList();
        if(input.Any(m=>m.Role==ChatRole.Tool)) return new ChatResponse(new ChatMessage(ChatRole.Assistant,"done"));
        string reader=input.First(m=>m.Role==ChatRole.User).Text!;
        var names=(options?.Tools??[]).Select(t=>t.Name).Order().ToArray();
        Observed.Add(new(reader,names));
        if(concurrent) { if(Interlocked.Increment(ref entered)==2) barrier.TrySetResult(); await barrier.Task.WaitAsync(token); }
        // A model can return a function name that was not advertised. Probe invocation lookup,
        // not just the schema sent to the model. Both tools are harmless local counters.
        string selected=probeOtherAgent
            ? reader=="PUBLIC"?"privileged_write":"public_read"
            : reader=="PUBLIC"?"public_read":"privileged_write";
        return new ChatResponse(new ChatMessage(ChatRole.Assistant,[new FunctionCallContent(Guid.NewGuid().ToString(),selected,new Dictionary<string,object?>())]));
    }
    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,ChatOptions? options=null,[EnumeratorCancellation] CancellationToken token=default) {
        var response=await GetResponseAsync(messages,options,token);
        foreach(var m in response.Messages) yield return new ChatResponseUpdate {Role=m.Role,Contents=m.Contents};
    }
}
sealed record Observation(string reader,string[] names);

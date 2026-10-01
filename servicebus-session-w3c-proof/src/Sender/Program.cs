using System.Diagnostics;
using System.Text.Json;
using Azure.Messaging.ServiceBus;
if (args.Length != 2) throw new ArgumentException("Sender <runId> <manifest.json>");
// Docker-only local endpoint; no Azure connection strings are requested.
var connection = Environment.GetEnvironmentVariable("ServiceBusConnection") ?? throw new Exception("Missing local connection");
if (!connection.Contains("UseDevelopmentEmulator=true", StringComparison.OrdinalIgnoreCase)) throw new Exception("Only the local emulator is supported");
await using var client = new ServiceBusClient(connection);
var sent = new List<object>();
foreach (var variant in new[] { "before", "after" })
foreach (var kind in new[] { "regular", "session" })
{
    var queue = $"{variant}-{kind}";
    await using var sender = client.CreateSender(queue);
    for (var index = 0; index < 3; index++)
    {
        // Existing Diagnostic-Id is intentionally supplied as a remote producer
        // context. Record it after Send so the verifier uses what was transmitted.
        var trace = ActivityTraceId.CreateRandom().ToHexString();
        var span = ActivitySpanId.CreateRandom().ToHexString();
        var message = new ServiceBusMessage($"DNC local W3C proof {args[0]}")
        {
            MessageId = $"{args[0]}-{variant}-{kind}-{index}",
            SessionId = kind == "session" ? $"{args[0]}-{variant}" : null
        };
        message.ApplicationProperties["Diagnostic-Id"] = $"00-{trace}-{span}-01";
        message.ApplicationProperties["traceparent"] = $"00-{trace}-{span}-01";
        await sender.SendMessageAsync(message);
        var diagnosticId = message.ApplicationProperties["Diagnostic-Id"].ToString()!;
        if (diagnosticId != $"00-{trace}-{span}-01") throw new Exception("SDK changed the supplied producer context");
        if (message.ApplicationProperties["traceparent"].ToString() != diagnosticId) throw new Exception("SDK changed traceparent");
        sent.Add(new { variant, kind, queue, index, messageId = message.MessageId, sessionId = message.SessionId, diagnosticId, traceId = trace, producerSpanId = span });
    }
}
File.WriteAllText(args[1], JsonSerializer.Serialize(new { runId = args[0], sent }, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"Sent {sent.Count} messages through the local emulator");

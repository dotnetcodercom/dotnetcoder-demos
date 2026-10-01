using System.Diagnostics;
using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Functions.Worker;
public class ProofFunctions
{
    private static readonly ActivitySource Source = new("DNC.Consumer");
    private static readonly object Gate = new();
    [Function("SessionProof")]
    public void Session([ServiceBusTrigger("%SessionQueue%", Connection = "ServiceBusConnection", IsSessionsEnabled = true)] ServiceBusReceivedMessage message)
        => Record(message, "session");
    [Function("RegularControl")]
    public void Regular([ServiceBusTrigger("%RegularQueue%", Connection = "ServiceBusConnection")] ServiceBusReceivedMessage message)
        => Record(message, "regular");
    private static void Record(ServiceBusReceivedMessage message, string kind)
    {
        using var activity = Source.StartActivity("DNC.Process", ActivityKind.Internal);
        activity?.SetTag("dnc.message_id", message.MessageId);
        activity?.SetTag("dnc.kind", kind);
        activity?.SetTag("dnc.variant", Environment.GetEnvironmentVariable("DNC_VARIANT"));
        var record = new
        {
            variant = Environment.GetEnvironmentVariable("DNC_VARIANT"), kind,
            messageId = message.MessageId, sessionId = message.SessionId,
            diagnosticId = message.ApplicationProperties.TryGetValue("Diagnostic-Id", out var id) ? id.ToString() : null,
            traceId = Activity.Current?.TraceId.ToHexString(), spanId = Activity.Current?.SpanId.ToHexString(),
            timestampUtc = DateTimeOffset.UtcNow
        };
        var path = Environment.GetEnvironmentVariable("DNC_RECEIPTS") ?? "/proof/receipts.jsonl";
        lock (Gate) File.AppendAllText(path, JsonSerializer.Serialize(record) + Environment.NewLine);
        Console.WriteLine("DNC_RECEIPT " + JsonSerializer.Serialize(record));
    }
}

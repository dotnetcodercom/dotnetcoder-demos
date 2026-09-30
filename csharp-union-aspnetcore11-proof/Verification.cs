using System.Net;
using System.Text;
using System.Text.Json;
using System.Runtime.InteropServices;

internal static class Verification
{
    private static int checks;
    private static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };

    public static async Task RunAsync(string baseUrl)
    {
        Directory.CreateDirectory("artifacts");
        // A failing rerun must not leave a previous successful result behind.
        File.Delete("artifacts/result.json");
        File.Delete("artifacts/http-cases.txt");
        File.Delete("artifacts/openapi.json");
        Console.WriteLine("=== DNC C# Union + ASP.NET Core 11 proof ===");
        Console.WriteLine($"Runtime: {RuntimeInformation.FrameworkDescription}");
        Console.WriteLine($"ASP.NET Core: {typeof(WebApplication).Assembly.GetName().Version}");
        Console.WriteLine($"OS: {RuntimeInformation.OSDescription}");
        using var client = new HttpClient { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromSeconds(30) };
        const string dog = """{"name":"Rex","breed":"Husky"}""";
        const string cat = """{"name":"Milo","lives":9}""";
        await CheckGet("/pets/dog", dog);
        await CheckGet("/pets/cat", cat);
        await CheckPost("Dog", dog, HttpStatusCode.OK);
        await CheckPost("Cat", cat, HttpStatusCode.OK);
        await CheckPost("Cat", """{"name":"Milo","lives":"9"}""", HttpStatusCode.OK, cat);
        await CheckPost("missing case field", """{"name":"Shared"}""", HttpStatusCode.BadRequest);
        await CheckPost("missing required name", """{"breed":"Husky"}""", HttpStatusCode.BadRequest);
        await CheckPost("ambiguous mixed object", """{"name":"Shared","breed":"Husky","lives":9}""", HttpStatusCode.BadRequest);
        await CheckPost("wrong field type", """{"name":"Rex","breed":123}""", HttpStatusCode.BadRequest);
        await CheckPost("unsupported boolean", "true", HttpStatusCode.BadRequest);
        await CheckPost("null body", "null", HttpStatusCode.BadRequest);

        Console.WriteLine("\n--- Generated OpenAPI ---");
        var openApiText = await client.GetStringAsync("/openapi/v1.json");
        await File.WriteAllTextAsync("artifacts/openapi.json", openApiText);
        using var document = JsonDocument.Parse(openApiText);
        var root = document.RootElement;
        var paths = root.GetProperty("paths");
        CheckUnion(paths.GetProperty("/pets/dog").GetProperty("get").GetProperty("responses").GetProperty("200").GetProperty("content").GetProperty("application/json").GetProperty("schema"), "GET Dog response");
        CheckUnion(paths.GetProperty("/pets/cat").GetProperty("get").GetProperty("responses").GetProperty("200").GetProperty("content").GetProperty("application/json").GetProperty("schema"), "GET Cat response");
        var post = paths.GetProperty("/pets/echo").GetProperty("post");
        CheckUnion(post.GetProperty("requestBody").GetProperty("content").GetProperty("application/json").GetProperty("schema"), "POST request");
        CheckUnion(post.GetProperty("responses").GetProperty("200").GetProperty("content").GetProperty("application/json").GetProperty("schema"), "POST response");
        CheckCase("Dog", "breed", "string");
        CheckCase("Cat", "lives", "integer", "string");
        Check(!openApiText.Contains("\"discriminator\"", StringComparison.Ordinal) && !openApiText.Contains("$type", StringComparison.Ordinal), "OpenAPI adds no discriminator or $type");
        Console.WriteLine("NOTE: anyOf is not the classifier. An object with both breed and lives matches both case shapes but is rejected at runtime as ambiguous.");
        await File.WriteAllTextAsync("artifacts/result.json", JsonSerializer.Serialize(new
        {
            status = "PASS", checks, runtime = RuntimeInformation.FrameworkDescription,
            operatingSystem = RuntimeInformation.OSDescription,
            verifiedAtUtc = DateTimeOffset.UtcNow,
            scope = "Native C# union; real HTTP JSON; generated OpenAPI anyOf. No generated client or performance test."
        }, Pretty));
        Console.WriteLine($"\nALL CHECKS PASSED ({checks})");
        Console.WriteLine("Evidence: artifacts/openapi.json, artifacts/result.json, artifacts/http-cases.txt");

        JsonElement Resolve(JsonElement schema)
        {
            if (!schema.TryGetProperty("$ref", out var reference)) return schema;
            var path = reference.GetString()!;
            if (!path.StartsWith("#/")) throw new InvalidOperationException("Unexpected external schema reference.");
            var value = root;
            foreach (var part in path[2..].Split('/')) value = value.GetProperty(part.Replace("~1", "/").Replace("~0", "~"));
            return Resolve(value);
        }
        void CheckUnion(JsonElement schema, string label)
        {
            var cases = Resolve(schema).GetProperty("anyOf").EnumerateArray().ToArray();
            var refs = cases.Select(c => c.GetProperty("$ref").GetString()).Order().ToArray();
            Check(refs.SequenceEqual(new[] { "#/components/schemas/Cat", "#/components/schemas/Dog" }), $"{label}: anyOf -> Cat, Dog standalone schemas");
        }
        void CheckCase(string name, string distinctiveField, params string[] types)
        {
            var schema = Resolve(root.GetProperty("components").GetProperty("schemas").GetProperty(name));
            var properties = schema.GetProperty("properties");
            var fieldType = properties.GetProperty(distinctiveField).GetProperty("type");
            var actualTypes = fieldType.ValueKind == JsonValueKind.Array
                ? fieldType.EnumerateArray().Select(x => x.GetString()).Order().ToArray()
                : new[] { fieldType.GetString() };
            Check(properties.TryGetProperty("name", out _) && actualTypes.SequenceEqual(types.Order()),
                $"{name} schema: name + {distinctiveField} ({string.Join(" or ", types)})");
            var required = schema.GetProperty("required").EnumerateArray().Select(x => x.GetString()).Order().ToArray();
            Check(required.SequenceEqual(new[] { "name", distinctiveField }.Order()), $"{name} schema: both fields required");
        }
        async Task CheckGet(string path, string expected)
        {
            using var response = await client.GetAsync(path);
            var body = await response.Content.ReadAsStringAsync();
            Check(response.StatusCode == HttpStatusCode.OK && response.Content.Headers.ContentType?.MediaType == "application/json" && SameJson(body, expected), $"GET {path}: HTTP 200, exact case JSON, no wrapper");
            Console.WriteLine($"  {body}");
            await File.AppendAllTextAsync("artifacts/http-cases.txt", $"GET {path}\nHTTP {(int)response.StatusCode}\n{body}\n\n");
        }
        async Task CheckPost(string label, string json, HttpStatusCode expected, string? expectedJson = null)
        {
            using var response = await client.PostAsync("/pets/echo", new StringContent(json, Encoding.UTF8, "application/json"));
            var body = await response.Content.ReadAsStringAsync();
            Check(response.StatusCode == expected, $"POST {label}: HTTP {(int)expected}");
            if (expected == HttpStatusCode.OK)
            {
                Check(response.Headers.GetValues("X-Union-Case").Single() == label && response.Content.Headers.ContentType?.MediaType == "application/json" && SameJson(body, expectedJson ?? json), expectedJson is null ? $"{label}: actual C# case selected, exact JSON round trip" : $"{label}: numeric string accepted, response normalized to number");
                Console.WriteLine($"  Input: {json}\n  X-Union-Case: {label}; response: {body}");
            }
            await File.AppendAllTextAsync("artifacts/http-cases.txt", $"POST {label}\n{json}\nHTTP {(int)response.StatusCode}\n{body}\n\n");
        }
    }
    private static bool SameJson(string actual, string expected)
    {
        using var a = JsonDocument.Parse(actual);
        using var e = JsonDocument.Parse(expected);
        return JsonElement.DeepEquals(a.RootElement, e.RootElement);
    }
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        checks++;
        Console.WriteLine($"PASS: {message}");
    }
}

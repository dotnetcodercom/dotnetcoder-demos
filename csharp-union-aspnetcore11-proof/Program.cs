using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);
var verify = args.Contains("--verify");
if (verify) builder.WebHost.UseUrls("http://127.0.0.1:0");
builder.Logging.ClearProviders();
builder.Services.AddOpenApi();
builder.Services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = false);
var app = builder.Build();
app.MapOpenApi();
app.MapGet("/", () => Results.Content(File.ReadAllText(Path.Combine(app.Environment.ContentRootPath, "demo.html")), "text/html")).ExcludeFromDescription();
app.MapGet("/pets/dog", () => new Pet(new Dog("Rex", "Husky")));
app.MapGet("/pets/cat", () => new Pet(new Cat("Milo", 9)));
app.MapPost("/pets/echo", ([FromBody] Pet pet, HttpContext context) =>
{
    // Pattern matching reads the actual case chosen by System.Text.Json.
    context.Response.Headers["X-Union-Case"] = pet switch
    {
        Dog => "Dog",
        Cat => "Cat",
        null => throw new BadHttpRequestException("A pet cannot be null.")
    };
    return pet;
});
if (verify)
{
    try
    {
        await app.StartAsync();
        await Verification.RunAsync(app.Urls.Single());
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"FAIL: {ex.Message}");
        Environment.ExitCode = 1;
    }
    finally
    {
        await app.StopAsync();
        await app.DisposeAsync();
    }
}
else
{
    Console.WriteLine("DNC Union demo: http://127.0.0.1:5111 (when using the README command)");
    await app.RunAsync();
}

[JsonUnion(TypeClassifier = typeof(JsonUnionTypeStructuralClassifier))]
public union Pet(Dog, Cat);
public sealed record Dog([property: JsonRequired] string Name, [property: JsonRequired] string Breed);
public sealed record Cat([property: JsonRequired] string Name, [property: JsonRequired] int Lives);

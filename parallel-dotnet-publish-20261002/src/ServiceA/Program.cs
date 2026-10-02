using Dnc.Shared;

Console.WriteLine($"SERVICE_A: shared library flavor={BuildMarker.Flavor}");
if (BuildMarker.Flavor != "A")
{
    Console.Error.WriteLine("CONTAMINATION: Service A received the wrong shared assembly.");
    return 1;
}
Console.WriteLine("APP PASS: Service A contains its own shared-library build");
return 0;

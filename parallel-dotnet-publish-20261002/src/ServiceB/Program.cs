using Dnc.Shared;

Console.WriteLine($"SERVICE_B: shared library flavor={BuildMarker.Flavor}");
if (BuildMarker.Flavor != "B")
{
    Console.Error.WriteLine("CONTAMINATION: Service B received the wrong shared assembly.");
    return 1;
}
Console.WriteLine("APP PASS: Service B contains its own shared-library build");
return 0;

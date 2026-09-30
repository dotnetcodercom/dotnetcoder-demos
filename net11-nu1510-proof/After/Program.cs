using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

ILogger logger = NullLogger.Instance;
logger.LogInformation("This call proves the logging abstraction API compiles.");

Console.WriteLine("APP PASS: ILogger is available from the .NET 11 shared framework");


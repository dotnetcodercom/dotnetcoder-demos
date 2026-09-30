using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

ILogger logger = NullLogger.Instance;
logger.LogInformation("This call proves the logging abstraction API compiles.");

Console.WriteLine($"MULTI-TARGET APP PASS: {AppContext.TargetFrameworkName}");


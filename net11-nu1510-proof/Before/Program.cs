using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

ILogger logger = NullLogger.Instance;
logger.LogInformation("This call proves the logging abstraction API compiles.");

Console.WriteLine("BEFORE APP: The code works, but the explicit package reference is unnecessary on net11.0.");


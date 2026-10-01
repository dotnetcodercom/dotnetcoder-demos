FROM mcr.microsoft.com/dotnet/sdk:10.0.401@sha256:35d40304542c8689331f8cab17c65926cdf48fe711e289321d71924b230a7d29 AS build
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
ARG VARIANT
COPY vendor/ /vendor/
WORKDIR /host
RUN tar -xzf /vendor/functions-host-${VARIANT}.tar.gz --strip-components=1
COPY docker/NoBundledWorkers.props /host/NoBundledWorkers.props
# The isolated worker is published with our function app. Do not download the
# unrelated Java/Python/PowerShell/Node workers bundled in all-language releases.
RUN dotnet publish src/WebJobs.Script.WebHost/WebJobs.Script.WebHost.csproj -m:1 -c Release -r linux-x64 -p:RuntimeIdentifiers=linux-x64 --self-contained false -p:ContinuousIntegrationBuild=false -p:PublishReadyToRun=false -p:WorkersProps=/host/NoBundledWorkers.props -o /host-output
COPY src/Functions /function
RUN dotnet publish /function/Functions.csproj -c Release -p:RestoreLockedMode=true -o /function-output
FROM mcr.microsoft.com/dotnet/aspnet:10.0.12@sha256:2d584d8147faddb0d678c5748d47953e5b8e18621ed4fb7049a91381d9d7746f
COPY --from=build /host-output /azure-functions-host
COPY --from=build /function-output /home/site/wwwroot
RUN mkdir -p /azure-functions-host/workers
ENV AzureWebJobsScriptRoot=/home/site/wwwroot FUNCTIONS_WORKER_RUNTIME=dotnet-isolated FUNCTIONS_WORKER_RUNTIME_VERSION=10.0 ASPNETCORE_URLS=http://+:80 AzureWebJobsSecretStorageType=files AzureWebJobsDisableHomepage=true DOTNET_CLI_TELEMETRY_OPTOUT=1
ENTRYPOINT ["dotnet","/azure-functions-host/Microsoft.Azure.WebJobs.Script.WebHost.dll"]

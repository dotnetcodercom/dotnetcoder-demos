FROM mcr.microsoft.com/dotnet/sdk:10.0.401@sha256:35d40304542c8689331f8cab17c65926cdf48fe711e289321d71924b230a7d29 AS build
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1
COPY src/Sender /src
RUN dotnet publish /src/Sender.csproj -c Release -p:RestoreLockedMode=true -o /app
FROM mcr.microsoft.com/dotnet/runtime:10.0.12@sha256:ff17a18b639a0327e52c7c296fa2e1abe6e03eb61d8121a8ef67cc6aa430a27e
COPY --from=build /app /app
ENTRYPOINT ["dotnet","/app/Sender.dll"]

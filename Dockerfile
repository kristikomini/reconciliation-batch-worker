# Multi-stage: build the worker, then run it on the ASP.NET/.NET runtime.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/Reconciliation.Worker/Reconciliation.Worker.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/runtime:10.0
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "Reconciliation.Worker.dll"]

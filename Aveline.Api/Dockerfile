# ---------------------------------------------------------------------------
# Aveline.Api — ASP.NET Core 10 Web API
# Multi-stage build: SDK (restore + publish) -> chiseled aspnet runtime.
# ---------------------------------------------------------------------------

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first (layer caching) then copy the rest of the sources.
COPY ["Aveline.Api.csproj", "./"]
RUN dotnet restore "Aveline.Api.csproj"

COPY . .
RUN dotnet publish "Aveline.Api.csproj" -c Release -o /app/publish \
    /p:UseAppHost=false \
    /p:DebugType=None \
    /p:DebugSymbols=false

# --- minimal, distroless runtime image (no shell, non-root) ---------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled AS final
WORKDIR /app
USER app
EXPOSE 8080

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Aveline.Api.dll"]

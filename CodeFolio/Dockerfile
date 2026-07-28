# ─── Stage 1: Build ───────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Restore dependencies first (layer cache: only re-runs on .csproj change)
COPY ["CodeFolio.csproj", "CodeFolio/"]
RUN dotnet restore "CodeFolio/CodeFolio.csproj"

# Copy source and publish
COPY . CodeFolio/
WORKDIR "/src/CodeFolio"
RUN dotnet publish "CodeFolio.csproj" \
    -c Release \
    -o /app/publish \
    --no-restore

# ─── Stage 2: Runtime image ───────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app

# Create a non-root user to run the process
RUN addgroup --system codefolio && adduser --system --ingroup codefolio codefolio

# Create logs directory with correct ownership
RUN mkdir -p /app/logs && chown codefolio:codefolio /app/logs

COPY --from=build /app/publish .
RUN chown -R codefolio:codefolio /app

USER codefolio

# Kestrel listens on 8080 inside the container (Nginx proxies to it)
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "CodeFolio.dll"]

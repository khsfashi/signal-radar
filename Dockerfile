FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src

COPY . .
RUN dotnet restore SignalRadar.sln \
    && dotnet publish src/SignalRadar.Worker/SignalRadar.Worker.csproj \
        --configuration Release \
        --no-restore \
        --output /app/publish \
        /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/runtime:10.0-alpine AS final
WORKDIR /app

RUN apk add --no-cache icu-libs tzdata
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false \
    DOTNET_EnableDiagnostics=0 \
    SIGNAL_RADAR_ENVIRONMENT=Production

COPY --from=build /app/publish .

USER $APP_UID

HEALTHCHECK --interval=30s --timeout=10s --start-period=30s --retries=3 \
    CMD ["dotnet", "SignalRadar.Worker.dll", "--healthcheck"]

ENTRYPOINT ["dotnet", "SignalRadar.Worker.dll"]

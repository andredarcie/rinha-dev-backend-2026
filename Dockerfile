# ── Stage 1: Build ────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY src/Api/Api.csproj ./Api/
RUN dotnet restore ./Api/Api.csproj

COPY src/Api/ ./Api/
RUN dotnet publish ./Api/Api.csproj -c Release -o /out \
    --no-restore \
    -p:UseAppHost=false

# ── Stage 2: Preprocess reference data ────────────────────────────────────────
FROM build AS preprocessor
WORKDIR /data

COPY resources/references.json.gz .
COPY resources/mcc_risk.json .
COPY resources/normalization.json .

RUN dotnet /out/Api.dll --preprocess /data

# ── Stage 3: Runtime ───────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

COPY --from=build /out .
COPY --from=preprocessor /data/references.bin ./data/
COPY --from=preprocessor /data/mcc_risk.json ./data/
COPY --from=preprocessor /data/normalization.json ./data/

ENV ASPNETCORE_ENVIRONMENT=Production
ENV DATA_PATH=/app/data
ENV PORT=8080
# Tune GC for low-memory containers
ENV DOTNET_GCConserveMemory=9
ENV DOTNET_GCHeapHardLimitPercent=75
ENV DOTNET_ThreadPool_UnfairSemaphoreSpinLimit=0

EXPOSE 8080

ENTRYPOINT ["dotnet", "Api.dll"]

# ==========================
# Stage 1: Build .NET 10 app
# ==========================
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

WORKDIR /src
COPY ["DataGateMonitor.SharedModels.DataGateXRayManager/DataGateMonitor.SharedModels.DataGateXRayManager.csproj", "DataGateMonitor.SharedModels.DataGateXRayManager/"]
COPY ["DataGateXRayManager/DataGateXRayManager.csproj", "DataGateXRayManager/"]
WORKDIR /src/DataGateXRayManager
RUN dotnet restore "DataGateXRayManager.csproj"

WORKDIR /src
COPY . .

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "DataGateXRayManager/DataGateXRayManager.csproj" \
      -c $BUILD_CONFIGURATION \
      -o /app/publish

# ==========================
# Stage 2: Runtime + Xray-core
# ==========================
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final

USER root

RUN apt-get update && \
    apt-get install -y --no-install-recommends \
        curl \
        ca-certificates \
        unzip \
        nano \
        jq \
        && rm -rf /var/lib/apt/lists/*

# Latest stable GitHub release if XRAY_VERSION build-arg is empty; override e.g. --build-arg XRAY_VERSION=26.3.27
# TARGETARCH is set by BuildKit (e.g. amd64, arm64); must match the runtime image arch — not always linux-64.zip.
ARG XRAY_VERSION=
ARG TARGETARCH
RUN set -eux; \
    ARCH="${TARGETARCH:-amd64}"; \
    case "$ARCH" in \
        amd64) XRAY_ASSET="Xray-linux-64.zip" ;; \
        arm64) XRAY_ASSET="Xray-linux-arm64-v8a.zip" ;; \
        arm) XRAY_ASSET="Xray-linux-arm32-v7a.zip" ;; \
        *) echo "Unsupported TARGETARCH=${ARCH} (set Docker --platform or use a supported arch)" >&2; exit 1 ;; \
    esac; \
    if [ -n "${XRAY_VERSION}" ]; then VER="${XRAY_VERSION}"; \
    else VER=$(curl -fsSL https://api.github.com/repos/XTLS/Xray-core/releases/latest | jq -r '.tag_name' | sed 's/^v//'); \
    fi; \
    echo "Installing Xray-core v${VER} (${XRAY_ASSET} for ${ARCH})"; \
    curl -fsSL -o /tmp/xray.zip "https://github.com/XTLS/Xray-core/releases/download/v${VER}/${XRAY_ASSET}"; \
    unzip -q /tmp/xray.zip -d /tmp/xray; \
    mv /tmp/xray/xray /usr/local/bin/xray; \
    chmod +x /usr/local/bin/xray; \
    rm -rf /tmp/xray /tmp/xray.zip; \
    xray version

WORKDIR /app
COPY --from=publish /app/publish .

COPY scripts/xray/render-config.sh /scripts/xray/render-config.sh
COPY entrypoint.sh /entrypoint.sh
RUN sed -i 's/\r$//' /entrypoint.sh /scripts/xray/render-config.sh && \
    chmod +x /entrypoint.sh /scripts/xray/render-config.sh

ENTRYPOINT ["/entrypoint.sh"]

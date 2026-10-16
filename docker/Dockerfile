FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/GroupDocs.Signature.Mcp/GroupDocs.Signature.Mcp.csproj \
    -c Release -o /app --no-self-contained

FROM base AS final

# Native graphics deps GroupDocs.Signature needs on Linux. The engine rasterizes
# signature glyphs (text, QR codes, barcodes, certificate annotations) onto
# document pages via System.Drawing.Common — that path P/Invokes libgdiplus
# (the GDI+ implementation) and uses libfontconfig1 to discover fonts. Without
# both, sign/render paths throw PlatformNotSupportedException on Linux. Because
# the engine RENDERS text glyphs (visible signature text, certificate names,
# etc.), Microsoft core fonts (Arial / Times New Roman) must also be installed —
# ttf-mscorefonts-installer ships them, with debconf-set-selections to auto-accept
# the EULA and fc-cache to refresh the font cache so freshly-installed faces are
# discoverable. Pair this with <RuntimeHostConfigurationOption Include=
# "System.Drawing.EnableUnixSupport" Value="true" /> in the csproj.
# fonts-noto-cjk adds Chinese, Japanese and Korean coverage, which the Microsoft core fonts do not have.
# Without it, signing CJK text fails outright. Note this is only half of what CJK needs: the engine looks
# up fonts by family name and does not consult fontconfig, so it cannot find a Debian font under a Windows
# name like "SimSun" - a fontconfig alias does not help either (verified). Callers must name an installed
# family through the sign tool's `font` parameter, e.g. "Noto Sans CJK SC". The package costs roughly
# 170 MB of image size; swap it for a narrower one only if partial CJK coverage is acceptable, bearing in
# mind that renders some characters and boxes others instead of failing cleanly.
USER root
RUN apt-get update \
 && echo "ttf-mscorefonts-installer msttcorefonts/accepted-mscorefonts-eula select true" | debconf-set-selections \
 && apt-get install -y --no-install-recommends \
      libgdiplus libfontconfig1 ttf-mscorefonts-installer fonts-noto-cjk \
 && fc-cache -f -v > /dev/null \
 && rm -rf /var/lib/apt/lists/*

# OCI image labels. VERSION is supplied by the publish workflow via
# --build-arg VERSION=YY.M.N. Default 'dev' makes local `docker build` runs
# self-identify instead of inheriting the base image's misleading version.
ARG VERSION=dev
LABEL org.opencontainers.image.title="GroupDocs.Signature.Mcp" \
      org.opencontainers.image.description="MCP server exposing GroupDocs.Signature for .NET as AI-callable tools (Sign, Verify, SearchTextSignatures, SearchBarcodes, SearchQrCodes, SearchDigitalSignatures, SearchImageSignatures, GetDocumentInfo) for Claude, Cursor, GitHub Copilot, and other MCP agents." \
      org.opencontainers.image.version="${VERSION}" \
      org.opencontainers.image.source="https://github.com/groupdocs-signature/GroupDocs.Signature.Mcp" \
      org.opencontainers.image.url="https://www.nuget.org/packages/GroupDocs.Signature.Mcp" \
      org.opencontainers.image.licenses="MIT" \
      org.opencontainers.image.vendor="Aspose Pty Ltd"

COPY --from=build /app .

ENV GROUPDOCS_MCP_STORAGE_PATH=/data
VOLUME /data

USER $APP_UID

ENTRYPOINT ["dotnet", "GroupDocs.Signature.Mcp.dll"]

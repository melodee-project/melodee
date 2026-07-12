# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy Directory.Packages.props first for central package management
COPY ["Directory.Packages.props", "./"]
COPY ["Directory.Build.props", "./"]

# Copy project files for restore
COPY ["src/Melodee.Blazor/Melodee.Blazor.csproj", "src/Melodee.Blazor/"]
COPY ["src/Melodee.Cli/Melodee.Cli.csproj", "src/Melodee.Cli/"]
COPY ["src/Melodee.Common/Melodee.Common.csproj", "src/Melodee.Common/"]
COPY ["src/Melodee.Mql/Melodee.Mql.csproj", "src/Melodee.Mql/"]

# Restore as distinct layers
RUN dotnet restore "src/Melodee.Blazor/Melodee.Blazor.csproj"
RUN dotnet restore "src/Melodee.Cli/Melodee.Cli.csproj"

# Copy everything else and build
COPY ["src/Melodee.Blazor/", "src/Melodee.Blazor/"]
COPY ["src/Melodee.Cli/", "src/Melodee.Cli/"]
COPY ["src/Melodee.Common/", "src/Melodee.Common/"]
COPY ["src/Melodee.Mql/", "src/Melodee.Mql/"]

WORKDIR "/src/src/Melodee.Blazor"
RUN dotnet build "Melodee.Blazor.csproj" -c Release -o /app/build

# Publish stage
FROM build AS publish
RUN dotnet publish "Melodee.Blazor.csproj" -c Release -o /app/publish --self-contained false -p:PublishTrimmed=false
WORKDIR "/src/src/Melodee.Cli"
RUN dotnet publish "Melodee.Cli.csproj" -c Release -o /app/cli --self-contained false -p:PublishTrimmed=false

# Migration bundle stage - create a self-contained migration bundle
FROM build AS migrations
WORKDIR /src/src/Melodee.Blazor
RUN dotnet tool install --global dotnet-ef --version 10.0.9
ENV PATH="$PATH:/root/.dotnet/tools"
# Provide a dummy connection string for design-time DbContext creation
ENV ConnectionStrings__DefaultConnection="Host=localhost;Database=melodee;Username=melodee;Password=melodee"
RUN dotnet ef migrations bundle --context MelodeeDbContext --output /app/efbundle --self-contained --force --project ../Melodee.Common/Melodee.Common.csproj

# Final runtime image - Ubuntu 26.04 provides current FFmpeg and OS packages.
FROM mcr.microsoft.com/dotnet/aspnet:10.0-resolute AS final
WORKDIR /app
EXPOSE 8080

# Install current security updates and required runtime dependencies.
# Ubuntu 26.04 includes both GNU and Rust coreutils; select the GNU provider so
# the unused Rust implementation is not retained in the production image.
RUN apt-get update && \
    DEBIAN_FRONTEND=noninteractive apt-get upgrade -y --no-install-recommends && \
    DEBIAN_FRONTEND=noninteractive apt-get install -y --no-install-recommends \
        --allow-remove-essential \
        coreutils-from-gnu \
        coreutils-from-uutils- \
        ffmpeg \
        postgresql-client \
        curl \
        lbzip2 \
    && apt-get purge -y rust-coreutils \
    && rm -f /usr/bin/pebble \
    && apt-get check \
    && test -z "$(dpkg --audit)" \
    && dpkg-query --status coreutils coreutils-from-gnu gnu-coreutils >/dev/null \
    && ! dpkg-query --status rust-coreutils >/dev/null 2>&1 \
    && for required_command in dotnet ffmpeg ffprobe pg_isready curl lbzip2 setpriv; do \
        command -v "$required_command" >/dev/null || exit 1; \
    done \
    && test ! -e /usr/bin/pebble \
    && rm -rf /var/lib/apt/lists/*

# Copy the published application
COPY --from=publish /app/publish .
COPY --from=publish /app/cli /app/cli

# Copy the EF migration bundle
COPY --from=migrations /app/efbundle /app/efbundle
RUN chmod +x /app/efbundle

# Copy the entrypoint script
COPY entrypoint.sh /entrypoint.sh
RUN chmod +x /entrypoint.sh

# Create a non-root user
RUN groupadd -r melodee && useradd -r -g melodee -m melodee

# Create volume directories
# These serve as mount points; the actual volumes will overlay them
# Note: podcasts and themes can be at /app/podcasts or /app/storage/podcasts depending on DB config
RUN mkdir -p \
        /app/storage \
        /app/storage/podcasts \
        /app/storage/themes \
        /app/podcasts \
        /app/themes \
        /app/inbound \
        /app/staging \
        /app/user-images \
        /app/playlists \
        /app/templates \
        /app/Logs \
        /home/melodee/.aspnet/DataProtection-Keys \
    && chown -R melodee:melodee /app /home/melodee/.aspnet

# Set environment variables
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1
ENV HOME="/home/melodee"
ENV MELODEE_STORAGE_PATH="/app/storage"
ENV SEARCHENGINE_MUSICBRAINZ_STORAGEPATH="/app/storage/_search-engines/musicbrainz"
ENV MELODEE_INBOUND_PATH="/app/inbound"
ENV MELODEE_STAGING_PATH="/app/staging"
ENV MELODEE_USER_IMAGES_PATH="/app/user-images"
ENV MELODEE_PLAYLISTS_PATH="/app/playlists"
ENV MELODEE_TEMPLATES_PATH="/app/templates"

# Compose overrides this to root only while repairing named-volume ownership;
# direct image consumers run with least privilege by default.
USER melodee

# Use entrypoint script for proper startup
ENTRYPOINT ["/entrypoint.sh"]

# Health check
HEALTHCHECK --interval=30s --timeout=5s --start-period=60s --retries=3 \
    CMD curl -fsS http://localhost:8080/health || exit 1

# OCI Labels
LABEL org.opencontainers.image.source="https://github.com/melodee-project/melodee"
LABEL org.opencontainers.image.description="Melodee music server"
LABEL org.opencontainers.image.licenses="MIT"

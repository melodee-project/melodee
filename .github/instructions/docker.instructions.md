---
description: 'Docker and container best practices for Dockerfiles and container configuration'
applyTo: '**/Dockerfile*, **/docker-compose*.yml, **/compose*.yml'
---

# Docker Development

## Dockerfile Best Practices

### Base Images

- Use official images from trusted sources
- Pin specific versions, avoid `latest` tag
- Prefer slim/alpine variants when possible for smaller images
- Use .NET SDK image for build, runtime image for final stage

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
```

### Multi-Stage Builds

- Use multi-stage builds to minimize final image size
- Name build stages for clarity
- Copy only necessary artifacts to final stage

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["src/Melodee.Blazor/Melodee.Blazor.csproj", "src/Melodee.Blazor/"]
RUN dotnet restore "src/Melodee.Blazor/Melodee.Blazor.csproj"
COPY . .
RUN dotnet publish -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "server.dll"]
```

### Layer Optimization

- Order instructions from least to most frequently changing
- Copy dependency files (*.csproj) before source code
- Combine related RUN commands to reduce layers
- Use `.dockerignore` to exclude unnecessary files

### Security

- Run containers as non-root user
- Don't store secrets in images; use environment variables or secrets management
- Scan images for vulnerabilities
- Remove unnecessary packages and files

```dockerfile
RUN adduser --disabled-password --gecos "" appuser
USER appuser
```

### Health Checks

- Include HEALTHCHECK instruction for production images
- Use appropriate intervals and timeouts

```dockerfile
HEALTHCHECK --interval=30s --timeout=3s --start-period=5s --retries=3 \
    CMD curl -f http://localhost:8080/health || exit 1
```

## Docker Compose

### Service Configuration

- Use explicit version for compose file format
- Define resource limits for services
- Use named volumes for persistent data
- Define networks for service isolation

### Environment Variables

- Use `.env` files for local development
- Never commit secrets to version control
- Use `${VARIABLE:-default}` syntax for defaults

### Networking

- Use custom networks instead of default bridge
- Expose only necessary ports
- Use internal networks for backend services

## Labels and Metadata

- Add OCI labels for image metadata
- Include maintainer and version information

```dockerfile
LABEL org.opencontainers.image.source="https://github.com/melodee-project/melodee"
LABEL org.opencontainers.image.description="Melodee music server"
LABEL org.opencontainers.image.version="1.0.0"
```

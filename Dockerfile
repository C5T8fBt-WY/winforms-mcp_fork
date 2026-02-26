# Multi-stage build for C5T8fBtWY.WinFormsMcp
# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:8.0-windowsservercore-ltsc2022 AS builder

WORKDIR /build

# Copy solution and project files
COPY C5T8fBtWY.WinFormsMcp.sln .
COPY src/Rhombus.WinFormsMcp.Server/C5T8fBtWY.WinFormsMcp.Server.csproj ./src/Rhombus.WinFormsMcp.Server/
COPY src/Rhombus.WinFormsMcp.TestApp/C5T8fBtWY.WinFormsMcp.TestApp.csproj ./src/Rhombus.WinFormsMcp.TestApp/
COPY tests/Rhombus.WinFormsMcp.Tests/C5T8fBtWY.WinFormsMcp.Tests.csproj ./tests/Rhombus.WinFormsMcp.Tests/

# Restore dependencies
RUN dotnet restore

# Copy source code
COPY src/ ./src/
COPY tests/ ./tests/

# Build release
RUN dotnet build -c Release --no-restore
RUN dotnet publish src/Rhombus.WinFormsMcp.Server/C5T8fBtWY.WinFormsMcp.Server.csproj -c Release -o /publish

# Stage 2: Runtime
FROM mcr.microsoft.com/dotnet/runtime:8.0-windowsservercore-ltsc2022

WORKDIR /app

# Copy published binaries
COPY --from=builder /publish .

# Set entry point
ENTRYPOINT ["C5T8fBtWY.WinFormsMcp.Server.exe"]

# Labels
LABEL org.opencontainers.image.title="C5T8fBtWY.WinFormsMcp"
LABEL org.opencontainers.image.description="WinForms automation MCP server with headless UI automation capabilities"
LABEL org.opencontainers.image.authors="c5t8fbt-wy"
LABEL org.opencontainers.image.url="https://github.com/C5T8fBt-WY/winforms-mcp_fork"
LABEL org.opencontainers.image.source="https://github.com/C5T8fBt-WY/winforms-mcp_fork"
LABEL org.opencontainers.image.licenses="MIT"

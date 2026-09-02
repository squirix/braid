# syntax=docker/dockerfile:1
#
# PVS-Studio analysis image for the braid repository.
#
# Pre-bakes the PVS-Studio static analyzer (pvs-studio, pvs-studio-dotnet)
# on top of the .NET 10.0.400 SDK so CI job steps run inside a container that
# already has the analyzer installed. The analyzer license is NOT baked in:
# each CI run activates it at runtime via `pvs-studio-analyzer credentials`
# from the PVS_STUDIO_CREDENTIALS secret.

# Use Ubuntu base and install .NET SDK 10.0.400 via Microsoft's script
FROM ubuntu:24.04

ENV DOTNET_ROOT=/usr/share/dotnet \
    PATH="/usr/share/dotnet:${PATH}" \
    DOTNET_NOLOGO=1 \
    DOTNET_CLI_TELEMETRY_OPTOUT=1

# Install dependencies for .NET SDK and PVS-Studio.
RUN apt-get update \
    && apt-get install -y --no-install-recommends \
        ca-certificates \
        curl \
        gpg \
        wget \
        iproute2 \
    && rm -rf /var/lib/apt/lists/*

# Install the PVS-Studio apt repository and the analyzer packages.
# pvs-studio-dotnet pulls apt's own dotnet (host + SDK 10.0.1xx) as a
# dependency and installs it under /usr/lib/dotnet. That apt dotnet must NOT
# become the .NET SDK used by CI, so the SDK install below must come AFTER
# this layer and land on top of it in /usr/share/dotnet.
RUN apt-get update \
    && apt-get install -y --no-install-recommends gpg wget \
    && wget -qO- https://files.pvs-studio.com/etc/pubkey.txt \
        | gpg --dearmor -o /etc/apt/trusted.gpg.d/viva64.gpg \
    && printf 'deb [arch=amd64] https://files.pvs-studio.com/deb viva64-release pvs-studio\n' \
        > /etc/apt/sources.list.d/viva64.list \
    && apt-get update \
    && apt-get install -y --no-install-recommends pvs-studio pvs-studio-dotnet \
    && rm -rf /var/lib/apt/lists/*

# Verify PVS-Studio installation.
RUN pvs-studio-analyzer --version

# Install .NET SDK via Microsoft's install script, AFTER the pvs-studio apt
# packages, so this SDK (10.0.400) is the one on PATH and under DOTNET_ROOT.
# --channel 10.0.4xx selects the latest patch in the 10.0.4xx feature band
# (e.g. 10.0.400), matching global.json's version 10.0.400 + rollForward latestFeature.
RUN curl -SL https://dotnet.microsoft.com/download/dotnet/scripts/v1/dotnet-install.sh \
        --output /dotnet-install.sh \
    && chmod +x /dotnet-install.sh \
    && /dotnet-install.sh --channel 10.0.4xx --install-dir "$DOTNET_ROOT" \
    && ln -sf "$DOTNET_ROOT/dotnet" /usr/bin/dotnet \
    && rm /dotnet-install.sh

# Verify the .NET SDK that CI will actually use.
RUN dotnet --list-sdks

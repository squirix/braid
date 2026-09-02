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

# Install dependencies for .NET SDK and PVS-Studio
RUN apt-get update \
    && apt-get install -y --no-install-recommends \
        ca-certificates \
        curl \
        gpg \
        wget \
        iproute2 \
    && rm -rf /var/lib/apt/lists/*

# Install .NET SDK 10.0.400 using Microsoft's install script
RUN DOTNET_VERSION=10.0.400 \
    && curl -SL https://dotnet.microsoft.com/download/dotnet/scripts/v1/dotnet-install.sh \
        --output /dotnet-install.sh \
    && chmod +x /dotnet-install.sh \
    && /dotnet-install.sh --channel 10.0.400 --install-dir /usr/share/dotnet \
    && ln -s /usr/share/dotnet/dotnet /usr/bin/dotnet \
    && rm /dotnet-install.sh

# Verify .NET installation
RUN dotnet --info

# Install the PVS-Studio apt repository and the analyzer packages.
RUN apt-get update \
    && apt-get install -y --no-install-recommends gpg wget \
    && wget -qO- https://files.pvs-studio.com/etc/pubkey.txt \
        | gpg --dearmor -o /etc/apt/trusted.gpg.d/viva64.gpg \
    && printf 'deb [arch=amd64] https://files.pvs-studio.com/deb viva64-release pvs-studio\n' \
        > /etc/apt/sources.list.d/viva64.list \
    && apt-get update \
    && apt-get install -y --no-install-recommends pvs-studio pvs-studio-dotnet \
    && rm -rf /var/lib/apt/lists/*

# Verify PVS-Studio installation
RUN pvs-studio-analyzer --version
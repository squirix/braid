# syntax=docker/dockerfile:1
#
# PVS-Studio analysis image for the braid repository.
#
# Pre-bakes the PVS-Studio static analyzer (pvs-studio, pvs-studio-dotnet)
# on top of the .NET 10.0.400 SDK so CI job steps run inside a container that
# already has the analyzer installed. The analyzer license is NOT baked in:
# each CI run activates it at runtime via `pvs-studio-analyzer credentials`
# from the PVS_STUDIO_CREDENTIALS secret.
FROM mcr.microsoft.com/dotnet/sdk:10.0.400

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

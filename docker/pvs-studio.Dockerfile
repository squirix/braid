# syntax=docker/dockerfile:1
#
# PVS-Studio analysis image for the braid repository.
#
# Pre-bakes the PVS-Studio static analyzer (pvs-studio, pvs-studio-dotnet)
# on top of the .NET 10.0.401 SDK so CI job steps run inside a container that
# already has the analyzer installed. The analyzer license is NOT baked in:
# each CI run activates it at runtime via `pvs-studio-analyzer credentials`
# from the PVS_STUDIO_CREDENTIALS secret.

# Use Ubuntu base and install .NET SDK 10.0.401 via Microsoft's script
FROM ubuntu:26.04

ENV DOTNET_ROOT=/usr/share/dotnet \
    PATH="/usr/share/dotnet:${PATH}" \
    DOTNET_NOLOGO=1 \
    DOTNET_CLI_TELEMETRY_OPTOUT=1

# Pin downloaded bytes so redirects or upstream changes cannot substitute content.
# Refresh these checksums together after verifying new upstream files.
ADD --checksum=sha256:c7489168c0423756c998bc1db9a70cb90e85a852efce8d4cdb0f41dd6ad41cd3 \
    https://files.pvs-studio.com/etc/pubkey.txt /tmp/pvs-key.asc
ADD --checksum=sha256:082f7685e156738a1b2e2ed8381a621870d4ce8e8c59278034556f05c186eb2e \
    https://builds.dotnet.microsoft.com/dotnet/scripts/v1/dotnet-install.sh /tmp/dotnet-install.sh
ADD --checksum=sha256:3d0a1bdf59c7b732886544e3a76e7b526c247a823d85b498019aba6442720d83 \
    https://builds.dotnet.microsoft.com/dotnet/scripts/v1/dotnet-install.sig /tmp/dotnet-install.sig
ADD --checksum=sha256:3658c5df9fdb53326bd27bfb23430d2e7e1e93b4be83a71bae0f13d1b0cd7cb4 \
    https://builds.dotnet.microsoft.com/dotnet/scripts/v1/dotnet-install.asc /tmp/dotnet-install.asc

# Install the SDK after PVS-Studio's apt dependencies so it takes precedence.
# Root is needed for package installation; analysis runs as the user below.
# Bash provides pipefail for signature verification.
SHELL ["/bin/bash", "-c"]
# Install dependencies for .NET SDK and PVS-Studio.
# gnupg (not the transitional gpg wrapper) provides gpg-agent, which the
# dotnet-install.sh signature verification requires on Ubuntu 26.04.
RUN set -euo pipefail \
    && apt-get update \
    && apt-get install -y --no-install-recommends \
        ca-certificates \
        curl \
        gnupg \
        iproute2 \
    && gpg --batch --dearmor -o /etc/apt/trusted.gpg.d/viva64.gpg /tmp/pvs-key.asc \
    && printf 'deb [arch=amd64] https://files.pvs-studio.com/deb viva64-release pvs-studio\n' \
        > /etc/apt/sources.list.d/viva64.list \
    && apt-get update \
    && apt-get install -y --no-install-recommends pvs-studio pvs-studio-dotnet \
    && pvs-studio-analyzer --version \
    && pvs-studio-dotnet --version \
    && mkdir -p /tmp/gpghome \
    && chmod 700 /tmp/gpghome \
    && gpg --batch --homedir /tmp/gpghome --import /tmp/dotnet-install.asc \
    && gpg --batch --homedir /tmp/gpghome --show-keys --with-colons /tmp/dotnet-install.asc \
        | grep -qi ":2b930ab1228d11d5d7f6b6acb9cf1a51fc7d3acf:" \
    && gpg --batch --homedir /tmp/gpghome --verify /tmp/dotnet-install.sig /tmp/dotnet-install.sh \
    && chmod +x /tmp/dotnet-install.sh \
    && /tmp/dotnet-install.sh --version 10.0.401 --install-dir "$DOTNET_ROOT" \
    && ln -sf "$DOTNET_ROOT/dotnet" /usr/bin/dotnet \
    && dotnet --list-sdks \
    && rm -rf /var/lib/apt/lists/* /tmp/gpghome /tmp/pvs-key.asc \
        /tmp/dotnet-install.sh /tmp/dotnet-install.sig /tmp/dotnet-install.asc \
    && useradd --create-home --uid 1001 --shell /bin/bash analyzer

USER analyzer
WORKDIR /home/analyzer

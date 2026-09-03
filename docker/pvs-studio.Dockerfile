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
FROM ubuntu:26.04

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
RUN pvs-studio-analyzer --version \
    && pvs-studio-dotnet --version

# Install .NET SDK via Microsoft's install script, AFTER the pvs-studio apt
# packages, so this SDK (10.0.400) is the one on PATH and under DOTNET_ROOT.
# --channel 10.0.4xx selects the latest patch in the 10.0.4xx feature band
# (e.g. 10.0.400), matching global.json's version 10.0.400 + rollForward latestFeature.
#
# The install script is verified before running: it is downloaded alongside
# Microsoft's detached GPG signature and public key, the key fingerprint is
# pinned to the known-good Microsoft signing key, and the script must verify
# against that key or the build fails.
#
# Run the install and verification under Bash: the default /bin/sh (dash) on
# Ubuntu does not implement `set -o pipefail`, which this pipeline relies on.
SHELL ["/bin/bash", "-c"]
RUN set -euo pipefail \
    && mkdir -p /tmp/gpghome \
    && chmod 700 /tmp/gpghome \
    && curl -SL https://dot.net/v1/dotnet-install.sh --output /tmp/dotnet-install.sh \
    && curl -SL https://dot.net/v1/dotnet-install.sig --output /tmp/dotnet-install.sig \
    && curl -SL https://dot.net/v1/dotnet-install.asc --output /tmp/dotnet-install.asc \
    && gpg --batch --homedir /tmp/gpghome --import /tmp/dotnet-install.asc \
    && gpg --batch --homedir /tmp/gpghome --show-keys --with-colons /tmp/dotnet-install.asc \
        | grep -qi ":2b930ab1228d11d5d7f6b6acb9cf1a51fc7d3acf:" \
    && gpg --batch --homedir /tmp/gpghome --verify /tmp/dotnet-install.sig /tmp/dotnet-install.sh \
    && chmod +x /tmp/dotnet-install.sh \
    && /tmp/dotnet-install.sh --channel 10.0.4xx --install-dir "$DOTNET_ROOT" \
    && ln -sf "$DOTNET_ROOT/dotnet" /usr/bin/dotnet \
    && rm -rf /tmp/gpghome /tmp/dotnet-install.sh /tmp/dotnet-install.sig /tmp/dotnet-install.asc

# Verify the .NET SDK that CI will actually use.
RUN dotnet --list-sdks

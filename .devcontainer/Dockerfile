# Одноконтейнерная среда разработки C# (.NET 8).
# Базовый образ devcontainers/dotnet уже включает dotnet SDK 8.0, user vscode и common utils.
FROM mcr.microsoft.com/devcontainers/dotnet:8.0

# Утилиты для работы с протоколом/трафиком RAS и анализа
RUN apt-get update && apt-get install -y --no-install-recommends \
    tcpdump \
    tshark \
    net-tools \
    htop \
    jq \
    file \
    && rm -rf /var/lib/apt/lists/* \
    && apt-get clean

# Opencode CLI (ставим от user vscode, чтобы PATH/кэш совпадали с удалённым пользователем devcontainer)
USER vscode
RUN mkdir -p ~/.local/share/opencode ~/.cache/opencode \
    && curl -fsSL https://opencode.ai/install | bash

ENV PATH="/home/vscode/.opencode/bin:${PATH}"
ENV SHELL=/bin/bash
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1
ENV DOTNET_NOLOGO=1

WORKDIR /workspaces/1c-rac-lib
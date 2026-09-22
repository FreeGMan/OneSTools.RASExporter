# Образ услуги mock-ras: сборка из исходников workspace, запуск .NET 8 runtime.
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Сначала копируем только csproj (для кэша восстановления зависимостей)
COPY Directory.Build.props ./
COPY src/OneSTools.Ras.Client/OneSTools.Ras.Client.csproj src/OneSTools.Ras.Client/
COPY src/OneSTools.Ras.MockServer/OneSTools.Ras.MockServer.csproj src/OneSTools.Ras.MockServer/
RUN dotnet restore src/OneSTools.Ras.MockServer/OneSTools.Ras.MockServer.csproj

COPY src/ src/
RUN dotnet publish src/OneSTools.Ras.MockServer/OneSTools.Ras.MockServer.csproj \
    -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/runtime:8.0
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 1545
ENTRYPOINT ["dotnet", "OneSTools.Ras.MockServer.dll"]
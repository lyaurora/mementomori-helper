FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG TARGETARCH
WORKDIR /src
COPY global.json ./
COPY MementoMori.WebUI/MementoMori.WebUI.csproj MementoMori.WebUI/
COPY MementoMori.Common/MementoMori.Common.csproj MementoMori.Common/
COPY MementoMori.BlazorShared/MementoMori.BlazorShared.csproj MementoMori.BlazorShared/
COPY MementoMori/MementoMori.csproj MementoMori/
COPY MementoMori.Ortega/MementoMori.Ortega.csproj MementoMori.Ortega/
RUN dotnet restore MementoMori.WebUI/MementoMori.WebUI.csproj -a $TARGETARCH
COPY . .
RUN dotnet publish MementoMori.WebUI/MementoMori.WebUI.csproj -c Release -a $TARGETARCH --no-restore -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
EXPOSE 8080
COPY --from=build /app/publish .
HEALTHCHECK --interval=1m --timeout=10s --start-period=3m --retries=3 CMD ["dotnet", "MementoMori.WebUI.dll", "--healthcheck", "http://127.0.0.1:8080/healthz"]
ENTRYPOINT ["dotnet", "MementoMori.WebUI.dll"]

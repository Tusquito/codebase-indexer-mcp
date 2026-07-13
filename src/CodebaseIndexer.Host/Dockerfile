FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY CodebaseIndexer.slnx ./
COPY global.json ./
COPY Directory.Build.props ./
COPY src/CodebaseIndexer.Domain/ src/CodebaseIndexer.Domain/
COPY src/CodebaseIndexer.Application/ src/CodebaseIndexer.Application/
COPY src/CodebaseIndexer.Infrastructure/ src/CodebaseIndexer.Infrastructure/
COPY src/CodebaseIndexer.ServiceDefaults/ src/CodebaseIndexer.ServiceDefaults/
COPY src/CodebaseIndexer.Host/ src/CodebaseIndexer.Host/
RUN dotnet restore src/CodebaseIndexer.Host/CodebaseIndexer.Host.csproj
RUN dotnet publish src/CodebaseIndexer.Host/CodebaseIndexer.Host.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8000
ENV ASPNETCORE_URLS=http://0.0.0.0:8000
ENV ASPNETCORE_ENVIRONMENT=Docker
ENTRYPOINT ["dotnet", "CodebaseIndexer.Host.dll"]

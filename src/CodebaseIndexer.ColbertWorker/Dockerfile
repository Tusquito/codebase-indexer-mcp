FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY CodebaseIndexer.slnx ./
COPY global.json ./
COPY Directory.Build.props ./
COPY src/CodebaseIndexer.Domain/ src/CodebaseIndexer.Domain/
COPY src/CodebaseIndexer.Application/ src/CodebaseIndexer.Application/
COPY src/CodebaseIndexer.Infrastructure/ src/CodebaseIndexer.Infrastructure/
COPY src/CodebaseIndexer.ServiceDefaults/ src/CodebaseIndexer.ServiceDefaults/
COPY src/CodebaseIndexer.ColbertWorker/ src/CodebaseIndexer.ColbertWorker/
RUN dotnet restore src/CodebaseIndexer.ColbertWorker/CodebaseIndexer.ColbertWorker.csproj
RUN dotnet publish src/CodebaseIndexer.ColbertWorker/CodebaseIndexer.ColbertWorker.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8082
ENV ASPNETCORE_URLS=http://0.0.0.0:8082
ENV ASPNETCORE_ENVIRONMENT=Production
ENV Colbert__UseCuda=false
ENTRYPOINT ["dotnet", "CodebaseIndexer.ColbertWorker.dll"]

# Production image for the API (Railway builds it from this folder).
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json ./
COPY src/Ailu.Api/Ailu.Api.csproj src/Ailu.Api/
RUN dotnet restore src/Ailu.Api/Ailu.Api.csproj
COPY src/Ailu.Api/ src/Ailu.Api/
RUN dotnet publish src/Ailu.Api/Ailu.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app ./
ENV ASPNETCORE_ENVIRONMENT=Production \
    Photos__Root=/data/photos \
    Photos__ArchiveRoot=/data/photos-archive
# Railway passes the port to listen on in PORT; 8080 when run anywhere else.
CMD ["sh", "-c", "ASPNETCORE_HTTP_PORTS=${PORT:-8080} exec dotnet Ailu.Api.dll"]

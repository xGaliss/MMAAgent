FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY MMAAgent.sln ./
COPY MMAAgent.Application/MMAAgent.Application.csproj MMAAgent.Application/
COPY MMAAgent.Domain/MMAAgent.Domain.csproj MMAAgent.Domain/
COPY MMAAgent.Infrastructure/MMAAgent.Infrastructure.csproj MMAAgent.Infrastructure/
COPY MMAAgent.Web/MMAAgent.Web.csproj MMAAgent.Web/

RUN dotnet restore ./MMAAgent.Web/MMAAgent.Web.csproj

COPY . .
RUN dotnet publish ./MMAAgent.Web/MMAAgent.Web.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

ENV PORT=8080
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

RUN mkdir -p /data/saves

COPY --from=build /app/publish .

EXPOSE 8080

ENTRYPOINT ["sh", "-c", "ASPNETCORE_URLS=http://+:${PORT:-8080} dotnet MMAAgent.Web.dll"]

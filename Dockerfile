# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["Chat.sln", "."]
COPY ["src/Chat.Api/Chat.Api.csproj", "src/Chat.Api/"]
COPY ["tests/Chat.Api.Tests/Chat.Api.Tests.csproj", "tests/Chat.Api.Tests/"]
RUN dotnet restore
COPY . .
WORKDIR "/src/src/Chat.Api"
RUN dotnet build -c Release -o /app/build

# Publish stage
FROM build AS publish
WORKDIR "/src/src/Chat.Api"
RUN dotnet publish -c Release -o /app/publish

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=publish /app/publish .
RUN useradd -m -u 1000 appuser 2>/dev/null || useradd -m appuser
RUN chown -R appuser:appuser /app
USER appuser
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "Chat.Api.dll"]

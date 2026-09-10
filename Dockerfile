# Base image
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443

# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["FlexiFit.Api/FlexiFit.Api.csproj", "FlexiFit.Api/"]
RUN dotnet restore "FlexiFit.Api/FlexiFit.Api.csproj"
COPY . .
WORKDIR "/src/FlexiFit.Api"
RUN dotnet build "FlexiFit.Api.csproj" -c Release -o /app/build

# Publish stage
FROM build AS publish
RUN dotnet publish "FlexiFit.Api.csproj" -c Release -o /app/publish

# Final stage
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "FlexiFit.Api.dll"]

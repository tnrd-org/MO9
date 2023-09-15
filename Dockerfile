FROM mcr.microsoft.com/dotnet/runtime:7.0 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:7.0 AS build
WORKDIR /src
COPY ["MO9/MO9.csproj", "MO9/"]
RUN dotnet restore "MO9/MO9.csproj"
COPY . .
WORKDIR "/src/MO9"
RUN dotnet build "MO9.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "MO9.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "MO9.dll"]

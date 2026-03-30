FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["Paw.Web/Paw.Web.csproj", "Paw.Web/"]
COPY ["Paw.Core/Paw.Core.csproj", "Paw.Core/"]
RUN dotnet restore "Paw.Web/Paw.Web.csproj"
COPY . .
WORKDIR "/src/Paw.Web"
RUN dotnet build "Paw.Web.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "Paw.Web.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Paw.Web.dll"]


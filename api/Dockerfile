FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props ./
COPY src/Florisa.Api/Florisa.Api.csproj src/Florisa.Api/
RUN dotnet restore src/Florisa.Api/Florisa.Api.csproj
COPY src/ src/
RUN dotnet publish src/Florisa.Api/Florisa.Api.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
USER app
ENTRYPOINT ["dotnet", "Florisa.Api.dll"]

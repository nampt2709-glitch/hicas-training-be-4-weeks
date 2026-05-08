FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ApartmentAPI/ApartmentAPI.csproj ApartmentAPI/
RUN dotnet restore ApartmentAPI/ApartmentAPI.csproj
COPY ApartmentAPI/ ApartmentAPI/
RUN dotnet publish ApartmentAPI/ApartmentAPI.csproj -c Release -o /app/publish

# Runtime: cổng nội bộ 8080; host map cổng khác trong docker-compose.
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "ApartmentAPI.dll"]

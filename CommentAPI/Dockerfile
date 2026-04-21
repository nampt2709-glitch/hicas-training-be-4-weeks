FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY CommentAPI/CommentAPI.csproj CommentAPI/
RUN dotnet restore CommentAPI/CommentAPI.csproj
COPY CommentAPI/ CommentAPI/
RUN dotnet publish CommentAPI/CommentAPI.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "CommentAPI.dll"]

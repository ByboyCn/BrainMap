FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY MindMap.Backend.csproj ./
RUN dotnet restore MindMap.Backend.csproj

COPY . ./
RUN dotnet publish MindMap.Backend.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

RUN mkdir -p /app/data

COPY --from=build /app/publish ./

ENV ASPNETCORE_URLS=http://0.0.0.0:5289
EXPOSE 5289

ENTRYPOINT ["dotnet", "MindMap.Backend.dll"]

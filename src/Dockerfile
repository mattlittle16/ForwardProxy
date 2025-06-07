FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /app 

COPY . .

RUN dotnet restore "ForwardProxy.csproj"

RUN dotnet publish "ForwardProxy.csproj" -c Debug -o out 

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime 
WORKDIR /app 
COPY --from=build /app/out ./ 

RUN apt-get update && apt-get install -y curl

EXPOSE 80
ENV ASPNETCORE_URLS="http://*:80"

ENTRYPOINT ["dotnet", "ForwardProxy.dll"]
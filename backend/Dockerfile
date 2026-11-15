# Build context: repository root (needs shared-contracts/ and database/seed/)
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY shared-contracts/ shared-contracts/
COPY database/seed/ database/seed/
COPY backend/ backend/
RUN dotnet publish backend/src/PetStar.Api/PetStar.Api.csproj -c Release -o /app /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
RUN groupadd -r petstar && useradd -r -g petstar petstar
COPY --from=build /app .
USER petstar
ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_RUNNING_IN_CONTAINER=true
EXPOSE 8080
ENTRYPOINT ["dotnet", "PetStar.Api.dll"]

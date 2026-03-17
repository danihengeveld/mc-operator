FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /build

COPY mc-operator.sln ./
COPY src/McOperator/McOperator.csproj src/McOperator/
COPY src/McOperator.Tests/McOperator.Tests.csproj src/McOperator.Tests/

RUN dotnet restore mc-operator.sln

COPY src/ src/

RUN dotnet publish src/McOperator/McOperator.csproj -c Release -o /app --no-restore

# Production runtime image
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final

# Create a non-root user
RUN groupadd -g 1001 mc-operator && \
    useradd -u 1001 -g mc-operator -s /sbin/nologin mc-operator

WORKDIR /app

COPY --chown=mc-operator:mc-operator --from=build /app ./

USER mc-operator

ENTRYPOINT ["dotnet", "McOperator.dll"]

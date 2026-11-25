# Build
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/LearnChess.Web/LearnChess.Web.csproj src/LearnChess.Web/
RUN dotnet restore src/LearnChess.Web/LearnChess.Web.csproj
COPY src/ src/
RUN dotnet publish src/LearnChess.Web/LearnChess.Web.csproj -c Release -o /app --no-restore

# Execução
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
# Pasta onde as partidas JSON são salvas (o usuário "app" da imagem precisa poder escrever)
RUN mkdir -p /app/data/games && chown -R app:app /app/data
USER app
ENV ASPNETCORE_ENVIRONMENT=Production
# O Render informa a porta na variável PORT
CMD ["sh", "-c", "ASPNETCORE_URLS=http://0.0.0.0:${PORT:-8080} exec dotnet LearnChess.Web.dll"]

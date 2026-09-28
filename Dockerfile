# Receipt Tracker in one image: the API with the web app built in (the same app as ReceiptTracker.exe).
# Used by docker-compose.yml; see "Run with Docker" in README.md.

# 1. Build the web app (web/dist).
FROM node:24-slim AS web
WORKDIR /web
COPY web/package.json web/package-lock.json ./
RUN npm ci --no-audit --no-fund
COPY web/ ./
# `npm run build` type-checks the tests too, and format.test.ts imports the VAT cases shared with the API and eval.
COPY tests/vat-cases.json /tests/vat-cases.json
RUN npm run build

# 2. Publish the API with the web app in its wwwroot (what publish.cmd does for the exe).
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS api
WORKDIR /src
COPY src/ReceiptExtractor.Api/ReceiptExtractor.Api.csproj src/ReceiptExtractor.Api/
RUN dotnet restore src/ReceiptExtractor.Api/ReceiptExtractor.Api.csproj
COPY src/ReceiptExtractor.Api/ src/ReceiptExtractor.Api/
COPY --from=web /web/dist/ src/ReceiptExtractor.Api/wwwroot/
RUN dotnet publish src/ReceiptExtractor.Api/ReceiptExtractor.Api.csproj -c Release -o /out --no-restore -p:UseAppHost=false

# 3. Run it.
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=api /out ./
# The database and receipt images go in /app/data ("data" next to the app, Hosting/DesktopMode.cs);
# docker-compose.yml keeps it in a volume. Created here so the non-root user can write to it.
RUN mkdir /app/data && chown "$APP_UID" /app/data
# Demo data for an empty database (Seeding/): the labeled receipts and their photos. Used when Seed__Enabled=true.
COPY datasets/labels/ /app/seed/labels/
COPY datasets/receipts/ /app/seed/receipts/
USER $APP_UID
# Same port as the exe, so the address it prints (http://localhost:5080) is right with the default port mapping.
# (HTTP_PORTS is cleared: the base image sets it to 8080, and ASP.NET Core warns when URLS overrides it.)
ENV ASPNETCORE_URLS=http://+:5080 \
    ASPNETCORE_HTTP_PORTS= \
    OpenBrowser=false \
    Seed__Path=/app/seed
EXPOSE 5080
ENTRYPOINT ["dotnet", "ReceiptExtractor.Api.dll"]

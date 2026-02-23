# 📰 CableNews – AI-Powered Industry Newsletter Agent

Automated news intelligence agent for the cable & electrical solutions industry. Fetches daily news from Google News RSS, summarizes with Gemini AI, and delivers executive reports via email for Colombia, Peru, Chile, and Ecuador.

## Quick Start (Local)

```bash
# Restore & build
dotnet restore CableNews.Worker/CableNews.Worker.csproj
dotnet build CableNews.Worker/CableNews.Worker.csproj

# Run (uses appsettings.Development.json — keep your secrets there)
dotnet run --project CableNews.Worker/CableNews.Worker.csproj
```

## GitHub Actions Setup

### 1. Create the repository and push

```bash
git init
git add .
git commit -m "initial commit"
git remote add origin https://github.com/YOUR_USER/CableNews.git
git push -u origin main
```

### 2. Add secrets in GitHub

Go to **Settings → Secrets and variables → Actions → New repository secret** and add:

| Secret Name | Value |
|---|---|
| `GEMINI_API_KEY` | Your Gemini API key |
| `SMTP_PASSWORD` | Your Gmail app password |

### 3. Done!

The workflow runs automatically every day at **6:00 AM Colombia time** (11:00 UTC). You can also trigger it manually from **Actions → CableNews Daily Newsletter → Run workflow**.

## Architecture

```
CableNews.Domain          → Entities (Article, CountryConfig)
CableNews.Application     → CQRS commands/handlers via MediatR
CableNews.Infrastructure  → Google News RSS, Gemini API, Gmail SMTP
CableNews.Worker          → .NET Worker entry point + config
```

## Configuration

All country-specific keywords are configured in `appsettings.json` under `NewsAgent.Countries`. Each country has:
- **DemandDrivers** – Energy, infrastructure, construction keywords
- **Institutions** – Government and regulatory bodies
- **Operators** – Major energy/utility companies
- **MacroSignals** – Economic indicators
- **SalesIntelligence** – Competitor tracking, tenders, project leads

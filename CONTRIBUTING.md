# Contributing to PAW

## Local setup

### Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/8)
- [Docker Desktop](https://www.docker.com/products/docker-desktop/) for SQL Server
- A Polar Flow developer account if you need to test the OAuth/webhook flow end-to-end

### First-time setup

```bash
cp .env.example .env          # fill in SA_PASSWORD
make docker-up                # start SQL Server on localhost:1433
make ef-update-db             # apply EF Core migrations
make run-api                  # API at http://localhost:5293
make run-worker               # background worker (separate terminal)
```

### Running tests (no database required)

Tests use an in-memory EF Core database — no real SQL Server needed.

```bash
make test           # ~20-30s, recommended for development
make test-all       # ~40-50s, includes slow worker tests
make test-coverage  # generates HTML report in TestResults/CoverageReport/
```

## Code conventions

- **Comments:** only on truly complex logic. Avoid restating what the code does; explain *why*.
- **Error responses:** use `Results.Problem(...)` for consistent RFC 7807 Problem Details format.
- **Tests:** each test class must use a unique in-memory DB name (prefix with class name + `Guid.NewGuid()`).
- **Secrets:** never commit API keys, connection strings, or tokens. Use `appsettings.Development.json` (git-ignored) or environment variables.

## PR guidelines

1. Keep PRs focused — one concern per PR.
2. All tests must pass: `make test`.
3. New public methods or endpoints should have at least one test.
4. Update `ARCHITECTURE.md` if the data flow changes.

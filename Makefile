# Simple Makefile for the PAW .NET solution
# Usage examples:
#   make build          # Build the solution
#   make test           # Run all tests (EXCLUDES slow worker tests)
#   make test-worker    # Run worker tests only (slower, ~60s)
#   make test-all       # Run ALL tests including worker tests
#   make test-sync      # Run sync tests only
#   make test-watch     # Run tests in watch mode

SOLUTION=Paw.sln
API_PROJ=Paw.Api/Paw.Api.csproj
WORKER_PROJ=Paw.Worker/Paw.Worker.csproj
WEB_PROJ=Paw.Web/Paw.Web.csproj
TEST_PROJ=Paw.Test/Paw.Test.csproj
INFRA_PROJ=Paw.Infrastructure/Paw.Infrastructure.csproj

# Test filters for different categories
UNIT_TESTS=Category=Unit
SYNC_TESTS=FullyQualifiedName~SyncServiceTests|FullyQualifiedName~ActivitySyncServiceTests
WEBHOOK_TESTS=FullyQualifiedName~WebhookFlowEndToEndTests
WORKER_TESTS=FullyQualifiedName~WorkerLoopTests
ENDPOINT_TESTS=FullyQualifiedName~SyncEndpointsTests|FullyQualifiedName~QepPolarEndpointsTests
INTEGRATION_TESTS=FullyQualifiedName~QepMapperIntegrationTests|FullyQualifiedName~PolarLinksIntegrationTests

# All tests EXCEPT worker tests (fast suite)
FAST_TESTS=FullyQualifiedName~SyncServiceTests|FullyQualifiedName~ActivitySyncServiceTests|FullyQualifiedName~WebhookFlowEndToEndTests|FullyQualifiedName~SyncEndpointsTests|FullyQualifiedName~QepPolarEndpointsTests|FullyQualifiedName~QepMapperIntegrationTests|FullyQualifiedName~PolarLinksIntegrationTests|FullyQualifiedName~PolarClientRetryTests|FullyQualifiedName~PolarWebhookVerifierTests|FullyQualifiedName~QepApiKeyAuthFilterTests|FullyQualifiedName~PolarWorkoutMapperTests|FullyQualifiedName~PolarToQepMapperTests

# Default target
.DEFAULT_GOAL := help

help:
	@echo "=== PAW Build & Test Commands ==="
	@echo ""
	@echo "BUILD:"
	@echo "  make build              Build solution"
	@echo "  make rebuild            Clean + build"
	@echo "  make restore            Restore packages"
	@echo ""
	@echo "RUN:"
	@echo "  make run-api            Start Web API (http://localhost:5293)"
	@echo "  make run-web            Start Web Gateway (Paw.Web)"
	@echo "  make run-worker         Start background Worker"
	@echo ""
	@echo "TEST (Pick one):"
	@echo "  make test               Run FAST tests (excludes slow worker tests)"
	@echo "  make test-worker        Run WORKER tests only (~60s)"
	@echo "  make test-all           Run ALL tests including worker tests (~60s)"
	@echo "  make test-sync          Run sync logic tests"
	@echo "  make test-webhook       Run webhook pipeline tests"
	@echo "  make test-endpoints     Run HTTP endpoint tests"
	@echo "  make test-integration   Run database integration tests"
	@echo "  make test-unit          Run unit tests"
	@echo ""
	@echo "TEST HELPERS:"
	@echo "  make test-watch         Auto-rerun tests on code changes"
	@echo "  make test-verbose       Show detailed test output"
	@echo "  make test-quick         Fast test run (minimal output)"
	@echo "  make test-coverage      Generate HTML coverage report"
	@echo ""
	@echo "DATABASE:"
	@echo "  make ef-migrations      List migrations"
	@echo "  make ef-add-mig n=Name  Add migration"
	@echo "  make ef-update-db       Apply migrations"
	@echo "  make docker-up          Start SQL Server (docker-compose)"
	@echo "  make docker-down        Stop SQL Server (docker-compose)"
	@echo "  make db-setup           docker-up + wait + ef-update-db (first-time setup)"

restore:
	dotnet restore $(SOLUTION)

build:
	dotnet build $(SOLUTION)

rebuild: clean build

clean:
	dotnet clean $(SOLUTION)

run-api:
	dotnet run --project $(API_PROJ)

run-web:
	dotnet run --project $(WEB_PROJ)

run-worker:
	dotnet run --project $(WORKER_PROJ)

# ============================================================================
# PRIMARY TEST TARGETS - Use these!
# ============================================================================

test:
	@echo "Running FAST tests (excludes slow worker tests)..."
	@echo "For worker tests, run: make test-worker"
	@dotnet test $(SOLUTION) --filter "$(FAST_TESTS)" --verbosity minimal --nologo

test-all:
	@echo "Running ALL tests including worker tests (~60s)..."
	@dotnet test $(SOLUTION) --verbosity minimal --nologo

test-sync:
	@echo "Running sync tests (SyncServiceTests, ActivitySyncServiceTests)..."
	@dotnet test $(SOLUTION) --filter "$(SYNC_TESTS)" --verbosity minimal --nologo

test-webhook:
	@echo "Running webhook pipeline tests (WebhookFlowEndToEndTests)..."
	@dotnet test $(SOLUTION) --filter "$(WEBHOOK_TESTS)" --verbosity minimal --nologo

test-worker:
	@echo "Running worker tests (WorkerLoopTests) - ~60s..."
	@dotnet test $(SOLUTION) --filter "$(WORKER_TESTS)" --verbosity minimal --nologo

test-endpoints:
	@echo "Running endpoint tests (SyncEndpoints, QepPolarEndpoints)..."
	@dotnet test $(SOLUTION) --filter "$(ENDPOINT_TESTS)" --verbosity normal --nologo

test-integration:
	@echo "Running integration tests (QepMapper, PolarLinks)..."
	@dotnet test $(SOLUTION) --filter "$(INTEGRATION_TESTS)" --verbosity normal --nologo

test-unit:
	@echo "Running unit tests..."
	@dotnet test $(SOLUTION) --filter "$(UNIT_TESTS)" --verbosity minimal --nologo

# ============================================================================
# HELPER TEST TARGETS
# ============================================================================

test-watch:
	@echo "Running tests in watch mode (Ctrl+C to stop)..."
	dotnet watch test $(TEST_PROJ)

test-verbose:
	@echo "Running FAST tests with detailed output..."
	dotnet test $(SOLUTION) --filter "$(FAST_TESTS)" --verbosity normal --nologo

test-quick:
	@echo "Running quick test (minimal output)..."
	dotnet test $(SOLUTION) --filter "$(FAST_TESTS)" --verbosity minimal --nologo

test-coverage:
	@echo "Generating code coverage report..."
	@dotnet test $(TEST_PROJ) \
		--collect:"XPlat Code Coverage" \
		--results-directory ./TestResults \
		--verbosity minimal \
		--nologo \
		--filter "$(FAST_TESTS)"
	@echo "Building HTML report..."
	@reportgenerator \
		-reports:"./TestResults/**/coverage.cobertura.xml" \
		-targetdir:"./TestResults/CoverageReport" \
		-reporttypes:"Html;HtmlSummary;Badges" \
		-title:"PAW Code Coverage (Excludes Worker Tests)"
	@echo ""
	@echo "Coverage report ready:"
	@echo " open TestResults/CoverageReport/index.html"

# ============================================================================
# EF CORE DATABASE HELPERS
# ============================================================================

ef-migrations:
	@echo "Listing EF Core migrations..."
	@dotnet ef migrations list --project $(INFRA_PROJ) --startup-project $(API_PROJ)

ef-add-mig:
	@echo "Adding migration: $(n)"
	@dotnet ef migrations add $(n) --project $(INFRA_PROJ) --startup-project $(API_PROJ)

ef-update-db:
	@echo "Applying migrations to database..."
	@dotnet ef database update --project $(INFRA_PROJ) --startup-project $(API_PROJ)

# ============================================================================
# DOCKER HELPERS
# ============================================================================

docker-up:
	@echo "Starting Docker services (SQL Server, Adminer)..."
	@docker compose up -d
	@echo "SQL Server running on localhost,1433"
	@echo "Adminer running on http://localhost:8080"

docker-down:
	@echo "Stopping Docker services..."
	@docker compose down

db-setup: docker-up
	@echo "Waiting 20s for SQL Server to accept connections..."
	@sleep 20
	@$(MAKE) ef-update-db
	@echo "Database ready."

docker-logs:
	docker compose logs -f

# ============================================================================
# UTILITY
# ============================================================================

clean-test-results:
	@rm -rf TestResults/
	@echo "Cleaned test results"

.PHONY: help restore build rebuild clean run-api run-web run-worker \
        test test-all test-sync test-webhook test-worker test-endpoints test-integration test-unit \
        test-watch test-verbose test-quick test-coverage \
        ef-migrations ef-add-mig ef-update-db \
        docker-up docker-down docker-logs db-setup clean-test-results


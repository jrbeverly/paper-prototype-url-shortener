.PHONY: setup build test test-integration test-e2e lint format validate \
        terraform-validate terraform-plan clean docs help \
        deploy-redirect \
        perf-hot-path \
        perf-smoke perf-load perf-spike perf-soak perf-stress perf-cp .perf-check-k6 \
        .setup-toolcheck .setup-dotnet .setup-frontend .setup-lambda-tools \
        .build-dotnet .build-frontend .build-redirect-package \
        .test-dotnet-unit .test-frontend \
        .lint-dotnet .lint-frontend .lint-terraform \
        .format-dotnet .format-frontend .format-terraform \
        .validate-terraform \
        .docs-terraform .docs-openapi

.DEFAULT_GOAL := help

# ── Paths ───────────────────────────────────────────────────────────────────
SRC_DIR        := src
APP_DIR        := app
TEST_DIR       := test
ENV_DIR        := env
MOD_DIR        := modules
REDIRECT_DIR   := src/RedirectService/RedirectService.Api
REDIRECT_PKG   := dist/redirect-service.zip
LOAD_TESTS_DIR := test/LoadTests

# ── help ────────────────────────────────────────────────────────────────────
help:
	@echo "short-io-url-shortener — Makefile targets"
	@echo ""
	@echo "  make setup              Install dependencies and verify tooling"
	@echo "  make build              Compile all services"
	@echo "  make test               Run unit tests"
	@echo "  make test-integration   Run integration tests"
	@echo "  make test-e2e           Run end-to-end tests"
	@echo "  make lint               Run all linters"
	@echo "  make format             Auto-format all code"
	@echo "  make validate           Run all checks (lint + terraform + build)"
	@echo "  make terraform-validate Validate Terraform configurations"
	@echo "  make terraform-plan     Plan Terraform changes (ENV=staging)"
	@echo "  make deploy-redirect    Deploy the redirect Lambda (FUNCTION_NAME=name)"
	@echo "  make clean              Remove build artifacts"
	@echo "  make docs               Generate documentation"
	@echo ""
	@echo "Hot-path performance tests (requires Docker for LocalStack):"
	@echo "  make perf-hot-path      DynamoDB + warm-invocation latency benchmarks"
	@echo "                          PERF_DYNAMO_P99_MS=N  override DynamoDB p99 threshold (default 200ms)"
	@echo "                          PERF_WARM_P99_MS=N    override warm-invocation p99 threshold (default 500ms)"
	@echo ""
	@echo "Performance / Load Tests (requires k6):"
	@echo "  make perf-smoke         CI smoke test (~90 s, connectivity only)"
	@echo "  make perf-load          Sustained redirect throughput (10 min, TARGET_RPS=N)"
	@echo "  make perf-spike         5× traffic spike test (~12 min)"
	@echo "  make perf-soak          1-hour soak test (~70 min, detects resource leaks)"
	@echo "  make perf-stress        Ramp to breaking point (~20 min)"
	@echo "  make perf-cp            Control-plane concurrent CRUD (10 min)"

# ── setup ───────────────────────────────────────────────────────────────────
setup: .setup-toolcheck .setup-dotnet .setup-frontend .setup-lambda-tools
	@echo "==> Setup complete"

.setup-toolcheck:
	@echo "==> Verifying development environment..."
	@bash scripts/verify-dev-env.sh
	@echo ""
	@echo "==> Checking tflint..."
	@command -v tflint >/dev/null 2>&1 || { echo "ERROR: tflint is required but not installed" >&2; exit 1; }
	@echo "  tflint:    $$(tflint --version 2>&1 | head -1)"
	@echo "==> Initializing tflint plugins..."
	@cd $(ENV_DIR) && tflint --init --recursive || { echo "ERROR: tflint --init failed" >&2; exit 1; }

.setup-dotnet:
	@if ls $(SRC_DIR)/*/*.sln >/dev/null 2>&1; then \
		echo "==> Restoring .NET solutions..."; \
		for sln in $(SRC_DIR)/*/*.sln; do \
			echo "  Restoring $$sln..."; \
			dotnet restore "$$sln" || { echo "ERROR: dotnet restore failed for $$sln" >&2; exit 1; }; \
		done; \
	else \
		echo "==> No .NET solutions found (skipping restore)"; \
	fi

.setup-frontend:
	@if ls $(APP_DIR)/*/package.json >/dev/null 2>&1; then \
		echo "==> Installing frontend dependencies..."; \
		for pkg in $(APP_DIR)/*/package.json; do \
			dir=$$(dirname "$$pkg"); \
			echo "  Installing in $$dir..."; \
			cd "$$dir" && (npm ci 2>/dev/null || npm install) || { echo "ERROR: npm install failed in $$dir" >&2; exit 1; }; \
		done; \
	else \
		echo "==> No frontend projects found (skipping npm install)"; \
	fi

.setup-lambda-tools:
	@echo "==> Restoring .NET local tools (amazon.lambda.tools)..."
	@dotnet tool restore || { echo "ERROR: dotnet tool restore failed" >&2; exit 1; }

# ── build ───────────────────────────────────────────────────────────────────
build: .build-dotnet .build-frontend .build-redirect-package
	@if ls $(SRC_DIR)/*/*.sln >/dev/null 2>&1 || ls $(APP_DIR)/*/package.json >/dev/null 2>&1; then \
		echo "==> Build complete"; \
	else \
		echo "==> No services to build (bootstrap phase)"; \
	fi

.build-dotnet:
	@if ls $(SRC_DIR)/*/*.sln >/dev/null 2>&1; then \
		echo "==> Building .NET services..."; \
		for sln in $(SRC_DIR)/*/*.sln; do \
			echo "  Building $$sln..."; \
			dotnet build "$$sln" --configuration Release || { echo "ERROR: Build failed for $$sln" >&2; exit 1; }; \
		done; \
	fi

.build-frontend:
	@if ls $(APP_DIR)/*/package.json >/dev/null 2>&1; then \
		echo "==> Building frontend apps..."; \
		for pkg in $(APP_DIR)/*/package.json; do \
			dir=$$(dirname "$$pkg"); \
			echo "  Building $$dir..."; \
			cd "$$dir" && npm run build || { echo "ERROR: Build failed for $$dir" >&2; exit 1; }; \
		done; \
	fi

.build-redirect-package:
	@echo "==> Packaging RedirectService Lambda (ReadyToRun, arm64)..."
	@mkdir -p dist
	@cd $(REDIRECT_DIR) && dotnet lambda package \
		--configuration Release \
		--output-package ../../../$(REDIRECT_PKG) \
		|| { echo "ERROR: Lambda package failed. Run 'make setup' to install dotnet-lambda tools." >&2; exit 1; }
	@printf "  Package: $(REDIRECT_PKG) (%s)\n" "$$(du -h $(REDIRECT_PKG) | cut -f1)"

# ── deploy-redirect ──────────────────────────────────────────────────────────
deploy-redirect:
	@echo "==> Deploying RedirectService Lambda$(if $(FUNCTION_NAME), ($(FUNCTION_NAME)),)..."
	@cd $(REDIRECT_DIR) && dotnet lambda deploy-function \
		--configuration Release \
		$(if $(FUNCTION_NAME),--function-name "$(FUNCTION_NAME)",) \
		|| { echo "ERROR: Lambda deployment failed. Ensure AWS credentials are configured." >&2; exit 1; }
	@echo "==> Deployment complete"

# ── test ────────────────────────────────────────────────────────────────────
test: .test-dotnet-unit .test-frontend
	@if ls $(SRC_DIR)/*/*.sln >/dev/null 2>&1 || ls $(APP_DIR)/*/package.json >/dev/null 2>&1; then \
		echo "==> Unit tests complete"; \
	else \
		echo "==> No test projects found (bootstrap phase)"; \
	fi

.test-dotnet-unit:
	@if ls $(TEST_DIR)/*/*.Tests.csproj >/dev/null 2>&1 || ls $(SRC_DIR)/*/*.Tests/*.csproj >/dev/null 2>&1; then \
		echo "==> Running .NET unit tests..."; \
		for sln in $(SRC_DIR)/*/*.sln; do \
			dotnet test "$$sln" --filter "Category=Unit" || { echo "ERROR: Unit tests failed" >&2; exit 1; }; \
		done; \
	fi

.test-frontend:
	@if ls $(APP_DIR)/*/package.json >/dev/null 2>&1; then \
		echo "==> Running frontend tests..."; \
		for pkg in $(APP_DIR)/*/package.json; do \
			dir=$$(dirname "$$pkg"); \
			if grep -q '"test"' "$$pkg" 2>/dev/null; then \
				cd "$$dir" && npm run test -- --run || { echo "ERROR: Frontend tests failed in $$dir" >&2; exit 1; }; \
			fi; \
		done; \
	fi

# ── test-integration ────────────────────────────────────────────────────────
test-integration:
	@if ls $(SRC_DIR)/*/*.sln >/dev/null 2>&1; then \
		echo "==> Running .NET integration tests..."; \
		for sln in $(SRC_DIR)/*/*.sln; do \
			dotnet test "$$sln" --filter "Category=Integration" || { echo "ERROR: Integration tests failed" >&2; exit 1; }; \
		done; \
	else \
		echo "==> No .NET solutions found (skipping integration tests)"; \
	fi

# ── test-e2e ────────────────────────────────────────────────────────────────
E2E_DIR := test/E2E

test-e2e:
	@echo "==> Installing E2E dependencies..."
	@cd $(E2E_DIR) && npm ci --silent
	@echo "==> Running Playwright E2E tests..."
	@cd $(E2E_DIR) && npx playwright test || { echo "ERROR: E2E tests failed" >&2; exit 1; }
	@echo "==> E2E tests complete"

# ── lint ────────────────────────────────────────────────────────────────────
lint: .lint-terraform .lint-dotnet .lint-frontend
	@echo "==> Lint complete"

.lint-dotnet:
	@if ls $(SRC_DIR)/*/*.sln >/dev/null 2>&1; then \
		echo "==> Linting .NET (dotnet format --verify-no-changes)..."; \
		for sln in $(SRC_DIR)/*/*.sln; do \
			dotnet format "$$sln" --verify-no-changes --verbosity normal || { echo "ERROR: .NET format check failed for $$sln" >&2; exit 1; }; \
		done; \
	else \
		echo "==> No .NET solutions found (skipping .NET lint)"; \
	fi

.lint-frontend:
	@if ls $(APP_DIR)/*/package.json >/dev/null 2>&1; then \
		echo "==> Linting frontend (ESLint)..."; \
		for pkg in $(APP_DIR)/*/package.json; do \
			dir=$$(dirname "$$pkg"); \
			if grep -q '"lint"' "$$pkg" 2>/dev/null; then \
				cd "$$dir" && npm run lint || { echo "ERROR: ESLint failed in $$dir" >&2; exit 1; }; \
			else \
				echo "  (no lint script in $$dir — skipping)"; \
			fi; \
		done; \
	else \
		echo "==> No frontend projects found (skipping ESLint)"; \
	fi

.lint-terraform:
	@echo "==> Checking Terraform formatting..."
	@cd $(ENV_DIR) && terraform fmt -check -recursive || { echo "ERROR: Terraform files need formatting. Run 'make format'." >&2; exit 1; }
	@if ls $(MOD_DIR)/*.tf >/dev/null 2>&1 || ls $(MOD_DIR)/*/*.tf >/dev/null 2>&1; then \
		cd $(MOD_DIR) && terraform fmt -check -recursive || { echo "ERROR: Module Terraform files need formatting." >&2; exit 1; }; \
	fi
	@echo "==> Running tflint..."
	@cd $(ENV_DIR) && tflint --recursive || { echo "ERROR: tflint found issues" >&2; exit 1; }

# ── format ──────────────────────────────────────────────────────────────────
format: .format-terraform .format-dotnet .format-frontend
	@echo "==> Formatting complete"

.format-dotnet:
	@if ls $(SRC_DIR)/*/*.sln >/dev/null 2>&1; then \
		echo "==> Formatting .NET..."; \
		for sln in $(SRC_DIR)/*/*.sln; do \
			dotnet format "$$sln" --verbosity normal || { echo "ERROR: dotnet format failed for $$sln" >&2; exit 1; }; \
		done; \
	else \
		echo "==> No .NET solutions found (skipping dotnet format)"; \
	fi

.format-frontend:
	@if ls $(APP_DIR)/*/package.json >/dev/null 2>&1; then \
		echo "==> Formatting frontend (Prettier)..."; \
		for pkg in $(APP_DIR)/*/package.json; do \
			dir=$$(dirname "$$pkg"); \
			if grep -q '"format"' "$$pkg" 2>/dev/null; then \
				cd "$$dir" && npm run format || { echo "ERROR: Format failed in $$dir" >&2; exit 1; }; \
			else \
				echo "  (no format script in $$dir — skipping)"; \
			fi; \
		done; \
	else \
		echo "==> No frontend projects found (skipping Prettier)"; \
	fi

.format-terraform:
	@echo "==> Formatting Terraform..."
	@cd $(ENV_DIR) && terraform fmt -recursive
	@if ls $(MOD_DIR)/*.tf >/dev/null 2>&1 || ls $(MOD_DIR)/*/*.tf >/dev/null 2>&1; then \
		cd $(MOD_DIR) && terraform fmt -recursive; \
	fi

# ── terraform-validate ──────────────────────────────────────────────────────
.validate-terraform: .format-terraform
	@echo "==> Validating Terraform configurations..."
	@failed=0; \
	dirs=$$(find $(ENV_DIR) $(MOD_DIR) -name '*.tf' -not -path '*/.terraform/*' -exec dirname {} \; | sort -u 2>/dev/null); \
	if [ -z "$$dirs" ]; then \
		echo "  No Terraform files found"; \
	else \
		for dir in $$dirs; do \
			echo "  Validating $$dir..."; \
			( cd "$$dir" && terraform init -backend=false >/dev/null 2>&1 && terraform validate ) || { \
				echo "ERROR: Terraform validation failed for $$dir" >&2; \
				failed=1; \
			}; \
		done; \
		if [ $$failed -ne 0 ]; then exit 1; fi; \
	fi
	@echo "==> Running tflint..."
	@cd $(ENV_DIR) && tflint --recursive || { echo "ERROR: tflint found issues" >&2; exit 1; }

terraform-validate: .validate-terraform
	@echo "==> Terraform validation complete"

# ── terraform-plan ──────────────────────────────────────────────────────────
terraform-plan:
	@if [ -z "$(ENV)" ]; then \
		echo "ERROR: ENV is required. Usage: make terraform-plan ENV=staging" >&2; \
		exit 1; \
	fi
	@echo "==> Planning Terraform for environment: $(ENV)"
	@found=0; \
	for svc in $$(ls $(ENV_DIR) 2>/dev/null); do \
		if [ -d "$(ENV_DIR)/$$svc/$(ENV)" ] && ls "$(ENV_DIR)/$$svc/$(ENV)"/*.tf >/dev/null 2>&1; then \
			echo "  Planning $$svc/$(ENV)..."; \
			( cd "$(ENV_DIR)/$$svc/$(ENV)" && terraform plan ) || { echo "ERROR: Terraform plan failed for $$svc/$(ENV)" >&2; exit 1; }; \
			found=1; \
		fi; \
	done; \
	if [ $$found -eq 0 ]; then \
		echo "ERROR: No Terraform configuration found for environment '$(ENV)' in $(ENV_DIR)/*/$(ENV)/" >&2; \
		exit 1; \
	fi

# ── validate ────────────────────────────────────────────────────────────────
validate: lint .validate-terraform build
	@echo "==> Validation complete (lint + terraform-validate + build)"

# ── clean ───────────────────────────────────────────────────────────────────
clean:
	@echo "==> Cleaning build artifacts..."
	@find . -type d \( -name bin -o -name obj -o -name node_modules -o -name dist -o -name .terraform -o -name TestResults \) \
		-not -path '*/node_modules/*' 2>/dev/null | while read d; do \
		rm -rf "$$d"; \
		echo "  Removed $$d"; \
	done
	@find . -type f -name '*.tsbuildinfo' 2>/dev/null | while read f; do \
		rm -f "$$f"; \
		echo "  Removed $$f"; \
	done
	@rm -rf coverage/ 2>/dev/null
	@echo "==> Clean complete"

# ── docs ────────────────────────────────────────────────────────────────────
docs: .docs-openapi
	@echo "==> Documentation generated"


.docs-openapi:
	@echo "==> OpenAPI spec generation..."
	@mkdir -p docs/openapi
	@for sln in $(SRC_DIR)/*/*.sln; do \
		service_dir=$$(dirname "$$sln"); \
		service_name=$$(basename "$$service_dir"); \
		output="docs/openapi/$$service_name.json"; \
		api_dir="$$service_dir/$$service_name.Api"; \
		if [ -d "$$api_dir" ]; then \
			echo "  Building $$service_name to export OpenAPI spec..."; \
			dotnet build "$$sln" --configuration Release --nologo --verbosity quiet || { echo "ERROR: Build failed for $$sln" >&2; exit 1; }; \
			assembly="$$api_dir/bin/Release/net9.0/$$service_name.Api.dll"; \
			if [ -f "$$assembly" ]; then \
				echo "  Exporting OpenAPI spec from $$assembly..."; \
				dotnet swagger tofile --output "$$output" "$$assembly" v1 2>/dev/null || \
					echo "  (dotnet swagger not available — run the API and curl /swagger/v1/swagger.json to $$output)"; \
			fi; \
		fi; \
	done
	@if ls app/*/package.json >/dev/null 2>&1; then \
		echo "==> Regenerating TypeScript API clients..."; \
		for pkg in app/*/package.json; do \
			dir=$$(dirname "$$pkg"); \
			if grep -q '"generate:api"' "$$pkg" 2>/dev/null; then \
				echo "  Generating in $$dir..."; \
				cd "$$dir" && (npm run generate:api 2>/dev/null || echo "  (npm install needed first — run 'make setup')"); \
			fi; \
		done; \
	fi

# ── perf-hot-path ─────────────────────────────────────────────────────────────
# In-process latency benchmarks against LocalStack DynamoDB (no deployed service needed).
# Run on merge to main; skipped on PRs.  Requires Docker.
#
# Override thresholds via env vars (production values — tighter than LocalStack defaults):
#   PERF_DYNAMO_P99_MS=10   DynamoDB GetItem/UpdateItem p99 < 10ms
#   PERF_WARM_P99_MS=50     Full ResolveAsync warm p99 < 50ms
#   DYNAMODB_ENDPOINT=...   Point at real DynamoDB instead of LocalStack

perf-hot-path:
	@echo "==> Running redirect hot-path performance benchmarks..."
	@if ls $(SRC_DIR)/*/*.sln >/dev/null 2>&1; then \
		for sln in $(SRC_DIR)/*/*.sln; do \
			dotnet test "$$sln" --filter "Category=Performance" \
				$(if $(PERF_DYNAMO_P99_MS),--environment PERF_DYNAMO_P99_MS=$(PERF_DYNAMO_P99_MS),) \
				$(if $(PERF_WARM_P99_MS),--environment PERF_WARM_P99_MS=$(PERF_WARM_P99_MS),) \
				|| { echo "ERROR: Performance tests failed — threshold violated or infrastructure error" >&2; exit 1; }; \
		done; \
		echo "==> Performance benchmarks complete"; \
	else \
		echo "==> No .NET solutions found (skipping performance tests)"; \
	fi

# ── perf ─────────────────────────────────────────────────────────────────────
# All perf targets delegate to run-load-tests.sh which checks for k6 and runs
# from the repo root so handleSummary output paths resolve correctly.
#
# Override TARGET_RPS, REDIRECT_BASE_URL, CONTROL_PLANE_BASE_URL, etc. via
# environment variables.  See test/LoadTests/README.md for full documentation.

.perf-check-k6:
	@command -v k6 >/dev/null 2>&1 || { \
		echo "ERROR: k6 is required for load tests." >&2; \
		echo "  Install: https://grafana.com/docs/k6/latest/set-up/install-k6/" >&2; \
		echo "  macOS:   brew install k6" >&2; \
		exit 1; \
	}

perf-smoke: .perf-check-k6
	@echo "==> Running CI smoke test (~90 s)..."
	@bash $(LOAD_TESTS_DIR)/scripts/run-load-tests.sh smoke

perf-load: .perf-check-k6
	@echo "==> Running redirect load test (10 min, TARGET_RPS=$(or $(TARGET_RPS),500))..."
	@bash $(LOAD_TESTS_DIR)/scripts/run-load-tests.sh load

perf-spike: .perf-check-k6
	@echo "==> Running 5× spike test (~12 min)..."
	@bash $(LOAD_TESTS_DIR)/scripts/run-load-tests.sh spike

perf-soak: .perf-check-k6
	@echo "==> Running soak test (~70 min) — detects resource leaks..."
	@bash $(LOAD_TESTS_DIR)/scripts/run-load-tests.sh soak

perf-stress: .perf-check-k6
	@echo "==> Running stress test (~20 min) — drives system past limits..."
	@bash $(LOAD_TESTS_DIR)/scripts/run-load-tests.sh stress

perf-cp: .perf-check-k6
	@echo "==> Running control-plane concurrent CRUD test (10 min)..."
	@bash $(LOAD_TESTS_DIR)/scripts/run-load-tests.sh control-plane

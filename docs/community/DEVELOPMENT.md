# Development workflow | إعداد بيئة التطوير

## Prerequisites

- .NET 10 SDK (see [global.json](../../backend/global.json))
- Git, Python 3 (for verification scripts), NuGet access
- SQL Server 2022+ **only for hosted API or optional live integration tests**
- An OIDC identity provider when running the authenticated API outside its built-in test harness

## Restore, build and test

```bash
git clone https://github.com/ameenalqershi/wow-erp.git
cd wow-erp/backend
dotnet --info
dotnet restore Ameen.Erp.sln --locked-mode
dotnet build Ameen.Erp.sln -c Release --no-restore
dotnet tests/Ameen.Erp.Tests/bin/Release/net10.0/Ameen.Erp.Tests.dll
```

**Important:** this test project is an executable; `dotnet test` alone is not the test runner. To run the combined verification:

```bash
python3 scripts/verify.py
```

Use `python3 scripts/verify.py --sql` **only** after setting `AMEEN_TEST_SQL_CONNECTION` to a disposable test SQL Server that permits creating/deleting temporary databases. Never use production credentials or tenant data.

## Running the API (advanced)

Read [backend/README.md](../../backend/README.md) in full. Configure `ConnectionStrings__Erp`, `Authentication__Authority`, `Authentication__Audience`, HTTPS, allowed hosts and CORS through environment variables or a secrets manager. The `backend/.env.example` is a **template only**; .NET does not automatically load dotenv values. Database migrations must be applied deliberately with a separate admin identity.

## Before modifying a module

- Check [PROGRESS](../backend/PROGRESS.md), the relevant module contract, and [traceability](../backend/traceability.md).
- Consider API, application, domain, infrastructure, tests, database migration, contracts and documentation together.
- If a workflow is blocked by an unspecified business decision, open an Issue instead of inventing a default.
- Keep test fixtures synthetic and scrub all CI artifacts.

## PR final check

Run the appropriate test layer, record the command and actual result, and tell reviewers which live SQL/HTTPS checks were **not** run. The CI workflow uses disposable SQL and may take longer than local non-SQL tests. A green check is not a production certification.

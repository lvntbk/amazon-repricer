# Amazon Repricer

[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![PostgreSQL](https://img.shields.io/badge/PostgreSQL-16-4169E1?logo=postgresql&logoColor=white)](https://www.postgresql.org/)
[![Docker](https://img.shields.io/badge/Docker-Compose-2496ED?logo=docker&logoColor=white)](https://www.docker.com/)
[![CI](https://github.com/lvntbk/amazon-repricer/actions/workflows/ci.yml/badge.svg)](https://github.com/lvntbk/amazon-repricer/actions/workflows/ci.yml)

Production-oriented Amazon repricing backend built with **.NET 10, ASP.NET Core, PostgreSQL, EF Core and background workers**.

The project focuses on safe repricing execution, concurrency control, authentication, observability and operational recovery.

> Real Amazon seller credentials are not included in this repository. Unattended production use should be preceded by a controlled real-seller pilot and deployment-specific validation.

## Architecture

Amazon Repricer separates synchronous API operations from background repricing workloads.

- **ASP.NET Core API** handles authentication, authorization and operational HTTP workflows.
- **Worker Service** executes periodic repricing and submission-reconciliation workloads.
- **PostgreSQL** stores application state, authentication data and repricing history.
- **Amazon SP-API clients** provide the external pricing and Listings integration boundary.
- **OpenTelemetry** captures API, HTTP, runtime and Worker metrics.
- **PostgreSQL backup service** creates validated custom-format database dumps independently of the application process.

Both the API and Worker use the application persistence layer, while authentication migrations are maintained separately through `AuthDbContext`.

## Core Capabilities

- Rule-based automatic repricing
- Minimum/maximum price boundaries and minimum-profit protection
- Manual approval and automatic execution flows
- Stale-price protection
- Atomic repricing claims and duplicate-execution prevention
- Retry with backoff and jitter
- Amazon throttling handling and circuit-breaker protection
- Persistent Amazon submission tracking
- Background reconciliation of incomplete submission results

## Security

- ASP.NET Core Identity
- JWT access tokens
- Opaque refresh tokens stored as SHA-256 hashes
- Refresh-token rotation, family revocation and replay detection
- Security-stamp based stale access-token invalidation
- Disabled-user validation
- `Admin` and `Operator` roles
- Login lockout protection
- Authentication rate limiting
- Trusted reverse-proxy configuration

Production credentials should be supplied through environment variables, deployment-platform secrets or a dedicated secret manager.

## Health and Observability

The API exposes:

```text
GET /health/live
GET /health/ready
```

`/health/ready` includes PostgreSQL connectivity validation.

API and Worker processes emit structured JSON logs. API requests use correlation IDs for request-level traceability.

OpenTelemetry metrics cover ASP.NET Core requests, outbound HTTP calls, .NET runtime measurements, Worker repricing cycles, cycle duration and repricing execution outcomes.

Custom Worker metrics:

```text
amazon_repricer.worker.cycles
amazon_repricer.worker.cycle.duration
amazon_repricer.repricing.executions
```

OTLP export can be configured through:

```text
OpenTelemetry__OtlpEndpoint
```

## PostgreSQL Backup and Restore

Docker Compose includes a dedicated PostgreSQL backup service.

Default policy:

```text
BACKUP_INTERVAL_SECONDS=86400
BACKUP_RETENTION_DAYS=7
```

Backups use PostgreSQL custom dump format and are validated with `pg_restore --list` before being finalized.

Manual backup:

```bash
./scripts/postgres/backup.sh
```

Backup plus restore verification:

```bash
./scripts/postgres/backup-and-verify.sh
```

Restore test:

```bash
./scripts/postgres/restore-test.sh backups/postgres/<backup-file>.dump
```

The restore test creates a temporary database, restores the dump, verifies both EF Core migration-history tables and removes the temporary database afterward.

> Same-host backups are not sufficient disaster recovery. Production deployments should replicate verified backups to encrypted off-host or object storage with independent retention and access control.

## Database Migrations

The application uses two EF Core contexts:

```text
RepricerDbContext
AuthDbContext
```

with separate migration histories:

```text
__EFMigrationsHistory
__EFMigrationsHistory_Auth
```

Both migration sets must complete successfully before production traffic is enabled.

## CI

GitHub Actions validates:

- Release build
- unit tests
- PostgreSQL integration tests
- secret scanning
- PostgreSQL startup and readiness
- application and authentication migrations
- backup creation
- restore verification

A release should use a commit whose exact SHA has a successful CI run.

## Technology Stack

| Area | Technology |
|---|---|
| Runtime | .NET 10 |
| API | ASP.NET Core Web API |
| Background processing | .NET Worker Service |
| Persistence | Entity Framework Core |
| Database | PostgreSQL 16 |
| Authentication | ASP.NET Core Identity + JWT |
| Metrics | OpenTelemetry |
| Logging | Structured JSON logging |
| Containers | Docker / Docker Compose |
| Testing | xUnit + PostgreSQL integration tests |
| CI | GitHub Actions |

## Production Boundaries

This repository is **production-oriented**, not production-certified.

Before unattended real-seller production automation, the deployment still requires:

- a controlled real-seller pilot
- encrypted off-host backup replication
- centralized monitoring and alert routing
- deployment-platform-specific manifests
- distributed or ingress-level rate limiting for multi-instance deployments
- production host restrictions
- infrastructure-level TLS policy
- periodic disaster-recovery exercises

The current application-level rate limiter is process-local.

The current `AllowedHosts="*"` configuration should be restricted before direct public exposure.

## Project Status

Implemented production-oriented foundations include authentication and authorization, concurrency-safe repricing, structured logging, health checks, OpenTelemetry metrics, automated PostgreSQL backups, restore verification, PostgreSQL integration tests and CI secret scanning.

The next major milestone is a controlled real-seller pilot followed by deployment-platform hardening.

## Disclaimer

This is an independent software project and is not affiliated with, endorsed by or sponsored by Amazon.

Amazon, Selling Partner API and related names may be trademarks of their respective owners.

## Author

**Levent İnce**

- GitHub: [@lvntbk](https://github.com/lvntbk)
- LinkedIn: [levent-ince-091838266](https://www.linkedin.com/in/levent-ince-091838266/)

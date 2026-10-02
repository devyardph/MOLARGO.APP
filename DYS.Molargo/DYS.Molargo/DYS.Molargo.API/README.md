# DYS.Molargo.API

The server API: PostgreSQL behind the same generic repository the device uses.

## Running it

`appsettings.Development.json` carries a localhost connection string and a development
signing key, so a developer runs this with no setup beyond a database.

Both are empty in `appsettings.json`, which applies to every environment including
production, and the API refuses to start with either missing. A connection string with a
fallback is an API that silently talks to the wrong database; a signing key with a fallback
is a key that ships — and the committed development key is additionally refused outside
Development, by value, because the likeliest way it reaches a server is a file copied there.

Anywhere but a developer's machine, supply both from the deployment's secret store or as
`ConnectionStrings__Molargo` and `Jwt__SigningKey`.

A database to point at:
dotnet ef database update

```bash
docker run --name molargo-pg -e POSTGRES_PASSWORD=CHANGEME -e POSTGRES_USER=molargo -e POSTGRES_DB=molargo -p 5432:5432 -d postgres:17
```

Then create the schema and run:

```bash
dotnet ef database update --project DYS.Molargo.API
dotnet run --project DYS.Molargo.API
```

`DYS.Molargo.API.http` has a request for every endpoint, starting with sign-in.

## How the schema gets there

**Migrations, not `EnsureCreated`.** The device gets away with dropping and recreating on a
schema bump because its database is a file holding one practice's own records. A shared
server database holding every subscriber's cannot be recreated by anybody, ever.

The migrations live in this project rather than in `DYS.Molargo.Data`, because Data is
shared with the device — which is on SQLite — and a migration is a script for one
particular engine.

```bash
dotnet ef migrations add WhatChanged --project DYS.Molargo.API
```

## The layering

```
DYS.Molargo.Domain    entities, enums — references nothing
DYS.Molargo.Data      MolargoDbContext, IRepository/EfRepository, tenant, clock, hasher
                      no database provider: the heads add SQLite, this project adds Npgsql
DYS.Molargo.API       endpoints, auth, per-request tenancy
```

This project does **not** reference `DYS.Molargo.Shared`. Shared is a Razor library
carrying Blazor, MvvmCross and a Tailwind build, and its 30 services are written around one
signed-in person per process — the one assumption a web API cannot make. What is genuinely
common is the schema and the repository, which is what Data holds.

## Three things that differ from the app, and why

**Money is `numeric(12,2)`, not text.** SQLite has no decimal type, so the app stores money
as TEXT through a value converter — which costs it ordering and `SUM` in SQL. PostgreSQL
has a real decimal type, so the converter is skipped here. `MolargoDbContext` asks the
provider rather than being told, because a flag set by the caller could disagree with the
connection actually opened, and the symptom would be money read back as a parse failure on
somebody's invoice.

**The tenant is scoped, not singleton.** On a device the clinic is process-wide because the
process serves one person. Here two requests for two clinics are in flight at once. The
tenant context starts at `Guid.Empty` and every query filters on equality with it, so a
request that somehow reached a repository before authentication matches *no* rows — the
safe direction to fail.

**Nothing pushes currency into `MolargoFormat`.** That static is how the app renders every
money figure, and `TenantContext` used to set it on resolve. Its own comment predicted the
problem: "the day a signed-in user's clinic is resolved per request, this line becomes a
cross-tenant leak." That push now lives in `FormattingTenantContext` in the heads, where one
process really does serve one person.

## What is not done

- **Endpoints cover patients, the diary and auth.** The other services are not exposed yet.
  The infrastructure they need — tenancy, identity, paging, the repository — is proven by
  these, so adding one is a file like `PatientEndpoints.cs` rather than new plumbing.
- **Writes are thin.** `POST /patients` goes straight to the repository. The app's own
  services enforce rules this does not — duplicate detection, the audit entry, the
  permission guard. Anything beyond the vertical slice should route through a service, not
  a repository, and that is the next decision to make rather than a gap to paper over.
- **No rate limiting, no refresh tokens, no CORS policy.** Each is a deployment decision
  that depends on who is calling.
- **It has not been run against a live PostgreSQL.** The model is proven to map — the
  initial migration generates 59 tables with money as `numeric` — but no request has been
  served end to end. See the note in the handover.

# Testing the API

Four steps from nothing to a signed-in request. The whole thing takes about five minutes,
and the only part that is not scripted is getting a PostgreSQL to point at.

## 1. A database

Any PostgreSQL 14 or later, listening on 5432.

### Already have one installed

Check first — a Windows PostgreSQL install runs as a service and is easy to forget:

```powershell
Get-Service -Name "*postgres*"
```

If it is `Running`, you need only the database and the role. One command, from the
`DYS.Molargo.API` folder:

```powershell
& "C:\Program Files\PostgreSQL\18\bin\psql.exe" -U postgres -v pw=devpassword -f local-postgres-setup.sql
```

Adjust `18` to your version, and `pw` to whatever password is in
`appsettings.Development.json` — the two have to match or the API cannot sign in to its own
database. psql then asks for the `postgres` password you set when PostgreSQL was installed;
that one is yours and is in no file here.

The script creates a `molargo` role and a `molargo` database it owns. Re-running it is
harmless, and is also how a changed password is applied — the role is altered if it already
exists.

**Forgotten the postgres password?** It is recoverable without reinstalling — set
`pg_hba.conf` to `trust` for `127.0.0.1`, restart the service, `ALTER USER postgres PASSWORD
'...'`, then set it back to `scram-sha-256`. The file is at
`C:\Program Files\PostgreSQL\18\data\pg_hba.conf`.

### Or install one

```powershell
winget install PostgreSQL.PostgreSQL.18
```

Then run the setup script above.

### Or Docker

```bash
docker run --name molargo-pg -e POSTGRES_PASSWORD=devpassword -e POSTGRES_USER=molargo -e POSTGRES_DB=molargo -p 5432:5432 -d postgres:17
```

This creates the role and database itself, so the setup script is not needed.

### Or hosted

Neon, Supabase and Railway all give a free Postgres and a connection string — fine here,
since none of this is real patient data. Append
`;SSL Mode=Require;Trust Server Certificate=true`, and put the string in user secrets rather
than the committed settings file.

## 2. Configuration

**Nothing to do** if the database above is on localhost with the default credentials —
`appsettings.Development.json` already points there, and carries a development signing key.

That file is committed, so it holds only values worthless outside a developer's own machine.
The signing key in it is refused by the API in any environment but Development, so it cannot
quietly become a production key.

Point it at anything real — a shared dev server, staging — and override instead of editing,
because neither of these is committed:

```bash
cd DYS.Molargo.API
dotnet user-secrets set "ConnectionStrings:Molargo" "Host=...;Database=...;Username=...;Password=..."
```

```bash
# or, per shell
$env:ConnectionStrings__Molargo = "Host=...;Database=...;Username=...;Password=..."
```

Configuration composes in order, so either silently replaces the committed value.

## 3. Schema, then a first user

```bash
dotnet ef database update --project DYS.Molargo.API
dotnet run --project DYS.Molargo.API -- seed --demo
```

### From Visual Studio instead

Both work; neither needs the command line.

**Package Manager Console** (Tools → NuGet Package Manager → Package Manager Console). Set
**Default project** to `DYS.Molargo.API`, then:

```powershell
Update-Database
```

`Add-Migration WhatChanged` creates one. These cmdlets come from
`Microsoft.EntityFrameworkCore.Tools`, which this project references for exactly that
reason — the `dotnet ef` CLI is a separate, global tool and having one does not give you
the other.

**Developer PowerShell** (View → Terminal) takes the `dotnet ef` commands above verbatim.

Either way the connection comes from `DesignTimeContextFactory`, which reads the same
configuration the API does. EF prefers a factory like that over building the application's
host, so it is the single place the tools get a connection string — and it is why
`Update-Database` applies migrations to exactly the database the app would have talked to.

The migration creates 59 tables.

**`--demo` seeds the app's own demo clinic** — the same records the desktop app creates on
its first run, from the same `SampleData`. One seed for both, so a bug reproduces on both or
neither:

```
  clinic code : molargo-dental
  password    : molargo-demo   (the same for everyone)

  sign in as:
    rvance       Dr Vance               Dentist, owner
    cbrennan     Cathy Brennan          Administration
    jellery      Dr Ellery              Dentist
    hito         H. Ito                 Hygienist

  25 patients, 10 appointments, plus invoices, notes, charting,
  prescriptions, stock and lab cases.
```

It seeds once into an empty database and refuses otherwise, because `SampleData` creates the
clinic itself — running it twice would produce a second copy of everything under new ids.

**Without `--demo`** you get an empty clinic instead: a practice, a site, a chair and one
owner, with a generated password printed once. Useful for testing against nothing, and for
adding a second clinic to check the tenant boundary:

```bash
dotnet run --project DYS.Molargo.API -- seed --clinic second-clinic --user owner2
```

That password is generated rather than taken as an argument, because a password passed on a
command line is a password in the shell history — and this one owns a practice. It is stored
only as a hash.

Seeding **refuses to run outside Development**. The connection string is whatever the
environment says, and that could be production.

## 4. Call it

```bash
dotnet run --project DYS.Molargo.API
```

### The interactive client — easiest

<http://localhost:5165/scalar/v1>

Every endpoint grouped by feature, with a request builder and curl/Python/JS snippets. Call
`POST /auth/token` with the seeded credentials, copy the `token` from the response into the
**Bearer Token** box at the top, and every other route is live.

Development only. The shape of an API is not something to publish.

### The .http file — repeatable

`DYS.Molargo.API.http`, from Visual Studio, Rider or the VS Code REST Client. The first
request signs in and every later one reuses the token automatically. It is already set to the
demo clinic; change the credentials at the top if you seeded a different one.

### curl — scriptable

```bash
TOKEN=$(curl -s -X POST http://localhost:5165/auth/token \
  -H "Content-Type: application/json" \
  -d '{"clinicCode":"molargo-dental","username":"rvance","password":"molargo-demo"}' \
  | jq -r .token)

curl -s http://localhost:5165/patients -H "Authorization: Bearer $TOKEN" | jq
curl -s http://localhost:5165/appointments -H "Authorization: Bearer $TOKEN" | jq
curl -s http://localhost:5165/practice -H "Authorization: Bearer $TOKEN" | jq
```

## The app's own surface

The MAUI app runs against this server: every transaction goes through the API, and there is
no local database on the device. It does not use the REST routes below — those are the
documented surface for anyone integrating from outside. It calls the feature services
directly:

```
POST /rpc/{service}/{method}/{arity}
```

One route for all 270-odd service methods. The server dispatches to the same service class
the device used to run locally, so a rule is written once and both sides get it. See
`MolargoRpc` for the catalogue of what is callable and why it is a written-out list rather
than an assembly scan.

Signing in is `POST /auth/app`, which runs the app's own `AuthService` — real lockout
counter, real two-step code — and issues a token from the result. `POST /auth/token` beside
it stays as the integration surface and has its own, simpler check.

### Testing the client, not just the server

A compiling client proves nothing here: the generated proxy, the argument order and the
multipart file path all fail at runtime or not at all. With the server running:

```bash
dotnet run --project DYS.Molargo.SelfTest
```

Its own project, because it is a client — it registers the real client exactly as `MauiProgram` does and drives it — sign in, page and
search patients, read money back as decimals, upload and delete a document, check the
server-side guard, sign out. Development only, and it cleans up the document it writes.

## What to check, beyond "it returns 200"

**Authentication fails closed.** Every route except `POST /auth/token` returns 401 without a
token. Worth confirming after adding any endpoint, because the fallback policy is what makes
a forgotten attribute a locked route rather than an open one:

```bash
curl -s -o /dev/null -w "%{http_code}\n" http://localhost:5165/patients   # expect 401
```

**The clinic boundary holds.** Seed a second clinic and confirm a token for one returns
nothing belonging to the other — this is the single most important behaviour in the system,
and the only one whose failure is silent:

```bash
dotnet run --project DYS.Molargo.API -- seed --clinic second-clinic --user owner2
```

Sign in as each and compare `GET /patients`. The lists must not overlap. A patient id from
one clinic requested with the other's token must return **404**, not 403 — saying "forbidden"
would confirm the record exists.

**Money survives the round trip.** `GET /billing/invoices` on a clinic with invoices: the
totals must come back with cents intact. This is the one thing the PostgreSQL switch changed
— money is `numeric(12,2)` here and TEXT on the device's SQLite.

**Dates do not throw.** Npgsql maps `DateTime` to `timestamptz` and throws on
`DateTimeKind.Unspecified`. Every write path traced sets `Kind=Utc`, but that is 238 datetime
columns proven by reading rather than by running. A `POST /patients` followed by
`GET /appointments` exercises the common ones.

## Automated tests

There are none yet. The natural shape is `Microsoft.AspNetCore.Mvc.Testing` with
`WebApplicationFactory` against a throwaway database — `Testcontainers.PostgreSql` gives a
real Postgres per test run, which matters here because the provider difference is the whole
point and an in-memory provider would prove nothing about it.

That is a decision worth making before the endpoint count grows much further: 47 routes is
already more than anybody will re-check by hand after every change.

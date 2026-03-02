# Marten `ConfigureMarten` Bug Reproduction

Minimal reproduction showing that Marten's `ConfigureMarten` static method convention is **not
discovered** for document types used by `MultiStreamProjection`, while it **works correctly** for
snapshot projections registered via `Projections.Snapshot<T>()`.

## The Bug

When a document type defines a `static void ConfigureMarten(DocumentMapping<T> mapping)` method
(e.g. to add indexes), Marten discovers and invokes it during the `DocumentMapping<T>` constructor.
However, `Projections.Add<TProjection>()` (used for `MultiStreamProjection`) does not call
`Schema.For<TDoc>()` for the view document type, so the `DocumentMapping<T>` constructor is never
invoked and `ConfigureMarten` is never discovered.

In contrast, `Projections.Snapshot<T>()` does call `Schema.For<T>()`, so `ConfigureMarten` works
as expected for snapshot projections.

## Prerequisites

- .NET 9.0 SDK
- Docker (for Testcontainers PostgreSQL)

## Running

```bash
dotnet test
```

Expected output: **1 pass, 2 fail**.

| Test | Result | Role |
|------|--------|------|
| `SnapshotView_ConfigureMarten_CreatesIndex` | Pass | Control - proves the convention works when `Snapshot<T>()` is used |
| `MultiStreamView_ConfigureMarten_IsDiscovered` | Fail | Root cause - `ConfigureMarten` is never called |
| `MultiStreamView_ConfigureMarten_CreatesIndex` | Fail | Consequence - the index it configures is never created |

## Project Structure

```
src/
  Events.cs              - Two simple event records
  SnapshotView.cs        - Snapshot projection view with ConfigureMarten (control - works)
  MultiStreamView.cs     - MultiStreamProjection view + projection with ConfigureMarten (bug)
tests/
  ProjectionIndexTests.cs - Tests verifying indexes exist in pg_indexes
```

## Root Cause

`Projections.Snapshot<T>()` internally calls `Schema.For<T>()`, which creates the
`DocumentMapping<T>` and runs the static `ConfigureMarten` convention.

`Projections.Add<TProjection>()` (used for `MultiStreamProjection<TDoc, TId>`) registers the
projection but never calls `Schema.For<TDoc>()` for the view document type. The generic
`DocumentMapping<T>` constructor is never invoked, so `ConfigureMarten` is never discovered.

## Marten Version

Tested with Marten **8.22.2** on .NET 9.0.

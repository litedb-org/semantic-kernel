# LiteDB Remaining Work Comparison

This document compares pull requests **#4 (`codex/implement-remaining-pr-tasks`)** and **#5 (`codex/implement-remaining-pr-tasks-dxtw5d`)** against the remaining-work plan for the LiteDB connector.

## 1. Filter translator breadth (Phase 3)

### PR #4
* `LiteDbFilterTranslator` adds explicit handling for `Contains`, `StartsWith`, `EndsWith`, and enumerable membership while guarding against invalid casts and unsupported overloads. The translator emits LiteDB-friendly predicates (`ANY`, `IN`, and `LIKE`) and enforces parameterization.【F:dotnet/src/VectorData/LiteDb/LiteDbFilterTranslator.cs†L102-L248】
* Dedicated unit tests cover the new operators and validate error messages for unsupported overloads.【F:dotnet/test/VectorData/LiteDb.UnitTests/LiteDbFilterTranslatorTests.cs†L15-L52】

### PR #5
* Translation of `Tags.Contains` reverses the predicate to `(@0 IN $.Tags)` instead of using LiteDB's `ANY` operator, matching the README example below. This still works but is less idiomatic and makes intent harder to read compared to PR #4.
  ```csharp
  // PR #5 translator (excerpt)
  return $"({placeholder} IN $.{property.StorageName})";
  ```
  ```markdown
  | `hotel => hotel.Tags.Contains("spa")` | `(@0 IN $.Tags)` |
  ```
* No translator-specific unit tests were added, so regressions in operator coverage would only surface through higher-level tests.

## 2. Transactional batch upserts (Phase 4)

### PR #4
* Multi-record upserts run inside a transaction, and single-record upserts skip the extra overhead.【F:dotnet/src/VectorData/LiteDb/LiteDbCollection.cs†L170-L203】
* The collection validates vector dimensions before queuing documents, catching schema issues prior to persistence.【F:dotnet/src/VectorData/LiteDb/LiteDbCollection.cs†L170-L176】【F:dotnet/src/VectorData/LiteDb/LiteDbCollection.cs†L383-L400】
* A unit test forces an index violation mid-batch and asserts that the collection remains empty, confirming atomicity.【F:dotnet/test/VectorData/LiteDb.UnitTests/LiteDbVectorStoreTests.cs†L389-L503】

### PR #5
* Batch upserts also run inside a transaction, but there is no write-time dimension validation—the mapper output is queued directly without checking collection-level overrides.【F:analysis/pr-comparison.md†L29-L36】

## 3. Write-time dimension checks & metric precedence (Phase 5)

### PR #4
* `ValidateVectorDimensions` honors per-collection overrides (`VectorDimensions`) and per-property metadata when vetting documents.【F:dotnet/src/VectorData/LiteDb/LiteDbCollection.cs†L383-L400】
* A targeted unit test writes mismatched vectors and verifies that an informative exception is raised, followed by tests proving property > collection > store metric precedence.【F:dotnet/test/VectorData/LiteDb.UnitTests/LiteDbVectorStoreTests.cs†L484-L574】

### PR #5
* Dimension checks rely solely on attribute metadata in the mapper, so a collection-level override is ignored. Metric precedence is validated via reflection-heavy tests, but the coverage matches PR #4.

## 4. Embedding generator ergonomics (Phase 6)

### PR #4
* The batch embedding pipeline respects per-property overrides and propagates cancellation tokens through generation, confirmed by unit tests for overrides and cancellation on both upsert and search paths.【F:dotnet/test/VectorData/LiteDb.UnitTests/LiteDbVectorStoreTests.cs†L576-L669】

### PR #5
* Similar coverage exists, but without the diagnostic assertions that the override generator ran (no tracking of batch inputs/output vectors).

## 5. Options polish & DI ergonomics (Phase 2.2 hardening)

### PR #4
* `LiteDbVectorStoreOptions` exposes the `AutoCreateVectorIndexes` alias while keeping both flags in sync, and tests verify alias behaviour alongside connection precedence (factory > database > connection string).【F:dotnet/src/VectorData/LiteDb/LiteDbVectorStoreOptions.cs†L13-L82】【F:dotnet/test/VectorData/LiteDb.UnitTests/LiteDbVectorStoreTests.cs†L704-L755】

### PR #5
* Connection precedence tests exist, but no alias regression test was added and collection-level index provisioning consults only the nullable alias.

## 6. Docs & runnable samples (Phase 7)

### PR #4
* The connector README documents filter mappings (including `ANY` and `IN`), transaction semantics, performance guidance, and DI usage.【F:dotnet/src/VectorData/LiteDb/README.md†L1-L145】
* `Step5_LiteDb_AdvancedScenario` demonstrates DI wiring, per-property metrics/generators, transactional batch upserts, and the new filter operators in a single runnable sample, with the README highlighting the scenario.【F:dotnet/samples/GettingStartedWithVectorStores/Step5_LiteDb_AdvancedScenario.cs†L25-L133】【F:dotnet/samples/GettingStartedWithVectorStores/README.md†L7-L33】

### PR #5
* The sample exercises similar concepts but operates on manually constructed services rather than DI and omits collection prefix/metric precedence coverage. Documentation mirrors the less idiomatic filter form.

## 7. Test matrix additions (cross-cutting)

### PR #4
* Added tests cover translator breadth, nested filters, transactional rollback, vector-dimension failures, metric precedence, generator overrides, cancellation, options precedence, and alias synchronisation.【F:dotnet/test/VectorData/LiteDb.UnitTests/LiteDbFilterTranslatorTests.cs†L15-L52】【F:dotnet/test/VectorData/LiteDb.UnitTests/LiteDbVectorStoreTests.cs†L389-L755】

### PR #5
* Many higher-level tests exist, but translator coverage and alias tests are absent, leaving gaps for regression detection.

## Verdict

PR #4 more completely satisfies the remaining work plan. It introduces stricter filter translation, richer diagnostics, exhaustive unit coverage (including translator and options alias tests), explicit vector-dimension validation that honors collection overrides, and documentation + samples aligned with the plan's expectations. PR #5 covers much of the same surface area but misses several safeguards and tests that PR #4 provides.

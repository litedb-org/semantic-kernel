# LiteDB Vector Store Connector Plan

## Overview
LiteDB v6.0.0-prerelease.0052 introduces a native `BsonVector` type, HNSW-based vector indexes, fluent query helpers, and SQL support for vector similarity. These capabilities enable Semantic Kernel to offer an embedded, single-file vector store with managed code only, addressing locking and space-reclamation issues called out in SqliteVec discussions. This document outlines the plan to deliver a first-class LiteDB-backed connector for the Semantic Kernel Vector Store abstractions.

## Goals
- Provide a production-ready `LiteDbVectorStore` and `LiteDbVectorStoreRecordCollection<TRecord>` implementation aligned with `IVectorStore` contracts.
- Leverage LiteDB's vector indexes for cosine (default), dot-product, and Euclidean similarity search.
- Support CRUD semantics with transactional guarantees so deleted vectors are reclaimed on disk automatically.
- Keep the connector dependency-light (only `LiteDB` 6.0.0-prerelease.63) and friendly to desktop, mobile, and edge deployments.
- Offer parity with existing connectors (SqliteVec, Azure Cosmos DB, etc.) including filtering, metadata storage, and asynchronous APIs.

## Non-Goals
- Implement embedding generation or orchestration; responsibility remains with kernel skills.
- Replace existing connectors; LiteDB is an additional option focused on managed, embedded scenarios.
- Depend on LiteDB preview features outside of vector search functionality.

## Key Requirements
1. **Package management**: Add `LiteDB` 6.0.0-prerelease.63 NuGet dependency to a new `Microsoft.SemanticKernel.Connectors.LiteDb` project targeting `netstandard2.1` and `net8.0` to match other connectors.
2. **Data model alignment**: Map SK records to LiteDB collections using a BSON document containing:
   - Primary key (`_id`) with developer-provided string ID.
   - Embedding stored as `BsonVector` via `float[]` serialization.
   - Optional metadata payload stored as `BsonDocument` (dictionary of primitives/arrays).
   - Additional fields required by `VectorStoreRecordDefinition` (timestamp, tags, references).
3. **Indexing**: Ensure collection creation calls `EnsureIndex` with `VectorIndexOptions` specifying embedding dimensionality and distance metric (default cosine). Provide configuration for alternative metrics.
4. **Search API**: Implement `TopNK` searches through `ILiteQueryable<T>` `TopKNear` extension. Support optional filter predicates translated from SK filter expressions into LiteDB `Query` objects.
5. **Filtering strategy**: Initially support equality, comparison, and logical operators on scalar metadata fields. Document limitations for complex nested filters and plan incremental expansion.
6. **Transactions and concurrency**: Use LiteDB's default transaction-per-operation semantics with `BeginTrans` when batching writes. Document concurrency model (single-writer, multiple-reader) and guidance for async usage.
7. **Serialization helpers**: Provide converters between SK `VectorStoreRecordData`/`VectorStoreRecordDefinition` and LiteDB types, ensuring deterministic field naming.
8. **Configuration surface**: Expose options object for file path, connection string, collection name prefix, vector dimension, distance metric, and automatic index creation flag.
9. **Testing**: Add unit tests covering CRUD, similarity search accuracy, filter application, and transactional deletes. Include integration tests running against in-memory (temporary file) LiteDB database.
10. **Documentation & samples**: Author README section demonstrating setup, ingestion, query, and deletion workflows along with guidance on embedding dimension management.

## Design Considerations
- **Connector placement**: Mirror existing connector layout under `dotnet/src/Connectors/VectorStores`. Introduce solution/project wiring and assembly metadata consistent with other connectors.
- **Dependency versioning**: Pin to `LiteDB` 6.0.0-prerelease.63; monitor release channel for API changes between prerelease builds.
- **Vector dimension management**: Require caller to specify embedding size when creating a collection; store this in a metadata document to enforce consistency on subsequent operations.
- **Distance metrics**: Provide options for `Cosine`, `DotProduct`, and `Euclidean`. Default to cosine per LiteDB release guidance and SK conventions. Validate metric compatibility during search operations.
- **Filter translation**: Reuse existing filter AST utilities where possible; otherwise implement LiteDB-specific translator with clear error messages for unsupported operators.
- **Async pattern**: LiteDB APIs are synchronous. Wrap operations in `Task.Run` only when necessary, but prefer synchronous methods exposed via `ValueTask` to avoid unnecessary thread pool usage. Document expectation for caller to run on background thread when high throughput is required.
- **Resource lifecycle**: Offer factory methods accepting `LiteDatabase` instance or connection string. Manage disposal carefully to avoid double-closing shared databases.
- **Schema evolution**: Include version marker in a dedicated metadata collection for future migrations.

## Work Breakdown Structure
1. **Project Setup**
   - Create connector project, add package reference, update solution files, and configure analyzers.
2. **Core Entities**
   - Define record schema classes, configuration options, and internal helpers for BSON conversion.
3. **Collection Implementation**
   - Implement `LiteDbVectorStoreRecordCollection<TRecord>` covering CRUD, search, upsert, delete, and iterator APIs.
4. **Store Wrapper**
   - Implement `LiteDbVectorStore` to create and manage collections with dimension validation and caching.
5. **Filter Translation Layer**
   - Translate SK filters into LiteDB queries; include coverage for supported operators.
6. **Testing**
   - Unit and integration tests plus concurrency and delete-reclamation validation (file size assertions via temporary database file growth/shrink behavior).
7. **Documentation & Samples**
   - Update docs, add sample console app demonstrating ingestion and similarity search.
8. **CI Integration**
   - Ensure tests run in existing pipelines, add necessary LiteDB temporary file cleanup, and document prerequisites.

## Risks & Mitigations
- **Prerelease Dependency Instability**: LiteDB vector APIs may change before GA. Mitigate by isolating usage behind adapters and tracking release updates.
- **Sync API Throughput**: Because LiteDB lacks async APIs, high-concurrency workloads may face contention. Mitigate by batching operations, documenting best practices, and considering background ingestion pipelines.
- **Filter Parity**: Achieving full filter compatibility may require iterative development. Start with core operators and capture backlog items for advanced scenarios.
- **File Locking on Multiple Processes**: LiteDB is single-process. Document this limitation and provide detection to throw informative errors if connection fails.

## Open Questions
- Should the connector ship with optional encryption support leveraging LiteDB's password-protected files?
- Do we need migration tooling if LiteDB modifies index metadata between prereleases?
- Is there appetite for a cross-platform sample showing offline RAG using LiteDB for distribution with SK demos?

## References
- LiteDB v6.0 prerelease #52 release notes highlighting vector search, HNSW index, and SQL support.
- Semantic Kernel issue #13224 proposing LiteDB connector and motivating advantages (managed dependency, reliable deletes, HNSW performance).

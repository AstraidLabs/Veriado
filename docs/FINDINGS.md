# SQLite-only Audit Findings

## Summary of high-priority findings
- Transaction/Connection mismatches: 3 (vysoký)
- Other categories: 0 (žádné nálezy)

## Packages to remove
- **Žádné přebytečné balíčky.** Audit neodhalil žádné odkazy na SQL Server, PostgreSQL, MySQL ani jiné poskytovatele.
- Testovací projekty již používají `Microsoft.Data.Sqlite` 9.0.9 stejně jako produkční kód – není potřeba žádná akce.【F:Veriado.Application.Tests/Veriado.Application.Tests.csproj†L1-L20】

## Code references to non-SQLite providers
- Žádné výskyty `SqlServer`, `Npgsql`, `MySql`, `SqlClient` ani podobných identifikátorů nebyly nalezeny. Repo je vyčištěno od alternativních providerů.

## Raw SQL incompatibilities
- Žádné nekompatibilní SQL konstrukce (IDENTITY, NVARCHAR(MAX), FILTER WHERE, …) nebyly detekovány v migracích ani ve volném SQL.

## Transaction/Connection mismatches
1. **Služby historie a oblíbených obcházejí pool připojení.** `SearchHistoryService` a `SearchFavoritesService` si vytvářejí nové `SqliteConnection` přímo z connection stringu a samy aplikují PRAGMA nastavení.【F:Veriado.Infrastructure/Search/SearchHistoryService.cs†L17-L118】【F:Veriado.Infrastructure/Search/SearchFavoritesService.cs†L17-L189】  
   - Problém: Refaktor zavedl `ISqliteConnectionFactory`, aby všechny pomocné služby sdílely pool, jednotné PRAGMA hooky a monitorování. Obě služby zůstaly mimo tuto cestu, takže nevyužívají pooling ani telemetry.
   - Návrh řešení: Injektovat `ISqliteConnectionFactory` (podobně jako `SqliteFts5QueryService`) a odstranit privátní metodu `CreateConnection`.
   - Dopad: Vysoký – mimo pool vznikají další připojení a hrozí nekonzistentní PRAGMA konfigurace.
2. **Maintenance služba pro návrhy ignoruje connection factory.** `SuggestionMaintenanceService` opakuje vlastní logiku pro vytváření připojení i transakcí, přestože běží v rámci indexační pipeline, která jinak používá sdílený pool.【F:Veriado.Infrastructure/Search/SuggestionMaintenanceService.cs†L24-L74】  
   - Problém: Duplicitní konfigurace PRAGMA příkazů a chybějící pooling odporuje cíli „jednoho“ kanálu k SQLite.
   - Návrh řešení: Připojit `ISqliteConnectionFactory` přes konstruktor, používat `CreateConnectionAsync` a předávat transakce bez ručního castu.
   - Dopad: Vysoký – hrozí rozdílné nastavení journal mode/cache vůči zbytku systému.
3. **Casty na `SqliteTransaction` přetrvávají ve write a FTS službách.** `WriteWorker`, `SqliteFts5Indexer` a `FtsWriteAheadService` volají `BeginTransactionAsync` a následně výsledek přetypovávají z `DbTransaction` na `SqliteTransaction`.【F:Veriado.Infrastructure/Concurrency/WriteWorker.cs†L223-L295】【F:Veriado.Infrastructure/Search/SqliteFts5Indexer.cs†L65-L125】【F:Veriado.Infrastructure/Search/FtsWriteAheadService.cs†L268-L420】  
   - Problém: Refaktor měl odstranit runtime casty a spoléhat na kompilátor, že hlídá použití `SqliteTransaction`. Tyto metody stále používají starý pattern.
   - Návrh řešení: Deklarovat `await using SqliteTransaction sqliteTransaction = await connection.BeginTransactionAsync(...)` (metoda vrací konkrétní typ) a odstranit explicitní casty.
   - Dopad: Vysoký – casty zamlčují regresní chyby při budoucích úpravách signatur.

## FTS5 inconsistencies
- Žádné konfliktní odkazy na jiné vyhledávací backendy. FTS logika využívá výhradně SQLite FTS5 (`file_search` plus mapovací tabulky a DLQ) a běží v jediné transakci.

## DI/Startup multi-provider branches
- Registrace v `ServiceCollectionExtensions` používají pouze `UseSqlite`. Při inicializaci se navíc kontroluje `providerName.Contains("Sqlite")`, což společně s guardy v `AppDbContext` rychle zachytí chybný provider.

## Config/Docs mismatches
- Dokumentace a README popisují pouze SQLite/FTS5. Nebyly nalezeny žádné odkazy na jiné databáze ani postupy.

## Risky areas & False positives
- Nebyly nalezeny žádné vazby na jiné než FTS5 mechanizmy. Současná implementace dotazovací služby využívá výhradně BM25 z FTS5.

## Top 10 patch proposals (high impact)
```diff
diff --git a/Veriado.Infrastructure/Search/SearchHistoryService.cs b/Veriado.Infrastructure/Search/SearchHistoryService.cs
--- a/Veriado.Infrastructure/Search/SearchHistoryService.cs
+++ b/Veriado.Infrastructure/Search/SearchHistoryService.cs
@@
-    private readonly InfrastructureOptions _options;
-    private readonly IClock _clock;
+    private readonly ISqliteConnectionFactory _connectionFactory;
+    private readonly IClock _clock;
@@
-    public SearchHistoryService(InfrastructureOptions options, IClock clock)
-    {
-        _options = options;
-        _clock = clock;
-    }
+    public SearchHistoryService(ISqliteConnectionFactory connectionFactory, IClock clock)
+    {
+        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
+        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
+    }
@@
-        await using var connection = CreateConnection();
+        await using var lease = await _connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
+        var connection = lease.Connection;
@@
-        await using var sqliteTransaction = (SqliteTransaction)await connection
-            .BeginTransactionAsync(cancellationToken)
-            .ConfigureAwait(false);
+        await using var sqliteTransaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
@@
-        await using var connection = CreateConnection();
+        await using var lease = await _connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
+        var connection = lease.Connection;
@@
-        await using var connection = CreateConnection();
+        await using var lease = await _connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
+        var connection = lease.Connection;
@@
-    private SqliteConnection CreateConnection()
-    {
-        if (string.IsNullOrWhiteSpace(_options.ConnectionString))
-        {
-            throw new InvalidOperationException("Infrastructure has not been initialised with a connection string.");
-        }
-
-        return new SqliteConnection(_options.ConnectionString);
-    }
+```
```diff
diff --git a/Veriado.Infrastructure/Search/SearchFavoritesService.cs b/Veriado.Infrastructure/Search/SearchFavoritesService.cs
--- a/Veriado.Infrastructure/Search/SearchFavoritesService.cs
+++ b/Veriado.Infrastructure/Search/SearchFavoritesService.cs
@@
-    private readonly InfrastructureOptions _options;
-    private readonly IClock _clock;
+    private readonly ISqliteConnectionFactory _connectionFactory;
+    private readonly IClock _clock;
@@
-    public SearchFavoritesService(InfrastructureOptions options, IClock clock)
-    {
-        _options = options;
-        _clock = clock;
-    }
+    public SearchFavoritesService(ISqliteConnectionFactory connectionFactory, IClock clock)
+    {
+        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
+        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
+    }
@@
-        await using var connection = CreateConnection();
+        await using var lease = await _connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
+        var connection = lease.Connection;
@@
-        await using var sqliteTransaction = (SqliteTransaction)await connection
-            .BeginTransactionAsync(cancellationToken)
-            .ConfigureAwait(false);
+        await using var sqliteTransaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
@@
-        await using var connection = CreateConnection();
+        await using var lease = await _connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
+        var connection = lease.Connection;
@@
-        await using var connection = CreateConnection();
+        await using var lease = await _connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
+        var connection = lease.Connection;
@@
-        await using var connection = CreateConnection();
+        await using var lease = await _connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
+        var connection = lease.Connection;
@@
-        await using var connection = CreateConnection();
+        await using var lease = await _connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
+        var connection = lease.Connection;
@@
-    private SqliteConnection CreateConnection()
-    {
-        if (string.IsNullOrWhiteSpace(_options.ConnectionString))
-        {
-            throw new InvalidOperationException("Infrastructure has not been initialised with a connection string.");
-        }
-
-        return new SqliteConnection(_options.ConnectionString);
-    }
+```
```diff
diff --git a/Veriado.Infrastructure/Search/SuggestionMaintenanceService.cs b/Veriado.Infrastructure/Search/SuggestionMaintenanceService.cs
--- a/Veriado.Infrastructure/Search/SuggestionMaintenanceService.cs
+++ b/Veriado.Infrastructure/Search/SuggestionMaintenanceService.cs
@@
-    private readonly InfrastructureOptions _options;
-    private readonly ILogger<SuggestionMaintenanceService> _logger;
+    private readonly ISqliteConnectionFactory _connectionFactory;
+    private readonly ILogger<SuggestionMaintenanceService> _logger;
@@
-    public SuggestionMaintenanceService(InfrastructureOptions options, ILogger<SuggestionMaintenanceService> logger)
-    {
-        _options = options ?? throw new ArgumentNullException(nameof(options));
-        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
-    }
+    public SuggestionMaintenanceService(ISqliteConnectionFactory connectionFactory, ILogger<SuggestionMaintenanceService> logger)
+    {
+        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
+        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
+    }
@@
-        if (string.IsNullOrWhiteSpace(_options.ConnectionString))
-        {
-            return;
-        }
-
-        var harvested = Harvest(document)
+        var harvested = Harvest(document)
@@
-            await using var connection = new SqliteConnection(_options.ConnectionString);
-            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
-            await SqlitePragmaHelper.ApplyAsync(connection, cancellationToken: cancellationToken).ConfigureAwait(false);
+            await using var lease = await _connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
+            var connection = lease.Connection;
+            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
+            await SqlitePragmaHelper.ApplyAsync(connection, cancellationToken: cancellationToken).ConfigureAwait(false);
@@
-            await using var sqliteTransaction = (SqliteTransaction)await connection
-                .BeginTransactionAsync(cancellationToken)
-                .ConfigureAwait(false);
+            await using var sqliteTransaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
```
```diff
diff --git a/Veriado.Infrastructure/Concurrency/WriteWorker.cs b/Veriado.Infrastructure/Concurrency/WriteWorker.cs
--- a/Veriado.Infrastructure/Concurrency/WriteWorker.cs
+++ b/Veriado.Infrastructure/Concurrency/WriteWorker.cs
@@
-        await using var sqliteTransaction = (SqliteTransaction)await sqliteConnection
-            .BeginTransactionAsync(cancellationToken)
-            .ConfigureAwait(false);
+        await using var sqliteTransaction = await sqliteConnection
+            .BeginTransactionAsync(cancellationToken)
+            .ConfigureAwait(false);
```
```diff
diff --git a/Veriado.Infrastructure/Search/SqliteFts5Indexer.cs b/Veriado.Infrastructure/Search/SqliteFts5Indexer.cs
--- a/Veriado.Infrastructure/Search/SqliteFts5Indexer.cs
+++ b/Veriado.Infrastructure/Search/SqliteFts5Indexer.cs
@@
-        await using var sqliteTransaction = (SqliteTransaction)await connection
-            .BeginTransactionAsync(cancellationToken)
-            .ConfigureAwait(false);
+        await using var sqliteTransaction = await connection
+            .BeginTransactionAsync(cancellationToken)
+            .ConfigureAwait(false);
@@
-        await using var sqliteTransaction = (SqliteTransaction)await connection
-            .BeginTransactionAsync(cancellationToken)
-            .ConfigureAwait(false);
+        await using var sqliteTransaction = await connection
+            .BeginTransactionAsync(cancellationToken)
+            .ConfigureAwait(false);
```
```diff
diff --git a/Veriado.Infrastructure/Search/FtsWriteAheadService.cs b/Veriado.Infrastructure/Search/FtsWriteAheadService.cs
--- a/Veriado.Infrastructure/Search/FtsWriteAheadService.cs
+++ b/Veriado.Infrastructure/Search/FtsWriteAheadService.cs
@@
-        await using var sqliteTransaction = (SqliteTransaction)await connection
-            .BeginTransactionAsync(cancellationToken)
-            .ConfigureAwait(false);
+        await using var sqliteTransaction = await connection
+            .BeginTransactionAsync(cancellationToken)
+            .ConfigureAwait(false);
@@
-        await using var sqliteTransaction = (SqliteTransaction)await connection
-            .BeginTransactionAsync(cancellationToken)
-            .ConfigureAwait(false);
+        await using var sqliteTransaction = await connection
+            .BeginTransactionAsync(cancellationToken)
+            .ConfigureAwait(false);
@@
-        await using var sqliteTransaction = (SqliteTransaction)await connection
-            .BeginTransactionAsync(cancellationToken)
-            .ConfigureAwait(false);
+        await using var sqliteTransaction = await connection
+            .BeginTransactionAsync(cancellationToken)
+            .ConfigureAwait(false);
@@
-        await using var sqliteTransaction = (SqliteTransaction)await connection
-            .BeginTransactionAsync(cancellationToken)
-            .ConfigureAwait(false);
+        await using var sqliteTransaction = await connection
+            .BeginTransactionAsync(cancellationToken)
+            .ConfigureAwait(false);
```

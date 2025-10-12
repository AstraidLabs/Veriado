# SQLite-only Audit Findings

## Summary of high-priority findings
- Packages to remove/update: 1 (vysoký)
- Transaction/Connection mismatches: 9 (vysoký)
- Tests/CI/CD issues: 1 (vysoký)
- Other categories: 0 (žádné nálezy)

## Packages to remove
- **Žádné přebytečné balíčky.** Audit neodhalil žádné odkazy na SQL Server, PostgreSQL, MySQL ani jiné poskytovatele. 
- **Nález:** Testovací projekt používá `Microsoft.Data.Sqlite` ve verzi 8.0.6, zatímco zbytek řešení běží na 9.0.9. Mismatch komplikuje ladění FTS funkcí a hrozí rozdílné chování mezi produkcí a testy.【F:Veriado.Application.Tests/Veriado.Application.Tests.csproj†L1-L20】
  - Návrh řešení: Sjednotit `Microsoft.Data.Sqlite` v testech na verzi 9.0.9, aby integrační testy běžely na totožném runtime jako aplikace.
  - Dopad: Vysoký

## Code references to non-SQLite providers
- Žádné výskyty `SqlServer`, `Npgsql`, `MySql`, `SqlClient` ani podobných identifikátorů nebyly nalezeny. Repo je již vyčištěno od alternativních providerů.

## Raw SQL incompatibilities
- Žádné nekompatibilní SQL konstrukce (IDENTITY, NVARCHAR(MAX), FILTER WHERE, atd.) nebyly detekovány v migracích ani ve volném SQL.

## Transaction/Connection mismatches
1. **Aplikační kontrakt akceptuje obecné transakce.** Rozhraní `ISearchIndexCoordinator` stále přijímá `DbTransaction?`, takže na úrovni API lze omylem předat jiného providera a teprve runtime guard situaci zachytí.【F:Veriado.Application/Abstractions/ISearchIndexCoordinator.cs†L1-L18】
   - Návrh řešení: Změnit signaturu na `SqliteTransaction` (nebo `SqliteTransaction?` s kontrolou na `null`) a aktualizovat všechny implementace i volající.
   - Dopad: Vysoký
2. **Implementace indexačního koordinátora očekává runtime cast.** `SqliteSearchIndexCoordinator` si sám kontroluje `DbTransaction` přes `is not SqliteTransaction`. Kompilátor ale stále dovolí zavolat metodu s jiným providerem.【F:Veriado.Infrastructure/Search/SqliteSearchIndexCoordinator.cs†L1-L60】
   - Návrh řešení: Přepnout signaturu na `SqliteTransaction`, přidat `ArgumentNullException.ThrowIfNull(transaction)` a odstranit runtime cast.
   - Dopad: Vysoký
3. **WriteWorker spoléhá na runtime detekci typu transakce.** Batch zpracování vytváří transakci přes `BeginTransactionAsync`, ale proměnná je pojmenována obecně a následně se testuje `is not SqliteTransaction`.【F:Veriado.Infrastructure/Concurrency/WriteWorker.cs†L221-L247】
   - Návrh řešení: Přímo deklarovat `SqliteTransaction sqliteTransaction = await sqliteConnection.BeginTransactionAsync(...)` a dále pracovat s tímto typem; EF `UseTransactionAsync` lze volat se stejnou instancí.
   - Dopad: Vysoký
4. **Samostatný indexer FTS pracuje s nepojmenovaným `DbTransaction`.** `SqliteFts5Indexer` vytváří transakci, ukládá ji do obecné proměnné a ihned přetypovává na `SqliteTransaction`.【F:Veriado.Infrastructure/Search/SqliteFts5Indexer.cs†L56-L124】
   - Návrh řešení: Změnit proměnnou na `await using var sqliteTransaction = ...` a odstranit přetypování; zajistí to přímý contract na Sqlite.
   - Dopad: Vysoký
5. **AppDbContext nehlídá provider v konstruktoru ani při SQL rutinách.** Metody `InitializeAsync`, `EnsureSqliteMigrationsLockClearedAsync`, `NeedsSqliteMigrationsHistoryBaselineAsync` a `EnsureSqliteMigrationsHistoryBaselinedAsync` používají SQLite-specifické SQL, ale při běhu na jiném provideru jen tiše skončí nebo vrátí `false`.【F:Veriado.Infrastructure/Persistence/AppDbContext.cs†L85-L178】
   - Návrh řešení: Přidat fail-fast guard (`EnsureSqliteProvider`) v konstruktoru a volat jej na začátku každé metody; v případě jiného providera vyhodit `InvalidOperationException`. Zároveň při baseline kontrole kástnout `Database.GetDbConnection()` na `SqliteConnection`.
   - Dopad: Vysoký
6. **ReadOnlyDbContext neprovádí kontrolu provideru.** Read-only kontext může být omylem nakonfigurován s jiným providerem, protože konstruktor pouze nastaví tracking, ale neověří, že běží nad SQLite.【F:Veriado.Infrastructure/Persistence/ReadOnlyDbContext.cs†L1-L34】
   - Návrh řešení: V konstruktoru zvalidovat `Database.ProviderName` a při nesouladu vyhodit `InvalidOperationException`.
   - Dopad: Vysoký
7. **SqlitePragmaInterceptor mlčky přeskočí cizí připojení.** Pokud by EF Core dostal jiné připojení, interceptor pouze vrátí kontrolu bez chyby, takže chybné PRAGMA nastavení by se neaplikovalo a problém by se projevil až později.【F:Veriado.Infrastructure/Persistence/Interceptors/SqlitePragmaInterceptor.cs†L1-L25】
   - Návrh řešení: Namísto tichého návratu vyhodit `InvalidOperationException` při jiném typu připojení; testy tím okamžitě odhalí špatného providera.
   - Dopad: Vysoký
8. **FtsWriteAheadService stále přetypovává transakce.** Veškeré DLQ operace zapisují pomocí `(SqliteTransaction)` castů a nevyužívají silnou typovou garanci, což komplikuje refaktoring a skrývá chyby při změně providera.【F:Veriado.Infrastructure/Search/FtsWriteAheadService.cs†L279-L495】
   - Návrh řešení: Deklarovat proměnné transakcí přímo jako `SqliteTransaction` při volání `BeginTransactionAsync` a odstranit casty.
   - Dopad: Vysoký
9. **SearchHistory/SearchFavorites opět přetypovávají transakce.** Lokální transakce ve službách historie a oblíbených dotazů se vracejí jako obecný `DbTransaction` a až při použití se přetypují na `SqliteTransaction`.【F:Veriado.Infrastructure/Search/SearchHistoryService.cs†L16-L75】【F:Veriado.Infrastructure/Search/SearchFavoritesService.cs†L112-L156】
   - Návrh řešení: Ukládat návratovou hodnotu `BeginTransactionAsync` přímo do proměnné `SqliteTransaction` a následně ji předávat příkazům; tím odpadnou ruční casty.
   - Dopad: Vysoký
## FTS5 inconsistencies
- Žádné konfliktní odkazy na jiné vyhledávací backendy. FTS logika využívá výhradně SQLite FTS5 (`file_search` plus mapovací tabulky a DLQ) a běží v jediné transakci.

## DI/Startup multi-provider branches
- Registrace v `ServiceCollectionExtensions` již používají pouze `UseSqlite`. Při inicializaci se navíc kontroluje `providerName.Contains("Sqlite")`; další guard z AppDbContextu výše problém ještě zkrátí.

## Tests/CI/CD issues
- **Veriado.Application.Tests**: verze `Microsoft.Data.Sqlite` 8.0.6 (viz výše) je neslučitelná s produkční konfigurací a může maskovat chyby v FTS. (Návrh + Dopad již uvedeno v části Packages.)
- CI pipeline ani skripty neinstalují jiné databáze – žádné zásahy nejsou potřeba.

## Config/Docs mismatches
- Dokumentace a README popisují pouze SQLite/FTS5. Nebyly nalezeny žádné odkazy na jiné databáze ani postupy.

## Risky areas & False positives
- Nebyly nalezeny žádné vazby na jiné než FTS5 mechanizmy. Současná implementace dotazovací služby využívá výhradně BM25 z FTS5.

## Top 10 patch proposals (high impact)
```diff
diff --git a/Veriado.Application/Abstractions/ISearchIndexCoordinator.cs b/Veriado.Application/Abstractions/ISearchIndexCoordinator.cs
--- a/Veriado.Application/Abstractions/ISearchIndexCoordinator.cs
+++ b/Veriado.Application/Abstractions/ISearchIndexCoordinator.cs
@@
-using System.Data.Common;
+using Microsoft.Data.Sqlite;
@@
-    Task<bool> IndexAsync(FileEntity file, FilePersistenceOptions options, DbTransaction? transaction, CancellationToken cancellationToken);
+    Task<bool> IndexAsync(FileEntity file, FilePersistenceOptions options, SqliteTransaction transaction, CancellationToken cancellationToken);
```
```diff
diff --git a/Veriado.Infrastructure/Search/SqliteSearchIndexCoordinator.cs b/Veriado.Infrastructure/Search/SqliteSearchIndexCoordinator.cs
--- a/Veriado.Infrastructure/Search/SqliteSearchIndexCoordinator.cs
+++ b/Veriado.Infrastructure/Search/SqliteSearchIndexCoordinator.cs
@@
-using System.Data.Common;
@@
-    public async Task<bool> IndexAsync(FileEntity file, FilePersistenceOptions options, DbTransaction? transaction, CancellationToken cancellationToken)
+    public async Task<bool> IndexAsync(FileEntity file, FilePersistenceOptions options, SqliteTransaction transaction, CancellationToken cancellationToken)
     {
         ArgumentNullException.ThrowIfNull(file);
-        if (!_options.IsFulltextAvailable)
+        if (transaction is null)
+        {
+            throw new ArgumentNullException(nameof(transaction));
+        }
+
+        if (!_options.IsFulltextAvailable)
         {
             _logger.LogDebug("Skipping full-text indexing for file {FileId} because FTS5 support is unavailable.", file.Id);
             return false;
         }
-
-        if (transaction is not SqliteTransaction sqliteTransaction)
-        {
-            throw new InvalidOperationException("SQLite transaction is required for full-text indexing operations.");
-        }
-
-        var sqliteConnection = sqliteTransaction.Connection as SqliteConnection
+
+        var sqliteConnection = transaction.Connection as SqliteConnection
             ?? throw new InvalidOperationException("SQLite connection is unavailable for the active transaction.");
@@
-        await helper.IndexAsync(document, sqliteConnection, sqliteTransaction, beforeCommit: null, cancellationToken)
+        await helper.IndexAsync(document, sqliteConnection, transaction, beforeCommit: null, cancellationToken)
             .ConfigureAwait(false);
         return true;
     }
```
```diff
diff --git a/Veriado.Infrastructure/Concurrency/WriteWorker.cs b/Veriado.Infrastructure/Concurrency/WriteWorker.cs
--- a/Veriado.Infrastructure/Concurrency/WriteWorker.cs
+++ b/Veriado.Infrastructure/Concurrency/WriteWorker.cs
@@
-        await using var dbTransaction = await sqliteConnection
-            .BeginTransactionAsync(cancellationToken)
-            .ConfigureAwait(false);
-
-        if (dbTransaction is not SqliteTransaction sqliteTransaction)
-        {
-            throw new InvalidOperationException("SQLite transaction is required for write operations.");
-        }
-
-        await using var transaction = await context.Database
-            .UseTransactionAsync(dbTransaction, cancellationToken)
+        await using var sqliteTransaction = await sqliteConnection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
+
+        await using var efTransaction = await context.Database
+            .UseTransactionAsync(sqliteTransaction, cancellationToken)
             .ConfigureAwait(false);
@@
-            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
+            await efTransaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
@@
-            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
+            await efTransaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
@@
-            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
+            await efTransaction.CommitAsync(cancellationToken).ConfigureAwait(false);
@@
-                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
+                await efTransaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
```
```diff
diff --git a/Veriado.Infrastructure/Search/SqliteFts5Indexer.cs b/Veriado.Infrastructure/Search/SqliteFts5Indexer.cs
--- a/Veriado.Infrastructure/Search/SqliteFts5Indexer.cs
+++ b/Veriado.Infrastructure/Search/SqliteFts5Indexer.cs
@@
-        await using var dbTransaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
-        var transaction = (SqliteTransaction)dbTransaction;
+        await using var sqliteTransaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
@@
-            await helper
-                .IndexAsync(document, connection, transaction, beforeCommit, cancellationToken)
+            await helper
+                .IndexAsync(document, connection, sqliteTransaction, beforeCommit, cancellationToken)
@@
-            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
+            await sqliteTransaction.CommitAsync(cancellationToken).ConfigureAwait(false);
@@
-            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
+            await sqliteTransaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
@@
-        await using var dbTransaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
-        var transaction = (SqliteTransaction)dbTransaction;
+        await using var sqliteTransaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
@@
-            await helper
-                .DeleteAsync(fileId, connection, transaction, beforeCommit, cancellationToken)
+            await helper
+                .DeleteAsync(fileId, connection, sqliteTransaction, beforeCommit, cancellationToken)
@@
-            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
+            await sqliteTransaction.CommitAsync(cancellationToken).ConfigureAwait(false);
@@
-            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
+            await sqliteTransaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
```
```diff
diff --git a/Veriado.Infrastructure/Persistence/AppDbContext.cs b/Veriado.Infrastructure/Persistence/AppDbContext.cs
--- a/Veriado.Infrastructure/Persistence/AppDbContext.cs
+++ b/Veriado.Infrastructure/Persistence/AppDbContext.cs
@@
     public AppDbContext(DbContextOptions<AppDbContext> options, InfrastructureOptions infrastructureOptions, ILogger<AppDbContext> logger)
         : base(options)
     {
         _options = infrastructureOptions;
         _logger = logger;
+        EnsureSqliteProvider();
     }
@@
     public async Task InitializeAsync(CancellationToken cancellationToken = default)
     {
-        if (Database.IsSqlite())
-        {
-            await Database.ExecuteSqlRawAsync("PRAGMA optimize;", cancellationToken).ConfigureAwait(false);
-        }
+        EnsureSqliteProvider();
+        await Database.ExecuteSqlRawAsync("PRAGMA optimize;", cancellationToken).ConfigureAwait(false);
     }
@@
     internal async Task EnsureSqliteMigrationsLockClearedAsync(CancellationToken cancellationToken)
     {
+        EnsureSqliteProvider();
         const string createTableSql = "CREATE TABLE IF NOT EXISTS "__EFMigrationsLock"(
  "Id" INTEGER NOT NULL CONSTRAINT "PK___EFMigrationsLock" PRIMARY KEY,
  "Timestamp" TEXT NOT NULL
);";
         const string deleteSql = "DELETE FROM "__EFMigrationsLock";";
@@
     internal async Task<bool> NeedsSqliteMigrationsHistoryBaselineAsync(CancellationToken cancellationToken)
     {
-        if (!Database.IsSqlite())
-        {
-            return false;
-        }
+        EnsureSqliteProvider();
 
-        var connection = Database.GetDbConnection();
+        var connection = (SqliteConnection)Database.GetDbConnection();
@@
     internal async Task EnsureSqliteMigrationsHistoryBaselinedAsync(CancellationToken cancellationToken)
     {
-        if (!Database.IsSqlite())
-        {
-            return;
-        }
+        EnsureSqliteProvider();
@@
         if (inserted > 0)
         {
             _logger.LogInformation("Baselined EF migrations history with initial migration for legacy SQLite database.");
         }
     }
+
+    private void EnsureSqliteProvider()
+    {
+        var providerName = Database.ProviderName;
+        if (string.IsNullOrWhiteSpace(providerName) || !providerName.Contains("Sqlite", StringComparison.OrdinalIgnoreCase))
+        {
+            throw new InvalidOperationException("AppDbContext requires Microsoft.Data.Sqlite as the underlying EF Core provider.");
+        }
+    }
 }
```
```diff
diff --git a/Veriado.Infrastructure/Persistence/ReadOnlyDbContext.cs b/Veriado.Infrastructure/Persistence/ReadOnlyDbContext.cs
--- a/Veriado.Infrastructure/Persistence/ReadOnlyDbContext.cs
+++ b/Veriado.Infrastructure/Persistence/ReadOnlyDbContext.cs
@@
     public ReadOnlyDbContext(DbContextOptions<ReadOnlyDbContext> options, InfrastructureOptions infrastructureOptions)
         : base(options)
     {
         _options = infrastructureOptions;
+        EnsureSqliteProvider();
         ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
         ChangeTracker.AutoDetectChangesEnabled = false;
         Database.SetCommandTimeout(30);
     }
@@
         }
     }
+
+    private void EnsureSqliteProvider()
+    {
+        var providerName = Database.ProviderName;
+        if (string.IsNullOrWhiteSpace(providerName) || !providerName.Contains("Sqlite", StringComparison.OrdinalIgnoreCase))
+        {
+            throw new InvalidOperationException("ReadOnlyDbContext requires Microsoft.Data.Sqlite as the configured provider.");
+        }
+    }
 }
```
```diff
diff --git a/Veriado.Infrastructure/Persistence/Interceptors/SqlitePragmaInterceptor.cs b/Veriado.Infrastructure/Persistence/Interceptors/SqlitePragmaInterceptor.cs
--- a/Veriado.Infrastructure/Persistence/Interceptors/SqlitePragmaInterceptor.cs
+++ b/Veriado.Infrastructure/Persistence/Interceptors/SqlitePragmaInterceptor.cs
@@
     public override async Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
     {
-        if (connection is not SqliteConnection sqlite)
-        {
-            return;
-        }
+        if (connection is not SqliteConnection sqlite)
+        {
+            throw new InvalidOperationException($"SqlitePragmaInterceptor requires SqliteConnection but received {connection.GetType().FullName}.");
+        }
 
         await SqlitePragmaHelper.ApplyAsync(sqlite, _logger, cancellationToken).ConfigureAwait(false);
     }
 }
```
```diff
diff --git a/Veriado.Application.Tests/Veriado.Application.Tests.csproj b/Veriado.Application.Tests/Veriado.Application.Tests.csproj
--- a/Veriado.Application.Tests/Veriado.Application.Tests.csproj
+++ b/Veriado.Application.Tests/Veriado.Application.Tests.csproj
@@
-    <PackageReference Include="Microsoft.Data.Sqlite" Version="8.0.6" />
+    <PackageReference Include="Microsoft.Data.Sqlite" Version="9.0.9" />
```
```diff
diff --git a/Veriado.Infrastructure/Search/FtsWriteAheadService.cs b/Veriado.Infrastructure/Search/FtsWriteAheadService.cs
--- a/Veriado.Infrastructure/Search/FtsWriteAheadService.cs
+++ b/Veriado.Infrastructure/Search/FtsWriteAheadService.cs
@@
-        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken)
-            .ConfigureAwait(false);
+        await using var sqliteTransaction = await connection.BeginTransactionAsync(cancellationToken)
+            .ConfigureAwait(false);
@@
-            await MoveToDeadLetterInternalAsync(connection, transaction, entry, error, cancellationToken).ConfigureAwait(false);
-            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
+            await MoveToDeadLetterInternalAsync(connection, sqliteTransaction, entry, error, cancellationToken).ConfigureAwait(false);
+            await sqliteTransaction.CommitAsync(cancellationToken).ConfigureAwait(false);
@@
-            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
+            await sqliteTransaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
@@
-        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken)
-            .ConfigureAwait(false);
+        await using var sqliteTransaction = await connection.BeginTransactionAsync(cancellationToken)
+            .ConfigureAwait(false);
@@
-            await helper.IndexAsync(document, connection, transaction, beforeCommit: null, cancellationToken, enlistJournal: false)
+            await helper.IndexAsync(document, connection, sqliteTransaction, beforeCommit: null, cancellationToken, enlistJournal: false)
@@
-            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
+            await sqliteTransaction.CommitAsync(cancellationToken).ConfigureAwait(false);
@@
-            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
+            await sqliteTransaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
```
```diff
diff --git a/Veriado.Infrastructure/Search/SearchHistoryService.cs b/Veriado.Infrastructure/Search/SearchHistoryService.cs
--- a/Veriado.Infrastructure/Search/SearchHistoryService.cs
+++ b/Veriado.Infrastructure/Search/SearchHistoryService.cs
@@
-        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
+        await using var sqliteTransaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
@@
-            update.Transaction = (SqliteTransaction)transaction;
+            update.Transaction = sqliteTransaction;
@@
-                insert.Transaction = (SqliteTransaction)transaction;
+                insert.Transaction = sqliteTransaction;
@@
-        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
+        await sqliteTransaction.CommitAsync(cancellationToken).ConfigureAwait(false);
```
```diff
diff --git a/Veriado.Infrastructure/Search/SearchFavoritesService.cs b/Veriado.Infrastructure/Search/SearchFavoritesService.cs
--- a/Veriado.Infrastructure/Search/SearchFavoritesService.cs
+++ b/Veriado.Infrastructure/Search/SearchFavoritesService.cs
@@
-        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
+        await using var sqliteTransaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
@@
-            update.Transaction = (SqliteTransaction)transaction;
+            update.Transaction = sqliteTransaction;
@@
-        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
+        await sqliteTransaction.CommitAsync(cancellationToken).ConfigureAwait(false);
```

# Veriado Package Format (VPF) 1.0 – Export/Import návrh

## Aktuální stav
- **ExportPackageService** dnes spouští pouze logický export (per-file) a ignoruje fyzické režimy. Export ověřuje pending migrace, spočítá diskovou náročnost, kopíruje soubory z aktuálního storage rootu, generuje per-file JSON deskriptory a zapisuje `package.json` + `metadata.json` v adresáři `files/`/`extra/`. Při chybě loguje a vrací `StorageOperationResult` s počty exportovaných/chybějících souborů.
- **ImportPackageService** měl původně „fyzický“ import (kopie `veriado.db` + storage), ale nově obsahuje VPF validační pipeline (`ValidateLogicalPackageAsync`, `CommitLogicalPackageAsync`). Validační fáze používá `VpfPackageValidator` – kontroluje manifest/metadata, existenci `files/`, páry soubor/deskriptor, velikost a SHA-256 hash, počty souborů/bytů a shodu schema verzí. Následně klasifikuje položky proti DB (dle `fileId`, `contentHash`, cesty) do stavů `New`, `DuplicateSameVersion`, `DuplicateOlderInDb`, `DuplicateNewerInDb`, `ConflictOther`.
- **StorageManagementService** je façade pro WinUI/API; mapuje DTO ↔ aplikační modely, exposeuje validaci/commit importu, export a migraci. DTO vrací detailní výsledek (`ImportValidationResultDto`, `ImportCommitResultDto`).
- **Slabiny dosavadního stavu**: původní metoda `ImportPackageAsync` kopírovala fyzickou DB a storage, což bránilo kompatibilitě a integritě mezi verzemi. Chyběla dvoufázová kontrola a deterministické chování pro konflikty verzí/duplikáty. Chyběly explicitní manifest/metadata modely sdílené kontrakty, a VPF commit zatím neprováděl fyzické kopie souborů ani nevyužíval strategii konfliktů.

## Cílová architektura (shrnutí)
- **VPF 1.0 struktura**: `package.json` (vizitka balíčku), `metadata.json` (technické parametry), adresář `files/` s daty + `<soubor>.json` deskriptorem; volitelně `extra/` pro globální metadata.
- **Integrita**:
  - manifest: `spec="Veriado.Package"`, `specVersion="1.0"`, identita balíčku, původ instance, čas/autor exportu, `exportMode="LogicalPerFile"`.
  - metadata: `formatVersion=1`, `applicationVersion`, `databaseSchemaVersion`, `exportMode`, `originalStorageRootPath`, `totalFilesCount`, `totalFilesBytes`, `hashAlgorithm="SHA256"`, `fileDescriptorSchemaVersion=1`, `extensions=[]`.
  - per-file deskriptor: schema/version, `fileId`, `originalInstanceId`, `relativePath`, `fileName`, `contentHash` (SHA-256 hex), `sizeBytes`, `mimeType`, audit (created/modified/by), `isReadOnly`, `labels[]`, `customMetadata`, `extensions`.
  - Validace zajišťuje shodu hashů/velikostí, přítomnost dvojic soubor/deskriptor a shodu počtů/bytů z metadata.json.
- **Detekce duplikátů/verzí**: porovnání podle `fileId`, záložně `contentHash` + relativní cesty. Stavové kategorie: `New`, `DuplicateSameVersion`, `DuplicateOlderInDb` (balíček novější), `DuplicateNewerInDb` (balíček starší), `ConflictOther` (kolize cesty/obsahu). Pro určení novosti se používá `lastModifiedAtUtc` a `contentHash`.
- **Dvoufázový import**:
  - `Validate` – bez zápisu; validuje strukturu/hashy, načte DB a klasifikuje každou položku, vrací Issues + per-item preview a souhrny počtů.
  - `Commit` – přijme strategii (`SkipIfExists`, `UpdateIfNewer`, `AlwaysOverwrite`, `CreateDuplicate`) a promítne rozhodnutí do fyzických kopií/DB (předpřipraveno). Připravuje `ImportCommitResult` se statistikami a případnými varováními.
- **Role projektů**: Infrastructure řeší VPF modely/validator a práci se storage; Application definuje modely výsledků/strategií; Services orchestruje import/export a mapuje DTO; Contracts vystavují DTO pro WinUI; frontend volá Validate → zobrazí stav → vybere strategii → Commit.

## Další kroky (implementační poznámky)
- Dotáhnout mapování commit fáze do aplikační vrsty (FileImportService) tak, aby se kromě kopií na disk aktualizovaly i doménové agregáty a audit.
- Doplnit UI o výběr strategie a per-item rozhodnutí s využitím nových DTO (`DefaultConflictStrategy`).
- Rozšířit integrační testy o scénáře hash-mismatch, chybějící deskriptory, kolize cesty a novější/starší verze v DB.

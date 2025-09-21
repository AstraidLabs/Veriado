# Veriado.Infrastructure

## TextExtractor providers

Subsystem `Veriado.Infrastructure.Search` poskytuje kompozitní `ITextExtractor`, který vyhodnotí MIME typ a vybere specializovaný provider. Ve výchozím stavu se obsah načítá z `FileEntity.Content.Bytes`, respektuje `InfrastructureOptions.MaxContentBytes` a při chybě nebo překročení limitů vrací `null`.

Podporované mimetypy a limity:

- **PlainTextExtractor** – `text/plain`, `text/csv`, `application/json`, `application/xml`, `text/xml`; max ~200k znaků, detekce kódování s registrací `CodePagesEncodingProvider`.
- **PdfTextExtractor** (UglyToad.PdfPig) – `application/pdf`; max 500 stran / 200k znaků.
- **DocxTextExtractor** (DocumentFormat.OpenXml) – `application/vnd.openxmlformats-officedocument.wordprocessingml.document`; max 4000 odstavců / 200k znaků.
- **PptxTextExtractor** – `application/vnd.openxmlformats-officedocument.presentationml.presentation`; max 500 slidů, 10k textových elementů, 200k znaků.
- **XlsxTextExtractor** – `application/vnd.openxmlformats-officedocument.spreadsheetml.sheet`; max 64 listů, 2k řádků na list, 64 sloupců a 50k buněk.
- **OdtTextExtractor** – `application/vnd.oasis.opendocument.text`; parser `content.xml`, limit 10k textových elementů / 200k znaků.
- **OdpTextExtractor** – `application/vnd.oasis.opendocument.presentation`; parser `content.xml`, limit 12k textových elementů / 200k znaků.
- **OdsTextExtractor** – `application/vnd.oasis.opendocument.spreadsheet`; parser `content.xml`, limit 2k řádků, 50k buněk / 200k znaků.

Každý provider je `Singleton`, stateless a funguje offline bez externích služeb. Pokud specializovaný extractor neuspěje nebo MIME není podporován, kompozitní extractor provede fallback na `PlainTextExtractor`.

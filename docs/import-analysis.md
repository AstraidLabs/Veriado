# Import pipeline analysis

## Backend findings
- **Configuration loss in streaming imports** – `ImportService.NormalizeOptions(ImportOptions?)` previously reset the search pattern, recursion, metadata preservation and read-only options to hard-coded defaults, so the WinUI client could not influence these behaviours when using the streaming API. This made the UI toggles for "recursive", "keep file-system metadata" and "set read only" ineffective. The normalization now carries through user-specified values, ensuring the streaming pipeline honours the selected settings.
- **Default author trimming** – The normalization step now trims the optional author value instead of replacing it with an empty string, preventing accidental spaces from propagating to the domain.

## Frontend findings
- **Incomplete option projection** – `ImportPageViewModel.BuildImportOptions` only forwarded resource limits to the service, so metadata-related toggles never reached the backend. The builder now mirrors the full request so both streaming and fallback batch imports receive consistent input.

## Suggested follow-up improvements
- Surface an optional search-pattern picker in the UI to allow narrowing imports without editing files manually.
- Consider exposing the buffer-size limit in advanced settings so power users can tune throughput on slow disks.

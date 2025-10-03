# CHANGELOG

## What changed
- Added the `v_file_search_base` view and supporting SQL infrastructure for centralised grid queries.
- Introduced a consolidated grid search pipeline that hydrates `FileSummaryDto` results directly from SQLite.
- Added indexes optimised for server-side filtering and ordering in the file grid workflow.

## Why
- Reduce the number of round-trips required to serve the file grid and remove duplicated filtering logic.
- Push ordering and pagination work into SQLite for better performance and simpler application code.
- Ensure the database can satisfy the new server-side filters efficiently.

## How to rollback
- Drop the `v_file_search_base` view and the new supporting indexes, then deploy the previous application binaries.
- Revert commits `aeef453`, `f4b714a`, `37ae869`, and `c5e4a97` to restore the prior query pipeline and schema.

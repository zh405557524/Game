# Framework data tools

- `build_runtime_definition.py` verifies the pinned v1.0 historical source database, copies the approved
  aggregate/rule whitelist, installs runtime topology and module tables, and atomically emits the ignored
  development Definition database.
- `validate_module_catalog.py` compares authoritative Markdown module names with the C# catalog and the
  generated database. `PersonModule` and `TaxModule` are accepted only as non-authoritative aliases.

Neither tool modifies the 408 MiB source database. A successful build is not evidence of commercial data
clearance; the generated manifest remains `commercial_release_ready=no`.

# P47 store microbenchmark and data-code generators

This is a .NET 8 rule/storage tool, not a Unity or Player FPS test. It uses actual `Resources/CombatConfig.json` and `DungeonConfig.json`, creates R1 through production profile commands, then explicitly adds 200 valid synthetic historical receipts as a size fixture. No author save is modified. Durable tests use a freshly allocated system-temp directory and preserve `Flush(true)`.

Run from PowerShell:

```powershell
dotnet run --project .\Tools\P47StoreBench\P47StoreBench.csproj -c Release -- . .\Reports\P47_STORE_AFTER.json
```

The output records source SHA256, runtime, fixture size, warm operation timings and managed allocations. The identical harness generated `Reports/P47_STORE_BASELINE.json` before Domain changes and `Reports/P47_STORE_AFTER.json` afterward. Eight warm-up operations precede each measured group. OS scheduling, .NET tiered compilation and concurrent editor work can affect latency; compare allocations as well as p50/p95. Do not interpret these operation latencies as Player frame times.

`CAS.ReadParseValidateDigest` deliberately measures the original full-read algorithm as an isolated reference. It is **not** the optimized Store fast-path timer. `ProfileStore.SaveDurable` and `Profile.SaveCurrent.EndToEnd` exercise the actual selected implementation.

## Regenerate typed clones after DTO changes

```powershell
dotnet run --project .\Tools\P47StoreBench\P47StoreBench.csproj -c Release -- generate .\Assets\CombatV051\Domain\CombatDeepClone.Generated.cs
```

The generated code contains direct field copies, typed collection loops, an exact field signature for every public serializable Domain DTO, and the special Profile copy that omits the old active combat branch it replaces. Reflection is used only by this offline generator and tests. Runtime cloning neither reflects over fields nor invokes JSON load/migration callbacks. Non-Domain `CombatJson.Clone` callers retain their JSON fallback.

Run `P47CloneTests` after regeneration. It verifies every generated field signature, populated/null serialized equivalence and absence of shared mutable references. A newly added field fails the test until regeneration. Keep the existing `.meta` file and GUID when regenerating.

## Regenerate the exact-contract JSON writer

```powershell
dotnet run --project .\Tools\P47StoreBench\P47StoreBench.csproj -c Release -- generate-json .\Assets\CombatV051\Domain\CombatTypedJson.Generated.cs
```

The generator reads actual Newtonsoft contracts and preserves readable property order, names, enum representation, null behavior and collection order. The runtime still uses Newtonsoft `JsonTextWriter` for scalar values and escaping. It does not deserialize, migrate or repair a live object. Config hashes and command fingerprints retain the original JSON format.

Only known Domain DTOs and their exact collection types use the fast path. Nondefault/global serializer settings, converters, serialization/error callbacks, unsupported contract options, derived collections and required-null values fall back to the original `JsonConvert.SerializeObject`. Required-null rejection therefore preserves the original exception, including its message. Unsupported future contracts must not silently omit behavior.

Run `P47JsonContractTests` and `P47JsonPaidStoreTests`. Their oracle directly calls `JsonConvert.SerializeObject`, never the optimized `CombatJson.Write`. Every DTO has a generated contract signature that must match the actual Newtonsoft contract, plus populated/null exact-text tests. File tests load an original-Newtonsoft-encoded paid roll and prove duplicate commands preserve faces and resources. Keep the generated `.meta` GUID stable.

## Store safety

The fast path reads the actual current main-file text while holding the existing per-path lock. It is available only when every character matches the exact file image previously fully loaded or successfully durably committed by the same Store instance. A changed file uses the validated slow path and original CAS checks; timestamps/file size are never authority. Every new candidate still receives full validation, including inactive modes and encounter-start snapshots.

Temp write, durable flush, exact temp readback, atomic replacement/backup, exact final readback, all five fault callbacks and rollback remain synchronous. Authority is adopted only after the final fault point. JSON deserialization/migration is reserved for reading files, not cloning or serializing a live candidate.

## Optional production timing

Set `DomainTiming.Sample = (name, milliseconds) => ...`. Names are `Domain.Clone`, `Domain.Apply`, `Domain.Validate`, `Profile.Capture`, `Profile.Validate`, `Store.ReadCAS`, `Store.Encode`, `Store.WriteFlush`, `Store.ReplaceVerify`, `Store.UTF8Encode`, `Store.TempWrite`, `Store.DurableFlush`, `Store.TempReadback`, `Store.AtomicReplace`, `Store.MainReadback`. Samples are inclusive wall time; nested samples must not be summed. A null callback avoids timestamp reads and allocation. Callback exceptions are ignored so instrumentation cannot change a transaction outcome.

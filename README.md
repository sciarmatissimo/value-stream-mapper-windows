# Value Stream Mapper (Windows)

The Windows edition of **Value Stream Mapping**: draw lean value-stream maps, record data-box metrics, analyze takt and lead time, simulate material and information flow to find bottlenecks, and exchange files with the iPhone / iPad / Mac app.

Built with .NET 8 and Avalonia UI (Fluent theme). Ships as a self-contained `win-x64` executable — no runtime installation needed.

## Features

- **Map editor** — the full value-stream icon set (processes with data boxes, customer/supplier, inventory, supermarkets, FIFO lanes, buffer/safety stock, transports, kanban, load leveling, operators, kaizen bursts…), push/pull/material arrows, manual and electronic information flows, kanban paths. Pan with the mouse, zoom with the wheel, drag elements, connect with two clicks, undo/redo.
- **Analysis** — takt time, production lead time, processing and value-creating time, process-cycle efficiency, cycle-time-vs-takt chart, the castellated lead-time ladder, kaizen register.
- **Flow animation** — a simulation driven by the map's own numbers: the supplier delivers at takt, every process works at its effective cycle time (derated by uptime), the customer pulls at takt. Material and information move along the map, WIP piles up in front of constraints, bottlenecks and starved steps are highlighted, and an end-of-run summary can be downloaded as a PDF report or CSV.
- **Import** — the bundled CSV/Excel template (Map / Elements / Connections), any element table (including the app's own CSV export), native `.xlsx` workbooks, Microsoft Visio `.vsdx` drawings, and `.vsmap` documents. A built-in guide shows the exact sheet layout with examples.
- **Export** — PDF drawing, PNG image, CSV metrics, `.vsmap` document.
- **Same file format as the Apple app** — `.vsmap` files round-trip byte-compatible (camel-cased keys, enum raw values, uppercase UUIDs, second-precision dates), verified by a test that opens a file written by the iOS app.

## Repository layout

| Path | Purpose |
|---|---|
| `src/VsmCore` | Domain model, `.vsmap` JSON, analysis math, CSV/Excel/Visio importers, exports, flow simulator — no UI dependency |
| `src/VsmApp` | Avalonia desktop app: map canvas, icon and arrow painters, inspector, analysis panel, animation, import guide, glossary, PDF/PNG export |
| `tests/VsmCore.Tests` | 19 xUnit tests incl. fixtures: `sample.vsmap` (written by the iOS app), `ImportFixture.xlsx`, `ImportFixture.vsdx` |
| `tests/VsmApp.Tests` | Headless UI tests that drive the real windows and render frames to PNG |
| `.github/workflows/build.yml` | Builds, tests and publishes the win-x64 package on every push |

## Building

```
dotnet build ValueStreamMapper.sln
dotnet test ValueStreamMapper.sln
dotnet run --project src/VsmApp
```

Self-contained Windows package:

```
dotnet publish src/VsmApp/VsmApp.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish/win-x64
```

The result is `publish/win-x64/ValueStreamMapper.exe`. Maps are stored in `Documents\ValueStreamMapper\Maps` (override with the `VSM_MAPS_DIR` environment variable). Double-clicking a `.vsmap`, `.csv`, `.xlsx` or `.vsdx` file onto the executable imports it.

The companion iPhone / iPad / Mac app lives in [value-stream-mapping](https://github.com/sciarmatissimo/value-stream-mapping).

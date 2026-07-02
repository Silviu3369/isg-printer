# ISG Printer

Technician-only Windows desktop printer support tool: print-server discovery,
one-click install with drivers staged from the server (Point-and-Print), queue
and spooler management, layered diagnostics with 1-click repairs, live SNMP
supply reads (toner / serial / page count), and diagnosis export — fully
portable, nothing written to the workstation.

## Download

Grab the portable, self-contained `ISGPrinter.App.exe` from the
[Releases](../../releases) page — no .NET installation required. Run it from
anywhere (including a USB stick); it starts clean every launch and stores
nothing on the machine.

## Current status

- .NET 10 WPF solution scaffolded.
- Clean Architecture projects created:
  - `ISGPrinter.Domain`
  - `ISGPrinter.Application`
  - `ISGPrinter.Infrastructure`
  - `ISGPrinter.App`
  - `ISGPrinter.Tests`
- Application manifest requires administrator permission at startup.
- Dashboard removed; application opens on `Printers`.
- Premium light UI: design tokens in `src/ISGPrinter.App/Styles/` (Tokens, Controls, DataGrid, Components),
  Mica system backdrop with a custom integrated title bar (Win11; solid fallback on older Windows),
  tactile raised buttons (gradient sheen, bevel highlight, layered shadow, 1px press),
  sidebar navigation with icons, global search in the title bar (Ctrl+F, filters Printers and Local Printers),
  semantic status pills, page transition animations, loading indicators and empty states.
- UI pages:
  - Printers
  - Local Printers
  - Diagnostics
  - Reports
  - Settings
  - About
- Local printer discovery uses WMI.
- Manual print server add validates the server first with `Get-Printer -ComputerName`; invalid or unreachable servers are not added to the current session.
- Printer discovery, auto-detect and direct-IP scans can be cancelled from the Printers page.
- Basic diagnostics and technician guidance are implemented.
- SNMP v2c/v3 is implemented: real Printer-MIB / Host-Resources OID reads for toner, model, serial, page count and status, consumed automatically from the active profile in Settings (callers pass only an IP).
- Toner/model/serial/page count auto-read over SNMP when a printer is selected on both the Printers and Local Printers pages (Local Printers resolves the device IP from its TCP/IP port).
- Portable mode is intentional: settings, known print servers and SNMP credentials live only in memory for the current session.
- The app starts clean on every launch so it can be run from a USB stick without leaving configuration or credentials on the workstation.
- File-based settings/DPAPI secret store implementations exist in Infrastructure, but production portable wiring uses the in-memory services.
- SNMP stays enabled by default, but no community or v3 password is assumed. Technicians enter the v2c community or v3 credentials in Settings for the current session only.
- Logs are written to a `Logs` folder next to the executable when that location is writable, so a USB-stick run keeps its logs on the stick.

## Build

```powershell
dotnet restore ISGPrinter.slnx
dotnet build ISGPrinter.slnx
dotnet test ISGPrinter.slnx
```

## Publish portable build

```powershell
dotnet publish .\src\ISGPrinter.App\ISGPrinter.App.csproj /p:PublishProfile=PortableWinX64
```

The portable executable is written to `.\publish`.

## Run

```powershell
dotnet run --project .\src\ISGPrinter.App\ISGPrinter.App.csproj
```

Windows will prompt for administrator permission because the app manifest uses `requireAdministrator`.

## License

Licensed under the [Apache License 2.0](LICENSE).

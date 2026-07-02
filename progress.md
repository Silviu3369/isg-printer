# ISG Printer — progress log

## 2026-06-17 — Real-network hardening pass (audit-driven)

Audited every network-facing path for hangs, crashes, and resource leaks that
could bite on a large production network. No **Critical** issues found (no
guaranteed crash, use-after-free, or injection — PowerShell shell-out uses
env-var parameters, SNMP parsing is fully guarded). Findings + actions:

### Fixed (Important)
- **`NetworkProbeProvider.CheckTcpPortAsync`** — replaced the
  `WhenAny(connectTask, Task.Delay)` pattern (which abandoned the connect task
  to fault unobserved while `TcpClient` was disposed under it) with a linked
  `CancellationTokenSource` + `CancelAfter`. The connect is now torn down
  cleanly on timeout. Mattered most for the subnet scanner firing hundreds of
  probes at dead hosts. Regression tests added (`NetworkProbeTests`).
- **`NetworkPrinterScanner`** — SNMP enrichment after the port sweep was
  **sequential** (N × SNMP timeout — a 30-printer scan could sit >1 min looking
  hung). Now parallel, bounded `SemaphoreSlim(16)`, order preserved. Also skips
  APIPA / link-local `169.254/16` prefixes (a disconnected NIC was costing 254
  wasted probes).
- **`ShellViewModel.RefreshShellStatusAsync`** — was the only load path with no
  `try/catch`; a WMI/environment hiccup could bubble out of the parallel startup
  `Task.WhenAll` or a fire-and-forget refresh. Now best-effort: logs via Serilog
  and keeps last-known values.
- **`App` global handlers** — added `TaskScheduler.UnobservedTaskException`
  (logs + `SetObserved`) alongside the existing `DispatcherUnhandledException`
  (logs + MessageBox + `e.Handled`) and `AppDomain.UnhandledException`. Faulting
  fire-and-forget tasks (auto-refresh, SNMP supply reads) no longer go silent.

### Verified solid (no change needed)
- `PowerShellRunner` — hard timeout, kills whole process tree, concurrent
  stdout/stderr read, env-var inputs (no injection).
- `PrinterDiscoveryService` — 25s per-server timeout, parallel cap 4, malformed
  JSON caught as a per-server error (never crashes).
- `PrintServerAutodetectService` — LDAP connection disposed, 15s timeout, paged
  search with cancellation between pages, AD failures swallowed to no-results.
- `SnmpPrinterProvider` — every query wrapped, `OperationCanceledException`
  re-thrown, parsing via `TryParse`/validity guards, no divide-by-zero.
- No sync-over-async (`.Result`/`.Wait()`) anywhere; the only `async void` is the
  `Loaded` handler, covered by the global dispatcher handler.

### Deferred (honest)
- **SNMP sync call is not interruptible mid-call.** `SnmpPrinterProvider.QueryAsync`
  runs SharpSnmpLib's blocking API inside `Task.Run`; a caller cancel can't abort
  an in-flight socket wait, so a cancelled read still holds a threadpool thread
  until its own timeout (bounded ≤15s; ≤2× for v3 discovery+request). The stale
  result is already dropped, so it's correct — just thread occupancy. **Not
  changed on purpose:** the SNMP path is not yet validated against a real printer,
  and a rewrite to the async API would risk destabilizing it before that.
- **CIM-based discovery** (drop `powershell.exe`) — modest per-server gain, real
  risk of breaking discovery that currently works on the user's network. Skipped.
- **Progressive discovery + per-server timeout surfaced in UI** — the biggest
  *perceived*-latency win, but a feature, not hardening. Queued for the
  performance pass.

### Verification
Build 0/0 · tests 10/10 · launched (RunAsInvoker), alive + responding after 8s,
184.5 MB working set · Event Viewer clean (no Application Error / .NET Runtime /
Hang events).

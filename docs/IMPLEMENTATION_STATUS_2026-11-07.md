# NajmCard implementation progress — 2026-11-07

## Independently verified
- GitHub source import and WPF compilation issues fixed in `3b60bae6a5a1360a03c6bf64fe1b621112ea0289`, CI run 42356657287: success.
- Hotspot expired-card bulk deletion added with re-validation and four regression tests in `af12edbf1dd29e8903c33175bac30346a056af8b`, CI run 42357067303: success.
- User Manager v6 profile and customer read-only inventory added with three API contract tests in `53aea5b7166f6f0b006abae019e6ea6a76463bfe`, CI run 42357467178: success.
- Windows Core and Release tests precede four x86/x64 Portable and Installer packages. All tests and package jobs in the above three final runs succeeded.

## Critical incomplete work (do not merge)
- Original Hadramout has multiple service modules and many nested operations not yet in C#.
- User Manager v6 voucher create/activate, status classification, expired inventory, limitations, profile write and deletion are **not** implemented.
- Rich card printing/layout editor, Hotspot HTML file editing, XLSX reporting, VPN/Winbox tools, and license/trial are **not** functionally complete.
- No live RouterOS v6/VPN/hardware integration evidence; no manual Windows end-user smoke or installer/uninstaller QA.
- Current outputs are **developer builds**, not a verified commercial parity release.
- Source repository `salehbinzain83-star/Hadramout-cards` has not been modified by this work.

## CI
One CI run is triggered per pull request update; no redundant branch-push run. Windows Core and Windows Release (x86/x64) still run in parallel on the same workflow SHA; packaging follows only after all tests succeed. Main push CI runs as a post-merge verification, not a substitute for PR gate.

## Verified subsequent work packages
- User Manager v6 user creation, `!done=ret` validation, profile activation by returned internal ID, and safe handling of uncertain writes: CI run 42361944554 succeeded.
- RouterOS mutation allowlist and real-TCP safety regression: CI run 42362949103 succeeded.
- Durable SQLite/DPAPI delivery statuses with restart protection, printable confirmed vouchers only: CI run 42363376324 succeeded.
- Three-service voucher name collision preflight, including denied reads: CI run 42363909655 succeeded.
- User Manager v6 usage lifecycle classification, strict positive Transfer Limit expiry, on-demand WPF view and missing-field fallback: CI run 42364363603 passed all tests and packaging.
- User Manager large-inventory bounded timeouts and connection invalidation: CI run 42364799602 succeeded.
- Single User Manager v6 account enable/disable/delete with exact-ID preflight and readback: CI run 42369408344 succeeded.
- User Manager v6 Transfer Limit-only expired-card cleanup with a fresh single inventory scan: CI run 42369836008 succeeded.

## Source limitation and acceptance gates
- `salehbinzain83-star/Hadramout-cards` README describes partially recovered source, not a verified complete original production build. Exhaustive application parity needs an end-user comparison against the real installed version or full runnable binary.
- Still absent: PPPoE lifecycle and tools, advanced print design/Windows print geometry, Hotspot page upload/download, full profile/limitation writes and account recharge, live-device verification of UM actions, network maintenance, VPN, licensing/trial and commercial end-user acceptance.
- No actual MikroTik hardware/Windows GUI installation smoke or production license activation has been performed. Preserve draft PR #1; **no merge**.

## RouterOS 6 read-only IP/interface/neighbor tools — 2026-11-07
- Test-first baseline commit `830ea1b612fe086a24d9d6da70ab7cd067c6f395`; 5/5 new Core tests failed on missing method; 169 existing Core tests passed (Windows run 42389562643). No packages were generated on the failing SHA.
- Added three projected RouterOS API reads: `/interface/print`, `/ip/address/print`, `/ip/neighbor/print`. Missing identity fields and denied permissions fail closed; unrecognized interface state is displayed as unknown, not wrongly treated as false.
- Added an explicit, on-demand burgundy/gold ToolsView section with Arabic labeled read-only tables. It clears all previous rows on every refresh before fetching and does not show partial results on failure.
- Real TCP loopback protocol coverage confirms the exact three read-only commands; this is not a physical RouterOS 6 device-acceptance claim. Live hardware and UI smoke remain outstanding, as does write-capable Winbox-equivalent administration.

## Hadramout-compatible offline TXT / guarded RouterOS6 RSC exports — 2026-11-07
- New source-grounded implementation: `VoucherOfflineExporter` and explicit ReportsView TXT/RSC actions; user confirmation before any plaintext credential export. No router writes are triggered.
- RED baseline (fixed fixture) `23327f422ed602ddb11e1cddb0ffc833cae00054`; workflow 42390650904 compiled with 8 new tests failing for missing exports and 174 previously passing tests.
- Real filesystem tests enforce BOM UTF-8, CSV field escaping, leading-zero names, RouterOS quoting, customer-owned UM6 script guards, unique names across services, extension checks and preservation of existing files on invalid inputs.
- Open `docs/PARITY_AUDIT_2026-11-07.md` for a domain-by-domain comparison; full production parity and hardware tests remain outstanding.

## Card physical printing geometry and preview — 2026-11-07
- Hadramout `core/render.py` defines 50×19mm default card dimensions; `core/printing.py` defines A4/A3 page math, rows/columns/gaps/margins, order modes and duplex coordinate mirroring.
- RED commit `a2194d291020280cb01d15799d9eb00b7f3b1d08` and workflow 42391169793: 7 new tests fail on missing planner while 182 existing Core tests pass; Windows build succeeded, packaging blocked.
- Implemented `CardPrintLayoutPlanner` as deterministic, validated physical mm planner with A4/A3, row/column/snake order, and duplex coordinate calculation. Added a WPF `DocumentViewer` preview with genuine fixed-mm page geometry and on-demand single-sided printing, only for local `SENT` vouchers. Changing any setting invalidates the previous print job, and printing requires a separate confirmation.
- Actual PDF export, front/back template design, QR/background/editor, print calibration and physical printer tests remain incomplete. Duplex is calculated/tested by the core but deliberately **not enabled** in printer UI without a backside template and real-printer QA.

## Hadramout reference-image parity: login, dashboard and voucher generation (2026-11-07)
- Reviewed the three user-supplied screen photographs against Hadramout `ui/login.py`, `ui/network.py` and `core/cards.py`, read-only. Source is partially recovered: screenshots are visual/function hints, not proof of complete API behavior.
- Test-first contract added for separate saved router connections, opt-in DPAPI password retention, malformed JSON fail-closed, independently configurable voucher password characters/length, total username length including prefix, and a single projected PPP active-session read.
- Login WPF now has dedicated right navigation, center form and geometric burgundy/gold brand panel; local/remote VPN mode selection, saved profile pick/delete/new, API SSL, password reveal, and explicit consent for protected password save. Remote mode requires an already-active Windows VPN and does not manufacture a tunnel. No Hadramout license reuse or invented Najm activation.
- Dashboard requests one lightweight `/ppp/active/print` in addition to Hotspot sessions. If PPP access is denied it reports the Hotspot-only partial count, rather than silently inventing a zero PPP count. WPF cancels stale on-navigation refreshes and moves large-inventory grouping from the dispatcher to a background task. Actual last-refresh entries are labeled as application refreshes, not fabricated router audit logs.

## Reference-image generator parity and performance — 2026-11-07
- The three service selectors continue routing through their real RouterGateway methods; User Manager v6 customer ownership remains required and service switching invalidates pending dispatch.
- Username and password character sets and lengths can now be configured independently; the username length may include the prefix, while legacy generator callers retain backward compatible defaults.
- New **preview before creation** is ephemeral and does not touch SQLite or RouterOS; explicit create writes to the existing independent SQLite/DPAPI store, and send remains a distinct confirmed action guarded by the existing SENDING/UNKNOWN contract.
- Bulk generation and SQLite DPAPI persistence are off the WPF dispatcher to avoid UI freezes; displayed batches are limited to 500 rows and never silently restrict the actual generated/send count. Versioned profile-load responses cannot overwrite a later service selection.

## Unified editable voucher appearance — 2026-11-07
- New validated `VoucherCardAppearance` limits colors to #RRGGBB, chosen fonts to Arial/Tahoma/Segoe UI/Calibri, reasonable numeric font and border sizes, safe title length and optional password/profile printing. These fields are **real rendering controls**, not placeholder settings.
- The `VoucherStore` keeps the appearance in an isolated SQLite `app_preferences` row alongside its existing WAL-backed voucher storage. Saves are transactional; corrupted/invalid values fail closed without silent loss. The independent DB snapshot already includes this setting.
- GeneratorView presents title/font/size/background/text/border/visibility controls, saves to the local store and updates the burgundy-gold card preview. PrintingView uses the same saved appearance when creating the actual WPF FixedDocument pages and follows the password-visibility choice. A separate confirmation still precedes printer submission.
- Not yet implemented: binary card background image import, drag/drop element placement, QR rendering, PDF export and actual printer calibration. These are not represented as working options.

## Hotspot dashboard slim projection / accurate RouterOS disabled flag — 2026-11-07
- Password-free and comment-free projected dashboard Hotspot user scan using only `name,profile,disabled,bytes-in,bytes-out,limit-bytes-total`, plus internal ID. The existing full `ReadUsersAsync` remains unchanged for card management.
- Missing, malformed or silently dropped projected fields trigger one authoritative full RouterOS read, then the gateway skips the unsuccessful projection for the rest of that login session. Permission errors are not treated as empty tables. Dashboard DTO explicitly clears the password, even after full fallback.
- RouterOS Boolean `disabled=yes` now maps correctly in the shared helper (previously only `true` was recognized), avoiding misleading enabled counts.
- This reduces TCP response bytes and local memory when RouterOS honors `.proplist`, without changing customer data or issuing router writes. Real RouterOS6 scaling remains a field-test gate.

## Source-grounded RouterOS6 maintenance reads (2026-11-07)
- Compared original Hadramout `core/network.py::neighbor_devices`, `hotspot_servers` and `ui/operations.py` with NajmCard gateway; source repository was inspected READ-ONLY.
- Added receipt-based neighbor `count-only` preflight and a hard 5,000-device limit before the detailed `/ip/neighbor/print` projection; unknown/malformed counts fail closed. Read-only results include IPv4, MAC, identity, interface, platform, version and board, each bounded to 120 characters. Search and IP/MAC copy run locally without new RouterOS requests.
- Added projected actual Hotspot server states from `/ip/hotspot/print`: `.id,name,interface,profile,disabled,invalid,running`; invalid IDs are ignored and unrecognized flags remain unknown rather than being silently treated as false. Server page uses a user-initiated refresh, not background polling.
- This package intentionally excludes Hadramout's high-impact disable/reboot/optimize-db/temporary maintenance actions, which require durable recovery journaling and live-router verification before any port.

## User Manager v6 database read-only status (2026-11-07)
- Source: Hadramout `core/network.py::database_maintenance_status`, read-only reference; narrowed to `/tool/user-manager/database/print` for RouterOS 6 and display only `db-size` / `free-disk-space`.
- RED `27242081d267783f27b331c6d2145f5b2f0065d2`, GitHub Actions 42398025846: 4 new tests failed specifically for missing status reader while 217 previous Core tests passed; no packages generated on RED.
- Trap/absent package/permission refusal is displayed as **unavailable** rather than a fabricated zero. Missing metadata displays unknown. Untrusted RouterOS error text is not exposed.
- ToolsView now has a genuine on-demand UM v6 database status action. No v7 optimize-db, rebuild or reboot controls are included in this read-only work package. Live RouterOS6 testing remains a follow-up requirement.

## Unified bounded RouterOS 6 neighbor discovery — 2026-11-07
- All callers of `ReadNetworkInventoryAsync` now reuse the count-first `ReadNearbyDevicesAsync` path; the old unbounded neighbor scan is retired. Interface and IP reads retain their original projection. All three inventories are committed together only after neighbor read succeeds.
- RED regression commit `75167c5cb776100ff4d56ee91adae664ea1ced8c`, workflow 42398295109: 2 tests failed (including an actual authenticated TCP loopback protocol test), while 219 Core tests passed; packaging correctly blocked.
- Count-only RouterOS v6 `!done ret` is required and a missing/invalid count or more than 5,000 neighbors fails before detail download. Existing UI uses one bounded neighbor path; no router settings change.
- A neighbor is a discovered ephemeral device with no stable internal record ID, so the prior `RouterNeighbor.Id` is intentionally empty in this read-only view; UI operations use explicit displayed IP/MAC and never attempt a write by neighbor ID.

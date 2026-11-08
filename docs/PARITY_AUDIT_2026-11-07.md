# NajmCard ↔ Hadramout v1.1.3 — source-grounded completion audit (2026-11-07)

**Reference:** salehbinzain83-star/Hadramout-cards, main tree f5973dea4d3bb35ba18e67852c824ccf7634bd5f (READ ONLY). Its README explicitly describes a partially recovered Python/Tkinter distribution. The installed commercial product cannot be declared equivalent from this reference alone. **Implementation:** salehbinzain83-star/NajmCard, feature/port-hadramout-functional-parity, C#/.NET 10 WPF, isolated SQLite/DPAPI.

## Legend
- **Implemented in code:** a concrete C# service and/or desktop view exists; automated checks may cover it
- **Partial:** key production behavior is missing, or hardware/UI acceptance has not been executed
- **Not implemented:** no working, independently verified replacement exists
- All rows require **actual Windows + RouterOS6 field acceptance** before commercial parity.

| Domain | Source evidence | C# implementation now | Outstanding acceptance work |
| --- | --- | --- | --- |
| Connection/login | core/session.py; ui/login.py; core/connection_address.py | Basic RouterOS API and TLS login; partial | Saved connections, VPN/remote, reconnection, trial screen, Windows GUI |
| Dashboard | core/network.py; core/snapshots.py; ui/network.py | Geometric burgundy/gold WPF dashboard and real basic metrics; partial | Real-time traffic, service totals, activity, long-running router snapshots |
| Generate cards | core/cards.py; core/delivery.py | Basic Hotspot/UM6/PPPoE generator; durable delivery; partial | Full format/sequence options, price/quota/server/uplink metadata, previews |
| Cards inventory/actions | ui/network.py; core/network.py | Some selected Hotspot/PPP/UM operations, status and cleanup; partial | Complete CRUD, large-scale pagination, recharge decision, reconciliation |
| Expired cards | core/network.py | Transfer Limit-only Hotspot and UM6 cleanup with re-read; partial | Real routers, realistic high-volume performance, reporting |
| Packages/profiles | core/packages.py; ui/network.py | UM6 read/profile limits and selected PPP rate edits; partial | Full UM6/PPP/Hotspot profile and limitation CRUD, real gateway roundtrip |
| Print and designer | core/render.py; core/printing.py; core/windows_printing.py; ui/card_preview.py | A3/A4 fixed-mm print planner, validated positioning modes, local WPF preview and single-sided printing; partial | True designer/background/QR, cut marks, PDF, tested backside duplex, printer calibration and Windows smoke |
| Reports/export | core/exporting.py; core/db.py | CSV, 13-column XLSX, offline UTF-8 TXT and guarded RouterOS6 RSC; partial | Persist 13 data columns, report variants, per-site summaries, roundtrips |
| Hotspot pages | core/hotspot.py | Local text editor, ZIP validation/export, FTP/FTPS backup-first transfer; partial | Browser preview, live RouterOS6/FTP validation |
| Router tools | core/mikrotik.py; ui/operations.py | Read-only interfaces/IP/neighbors; partial | Winbox-equivalent edit tools, diagnostics, logs, backups, safe maintenance, VPN |
| Licensing | core/github_license.py; core/trial_runtime.py | No independent Najm licensing; not implemented | Owner-approved new issuer, new trust keys, trial semantics and OS support |
| Visual experience | ui/theme.py; ui/layout.py | Burgundy/gold RTL and vector icons; partial | All screens real Windows screenshots, DPI, keyboard, long text, responsive grids |
| Data integrity | core/db.py | Independent SQLite, DPAPI, migration, backup, durable SENT/UNKNOWN; partial | Full metadata migration, audit, restore acceptance, account records |
| Windows builds | installer/Hadramout.iss | 4 developer outputs x86/x64 Portable/Installer with PR CI+SHA256; partial | Real install/uninstall, Windows minimum OS decision, signed release |

## Evidence and limitations

- The repository contains **10 main WPF navigation destinations** and the release pipeline packages four variants; this does **not** mean the ten original Hadramout screens or all nested actions are complete.
- Core, Desktop and static UI CI run on Windows; actual printer, physical MikroTik and full GUI user journey are not automated by those jobs.
- Do not publish a percentage claimed to represent commercial parity: the reference source is incomplete, nested features are not of equal weight, and no original-vs-new end-to-end dataset exists.
- Licensing/trial and precise UM6 renewal/reset semantics are **owner policy decisions**, not opportunities to invent behavior.
- No direct modifications to Hadramout, its credentials, customers, license database, or production Cloudflare/Supabase.
- Keep the NajmCard PR in draft and do not merge incomplete development.

## This work package — TXT/RSC exporter

- Baseline RED test-first commits: 3677a12366282c458cd2cd02a253893859193e83 (fixture initially had a compile-escape defect), corrected RED 23327f422ed602ddb11e1cddb0ffc833cae00054. Windows run 42390650904 compiled and confirmed 8 new missing-implementation failures while 174 existing Core tests passed.
- The port supports two on-demand export formats with credential warnings: TXT quoted username/password rows and RSC guarded RouterOS6 Hotspot, PPPoE and UM6 import scripts.
- Fail-closed input validation and duplicate protection are performed **before any output file write**. The staged same-directory, flushed atomic replacement prevents damaging an existing export on validation or write failure.
- Strings passed to RouterOS are quoted and escaped; a pre-existing account fails the script guard rather than silently reactivating an existing User Manager profile. The script never auto-executes and it is not a live-router test.

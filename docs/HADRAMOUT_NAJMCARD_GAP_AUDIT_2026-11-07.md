# Functional comparison audit — Hadramout Cards ↔ NajmCard (2026-11-07)

## Repository provenance and limits
- Source reference: `salehbinzain83-star/Hadramout-cards`, inspected main commit `f5973dea4d3bb35ba18e67852c824ccf7634bd5f`, 63 tracked file blobs. **READ ONLY**.
- Independent destination: `salehbinzain83-star/NajmCard`, feature branch `feature/port-hadramout-functional-parity`, draft PR #1. `main` is not modified.
- Hadramout README says version 1.1.3 is **partially recovered** from an x64 Portable distribution and original history/CI/test coverage were not fully restored. Static source inspection cannot prove a complete commercial UI or release behavior.
- The original Python/Tkinter source is a behavior reference; NajmCard is a new C#/.NET 10 WPF implementation, not a wrapped Python app and not a fork of customer data, signed license credentials or trusted runtime secrets.

## Verified implementation comparison

| Domain | Hadramout evidence | NajmCard evidence | Gap / acceptance |
|---|---|---|---|
| Login and network | `ui/login.py`, `core/session.py`, `core/connection_address.py` | `RouterOsClient.cs`, WPF login | Saved connections, local/remote VPN, Win8 policy, UI modes and retry handling need E2E tests |
| Network dashboard | `ui/network.py::NetworkHome`, `core/network.py`, `core/snapshots.py` | `DashboardView.xaml(.cs)`, `DashboardMetrics.cs` | Counts across service types, long-running snapshots, activity and real-time traffic still differ |
| Cards generation | `core/cards.py::generate_cards`, `ui/network.py::VoucherWorkspace` | `VoucherGenerator.cs`, `GeneratorView` | Hotspot and UM6 basic; PPPoE, generation modes, fields and print previews incomplete |
| Delivery safety | `core/delivery.py::send_cards` | `VoucherStore`, `VoucherBatchDispatcher` | Per-card durable SENDING/SENT/FAILED/UNKNOWN and no blind uncertain replay ported. RouterOS live-device verification required |
| Cross-service uniqueness | `core/network.py::voucher_names` | `RouterGateway.ReadExistingVoucherNamesAsync` | Reads Hotspot, PPP and UM6; stops on denied reads; performance on large real routers unverified |
| User Manager v6 issuance | `core/network.py::add_user`, `core/mikrotik.py` | `RouterGateway.CreateAndActivateUserManagerV6Async`, `RouterOsReplyDecoder` | Uses returned single ID and safe rollback only on definite trap. Not live-verified |
| User Manager lifecycle | `core/network.py::_userman_lifecycle_v6`, `_userman6_profile_limit_map` | `UserManagerV6Usage`, `UserManagerV6Inventory` | On-demand usage classification incl. profile limits; large-dataset compatibility and complete runtime comparison pending |
| Expired-card handling | `core/network.py::purge_expired_vouchers` | `RouterGateway.DeleteExpiredByTransferAsync`, `DeleteExpiredUserManagerV6Async` | Hotspot and UM6 cleanup require positive exhausted Transfer Limit and authoritative recheck; only code/CI validated, real router QA pending |
| User/profile edit | `ui/network.py::NetworkProfilesPage`, `LimitationEditor`, `NetworkUsersPage` | Basic Hotspot edit; UM6 selected account enable/disable/remove with exact-ID query and postwrite verification | UM/PPP profile/limit write, recharge, selected-session disconnect, permissions and real hardware tests remain |
| Printing | `core/render.py`, `core/printing.py`, `core/windows_printing.py`, `ui/card_preview.py` | `PrintingView` basic WPF PrintDialog | Rich template editor, print layouts, duplex/cut marks, preview/PDF and printer calibration unimplemented |
| Data exports | `core/exporting.py` | `VoucherExcelExporter.cs`, `VoucherStore.ExportCsv` | Actual XLSX + 13 headers present, but several source columns are not persisted; .rsc script, TXT/import and report variants missing |
| Hotspot pages | `core/hotspot.py::validate_hotspot_template/upload_folder/download_folder` | `ToolsView` placeholder | FTP/SFTP transfer, template editing, preview, safe backup/restore absent |
| Router maintenance | `ui/operations.py::RouterMaintenancePage`, `core/network.py`, `core/maintenance_state.py` | `RouterOsCommandPolicy.cs`, basic lists | Router neighbors, DB optimization, maintenance/reboot safety, IP tools, VPN and diagnostics missing |
| Licensing | `core/github_license.py`, `core/trial_runtime.py`, `SUPABASE_LICENSING.md` | No Najm-specific licensing implementation | Independent issuer/claim/verify, signed feed and 20-day first-MikroTik-connect trial require owner policy and **new** Najm trust keys; never reuse Hadramout private secrets |
| Local database | `core/db.py` | `VoucherStore.cs` SQLite/DPAPI | Local service/owner identity and durable statuses implemented; batch templates, audit/event tables, import/export, business records, migration suite incomplete |
| Windows delivery | `installer/Hadramout.iss`, `scripts/build_windows.py` (recovery) | `windows-ci.yml`, `scripts/package.ps1`, `installer/NajmCard.iss` | x86/x64 Portable/Installer build in CI; real launch/install/uninstall and OS compatibility validation outstanding |
| Visual identity | Tkinter themes, custom card preview, colorful assets | WPF burgundy-gold theme, colored vector icons/charts | Screenshots from real Windows application + keyboard, scaling, RTL, long text and non-duplicated choices must be checked |

## Automated test-first implementation evidence
- Build fixes: run 42356657287.
- Hotspot expiry by Transfer Limit only: run 42357067303.
- UM6 read-only profiles/customers: run 42357467178.
- Corrected duplicate CI runs: run 42357708512.
- Real XLSX generation and password-safe cells: run 42360109198.
- UM6 add/activate and RouterOS trap-drain: run 42361944554.
- RouterOS write allowlist at real local TCP boundary: run 42362949103.
- SQLite SENDING/SENT/FAILED/UNKNOWN delivery: run 42363376324.
- Cross-service Hotspot/PPP/User Manager reserved names: run 42363909655.
- UM6 usage lifecycle/positive Transfer Limit classification: run 42364363603.
- RouterOS bulk-read timeout policy: run 42364799602.
- UM6 selected account actions and exact-ID readback: run 42369408344.
- UM6 positive Transfer Limit expired cleanup with one fresh inventory scan: run 42369836008.
- Exact-ID RouterOS TCP query serialization: check the latest run separately.

## Required work packages in dependency order
1. Finish UM6 advanced account/profile/limitation CRUD, balance recharge and session disconnection; selected account edit and transfer-expiry cleanup have automated coverage but still require live hardware verification.
2. Implement full PPPoE account/profile operations, name uniqueness across all services and safe session management.
3. Bring over card generation modes, per-card server, rate/quota/validity/price/comment metadata and all original report/export fields without inventing values.
4. Build the full visual printing and template editor, layout scaling, backgrounds, PDF and physical printer workflows.
5. Build Hotspot page editing/import/export and remote transfer with template validation and backups.
6. Add router address/interface/neighbor/IP-binding/maintenance/diagnostics and remote VPN scenarios.
7. Implement the independent NajmCard licensing and first-connect 20-day trial only after the owner's backend/trust-key/Windows compatibility decisions.
8. End-to-end Windows x86/x64 GUI and installer/uninstaller smoke, RouterOS 6 live tests, 4 SHA-256-verified artifacts, and protected-main PR approval.

## Non-negotiable gates
- Tests fail for a verified absence at the owning real boundary **before** production repair; do not substitute static JS mocks for RouterOS/SQLite integration.
- Windows Core and Windows Release x86/x64 execute in parallel on one SHA; tests must succeed before Portable and Installer packaging for each architecture.
- No merge with any failure or unverified full parity; PR #1 stays draft until the owner authorizes release.
- Do not touch the Hadramout repository, its license tables, Cloudflare/Supabase setup, installed runtime or customer data.

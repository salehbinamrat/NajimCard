# Full functional parity acceptance — NajmCard vs Hadramout v1.1.3
This is the mandatory acceptance contract, not a description of completed features. Source basis: `salehbinzain83-star/Hadramout-cards` main as inspected on 2026-11-07. Its README explicitly states the v1.1.3 Python source is **partially recovered**, so parity with a complete commercial executable still requires runtime confirmation. Changes to Hadramout are prohibited.

## Workstreams and required evidence
| Workstream | Required behaviors | Present NajmCard v0.1.0 | Acceptance gate |
|---|---|---|---|
| 01. Login and sessions | Router IP/hostname/port API, saved connections, SSL, local and remote/VPN mode, reconnection and failure details, password visibility, customer-facing activation and trial UI | Partial | Real RouterOS v6 + VPN integration |
| 02. Dashboard | Real counters of cards/active sessions/remaining/expired, CPU/RAM, per-service profile distribution, live traffic, recent activity, quick actions | Partial | No fabricated metrics; live router samples + graphical UI test |
| 03. Card generation | Hotspot and User Manager v6, username+password rules, digit/letter patterns, prefix, lengths, batch deduplication, creation permissions, preview, remote upload safely | Hotspot + UMv6 basic generation and submission; cross-service name preflight; PPP and rich choices missing | Real RouterOS+User Manager v6 verification |
| 04. Card management | Local/remote inventories, search/filter/sort/page, status, print/export, enable/disable, delete, reset limits, recharge, details, safe bulk operations | Partial | End-to-end tests; explicit confirmation; prevent retries of uncertain writes |
| 05. Expired cards | Expiry based on **Transfer Limit only**; remove Total Time Left condition; fast paged or batch delete with consistent audit | Hotspot deletion implemented with one authoritative re-read and no uncertain retries; User Manager bulk expiry still missing | RouterOS profile fixtures and scale/performance benchmark |
| 06. Profiles/packages | Manage profiles and limitations, User Manager links/unlinks, service-specific speed and quota, validation and conflict handling | Read-only Hotspot and User Manager v6 profiles/customers; editing and links still missing | Write/read roundtrip and permissions tests |
| 07. Print & graphical designer | Editable card number/font/size/colors/background/border, drag placement, multi-column card layouts, real preview, template saves, PDF, Windows printing | Basic print | Print/PDF pixel geometry comparison on Windows |
| 08. Reports/export/import | All original reports, accurate CSV and Excel, per-site/report naming and filters, safe imported records | CSV + XLSX 13-column schema; several source fields not stored yet; RouterOS scripts and full reports missing | XLSX contract tests and export/import roundtrip |
| 09. Hotspot pages | Browse/edit/import/export login/status/logout templates; preview and upload to router after validation; backups | Not implemented | Test against real RouterOS file API |
| 10. Router/network tools | Device address/interfaces, hotspot servers, neighbors, sessions, logs, profiles, backup/restore, maintenance mode, DB health, reboot confirmation | RouterOS mutation allowlist + basic reads; maintenance operations and FTP/Winbox still missing | Security boundary, restore, and actual-router tests |
| 11. Licensing & trial | Program-specific issuing/first-device binding/renewal/revocation, offline signed validation and 20-day MikroTik-started trial | Not implemented | **Owner policy required**: use new independent Supabase product and trust key; never reuse Hadramout license state |
| 12. Visual UI | Every page and dialog is RTL, burgundy-gold with colorful coherent vector icons, geometric responsive cards and real charts, no duplicate nav | Initial | Windows screenshots and keyboard/accessibility coverage |
| 13. Data integrity | Secure local SQLite encrypted secrets, migration, backup/restore, safe auditing and no access to Hadramout data | Real SQLite/DPAPI migration, backup, SENDING/SENT/FAILED/UNKNOWN durable delivery; broader database/accounting still missing | Real persisted database tests |
| 14. Delivery | .NET 10 WPF; Windows x86/x64 Portable and Installer; tests before packaging, two workflows parallel, artifact SHA-256, PR-only protected main | CI produces four developer artifacts with SHA256 files after tests; live Windows smoke, signed release and main protection verification pending | Four Windows artifacts on same SHA plus real smoke tests |

## Non-negotiable engineering rules
1. Re-implement functionality in C#; do not bundle, execute or embed the Hadramout Python application, database or production signing secrets.
2. Capture behavior with a failing regression test on the actual owning boundary before implementing each missing feature.
3. Windows Core and Windows Release must both succeed on the exact same SHA; release tests precede packaging.
4. Verify x86/x64 Portable and Installer with executable PE architecture, install/uninstall test and SHA-256 before merge.
5. When the current Hadramout behavior conflicts with a NajmCard change, obtain the owner's decision **before** touching that conflicting behavior.
6. No claim of full feature parity, no stable commercial release and no merging incomplete work into main.
7. Existing Hadramout GitHub repository, Supabase licensing backend, local installation and its customers remain untouched.

## Status
The v0.1.0 development branch includes RouterOS6 user-manager voucher issuance and read-only usage lifecycle, safety allowlists, cross-service name preflight, SQLite durable delivery, basic WPF dashboard/printing and CSV/XLSX. Most source parity and live QA workstreams are still incomplete. Mockups are not evidence of Windows runtime behavior. See HADRAMOUT_NAJMCARD_GAP_AUDIT_2026-11-07.md. Mockups are not evidence of Windows runtime behavior.

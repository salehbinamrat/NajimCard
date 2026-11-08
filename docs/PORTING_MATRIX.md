# NajmCard independent porting matrix

The Python source of Hadramout Cards was reviewed only to identify the needed behavior. None of its source files, database, activation keys or production endpoints have been copied into NajmCard.

| Feature | Current NajmCard status | Next gate |
|---|---|---|
| RouterOS v6 API login, TLS | Implemented using .NET networking | Real Windows + MikroTik test |
| Dashboard real card totals, active sessions, memory, CPU | Implemented using real RouterOS queries | UI manual test |
| Harmonized colorful vector icons and geometric dashboard | Implemented, standalone WPF vector graphics | Windows visual regression |
| Chart: transfer expiration/available/disabled breakdown | Implemented, based on real counts | Unit and Windows UI test |
| Chart: most-used profiles | Implemented, real snapshot, not fake history | Unit and Windows UI test |
| Hotspot card creation, saved batch and router upload | Initial implementation | RouterOS 6.49 regression tests |
| Local card store | SQLite + Windows DPAPI | Restore tests |
| Hotspot card enable/disable/delete | Implemented with explicit confirmation | Real-router integration |
| Online sessions, hotspot servers, profiles | Read-only views | Real-router integration |
| Printing | Basic WPF printing | Visual print template/quality |
| Reports | CSV export only | Excel reports, full filtering |
| User Manager v6 | NOT IMPLEMENTED | API semantics and real User Manager tests |
| Advanced graphic card designer and Excel imports | NOT IMPLEMENTED | Requirements and screenshot parity |
| Detailed Winbox-style tools, VPN | NOT IMPLEMENTED | Security and interoperability review |
| Commercial licensing/trial | NOT IMPLEMENTED; no Hadramout keys reused | Independent policy and key infrastructure |
| 4 Windows packages | CI definitions exist; not yet executed | Real Windows builds and artifacts |

**Do not merge into a future `main` branch or distribute as a full Hadramout equivalent until the missing features have been implemented and Windows tests pass.**

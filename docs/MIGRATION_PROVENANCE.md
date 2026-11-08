# Repository migration and separation (2026-11-08)

## NajmCard
- Dedicated private repository: `salehbinamrat/NajimCard`
- Original source repository: `salehbinzain83-star/NajmCard` at `ffefe0ab8aa5ede7e9c4f589cb54f83887137a30`.
- The 178 source files were migrated; one desktop licensing test fixture was intentionally corrected to use a deterministic signed-feed timestamp. Windows Core, Windows Release x86/x64 and four packages passed after the migration.
- This repository now contains **NajmCard alone**. The embedded `Hadramout_Cards/` source-copy folder was removed after its 63 original files were verified separately.

## Hadramout Cards
- Separate private repository: `salehbinamrat/Hadramout`
- Original source: `salehbinzain83-star/Hadramout-cards` at `f5973dea4d3bb35ba18e67852c824ccf7634bd5f`.
- Verification: **63/63 original Git blobs match**, including 17 image and icon binaries.
- The independent Hadramout repository has its own `main`, native Windows workflows and build artifacts, without being nested in NajmCard.
- The Hadramout source describes itself as a partially recovered Python v1.1.3 snapshot. Matching Git blobs and successful CI do not prove full historical feature parity or printer/device acceptance.

The old source repositories were not modified. No Hadramout application code was copied into NajmCard's C# implementation.

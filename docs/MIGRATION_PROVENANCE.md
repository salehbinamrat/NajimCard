# Independent repository separation — 2026-11-08

## NajmCard
- Main C# WPF application: [salehbinamrat/NajimCard](https://github.com/salehbinamrat/NajimCard)
- Source before the move: salehbinzain83-star/NajmCard at `ffefe0ab8aa5ede7e9c4f589cb54f83887137a30`.
- Imported 178 original files; one deterministic licensing-date fixture was subsequently corrected and tested in the destination.
- The unrelated nested `Hadramout_Cards/` copy was removed from NajimCard after exact verification of the separate Hadramout repository.
- Existing NajmCard Windows Core and Windows Release pipelines and functional code are unchanged.

## Hadramout (separate Private repository)
- [salehbinamrat/Hadramout](https://github.com/salehbinamrat/Hadramout)
- Original source: salehbinzain83-star/Hadramout-cards, commit `f5973dea4d3bb35ba18e67852c824ccf7634bd5f`.
- **63/63 exact Git blob identities**, including full-size original artwork.
- Its independent source and Windows Actions pipeline were tested on PR #2, including x86/x64 Core/Release and Portable/Installer with verified SHA-256.
- The original source README describes a partially recovered Python v1.1.3 snapshot, which must not be misconstrued as a verified full feature-parity recreation.

## Isolation policy
Neither repository uses the other as a source dependency. Separate code, test pipelines, release packages and future PRs. Do not copy Hadramout source back inside NajmCard.

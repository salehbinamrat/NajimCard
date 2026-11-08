# Hadramout source transfer — remaining two artwork files

Source repository: `salehbinzain83-star/Hadramout-cards`
Source commit: `f5973dea4d3bb35ba18e67852c824ccf7634bd5f`

NajmCard was migrated intact with 178/178 matching Git blob SHAs. Hadramout is kept under `Hadramout_Cards/` and its 46 UTF-8 source/configuration/test files plus 15 binary assets have been transferred with matching Git blob SHAs. **Two remaining image assets are NOT in this Git tree** because the GitHub connector's Contents endpoint does not return files larger than 1 MB.

Verified original files:
- `Hadramout_Cards/assets/default_card.png` — 1,262,691 bytes; Git blob SHA-1 `c1bcb76cf37d626e39bd65e72e45ddd2286f39bf`.
- `Hadramout_Cards/assets/login_hero.png` — 1,153,878 bytes; Git blob SHA-1 `9c5fbc6117eeef514f78f4ac9a87dcb417e915f0`.

Both originals were recovered from the previous successful Windows x64 Portable artifact and independently checked against the source Git blob hashes. A `Hadramout_2_Original_Assets.zip` is available as a conversation attachment. Extract the ZIP and upload **both** image files to the above paths on the `import/najmcard-hadramout-2026-11-08` branch. Do not upload ZIP alone as a substitute. After upload, compare the two Git blob IDs and verify 63/63 source files.

Do not merge this branch into `main` until both source trees are complete and Windows Core / Release tests and four release packages pass on the same SHA. This migration does not claim to repair or build recovered Hadramout source. Its nested workflow is archival, not an automatic top-level GitHub Actions trigger.

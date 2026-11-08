# NajmCard — GitHub Actions startup blocker (2026-11-08)

## Confirmed observations

Repository: `salehbinzain83-star/NajmCard` (private). The active draft PR is #6; it targets the `ui/hadramout-layout-match` development branch. Do not merge until the checks actually execute and pass.

- Run [42430794872](https://github.com/salehbinzain83-star/NajmCard/actions/runs/42430794872) previously passed Core, Release x86/x64 and packaging.
- Run [42473030628](https://github.com/salehbinzain83-star/NajmCard/actions/runs/42473030628) failed before Windows runner allocation. One retry (attempt 2) failed the same way.
- A temporary `ubuntu-24.04` no-checkout/no-dependencies job was added and executed as a controlled diagnostic in run [42473810648](https://github.com/salehbinzain83-star/NajmCard/actions/runs/42473810648). It also failed before allocation. The probe is **removed** from the workflow after this result.
- Run 42473810648: Windows Core (job 128710547332), Windows Release x86 (128710547099), Windows Release x64 (128710547395), and Ubuntu probe (128710547315) all failed with `runner_id = 0` and `steps = []`. The GitHub run timing endpoint reported 0 ms of billable Windows and Ubuntu compute. No WPF build or test steps started.

**Conclusion:** Runner provisioning is blocked across both operating systems. This is not evidence of C# compilation failure. The exact GitHub-side cause cannot be proven without reading a check-run annotation or the owner's Actions/billing settings. Do not claim the application is build-verified.

## Required account-owner checks, in order

1. Open the latest run, expand the failed Windows Core and Ubuntu probe check and inspect the **Annotations** or pre-start failure banner for its exact text.
2. Open GitHub personal account **Settings → Billing and licensing → Usage** and **Budgets**, inspect Actions/shared compute quota, payment failures, spending caps and any blocked status. Avoid changing budgets or enabling charges without express owner approval.
3. Review repository **Settings → Actions → General** and enabled runner policy; confirm GitHub-hosted workflows are permitted. Verify any account restriction.
4. If billing and runner policies are healthy but jobs remain rejected, contact [GitHub Support](https://support.github.com/) with the job ids, 0 steps, 0 billable ms and these run links. GitHub can check internal entitlement/provisioning records.
5. After the blocker is cleared, rerun all required Core/Release jobs on **one exact PR SHA**. Require executable Python, Core and Desktop tests before packaging. Then verify **x86 Portable + Installer** and **x64 Portable + Installer**, including SHA-256 checksums and actual WPF/user acceptance; merge only when every required status is green.

## Non-solutions

- Repeating the same C# build without a runner does not fix pre-start rejection.
- Changing `windows-2022` to an Ubuntu label cannot build the WPF desktop packages; the Ubuntu probe itself also failed.
- Never make this private commercial source public as a workaround without owner approval.
- Do not use a commit with a green result from an earlier SHA as evidence for the current changes.

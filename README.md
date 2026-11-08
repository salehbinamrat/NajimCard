# NajimCard

Independent NajmCard C# / .NET 10 WPF project for MikroTik card creation, printing, and network management.

The Hadramout Cards project is maintained as a **separate private repository** at [salehbinamrat/Hadramout](https://github.com/salehbinamrat/Hadramout). Its source files, assets, builds, and workflows do not belong to this NajimCard repository.

## Windows release policy

Run Windows Core and Windows Release x86/x64 tests in parallel on the same commit. Only after all pass, create the four artifacts (x86/x64 Portable and Installer) and their SHA-256 checksums. Do not merge a PR while any required job is failing.

Chummer standalone Android port — preview 0.3

Upstream base: 3325ee0589be3d35743f6ed720889018437ad555
Fork: https://github.com/DarenCJones/chummer5a-android
Development branch: android-port
License: GPL-3.0-or-later. Retain the upstream LICENSE and notices.

Implemented in this preview:
- Offline .chum5 import/export and .chum5lz import/export using upstream LZMA.
- Multiple locally saved characters, restore on launch and one-version backups.
- Profile, appearance, concept, description and background editing.
- Manual career Karma/Nuyen transactions with desktop-compatible expense records.
- Manual physical/stun damage tracking and career history.
- Human mundane creation drafts using Standard Priority or Sum-to-Ten budgets.
- Draft attribute, individual active skill, skill group, knowledge and language allocation.
- Standard draft point budgets, natural maxima and one-free-native-language validation.
- Unaugmented Human draft limits, initiative and condition monitor calculations.
- Dice pools with exploding sixes, cinematic/rushed options, Gremlins, variable glitches and bubble dice.
- Initiative order, pass advancement and persistence; ties use entry order for manual resolution.
- Searchable offline catalog browsing across bundled upstream data files.
- Characters/Create/Play/Library navigation and an in-app license view.

Still pending for desktop feature parity:
- Other metatypes, magic/resonance, Karma/Life Module creation and final creation validation.
- Qualities, specializations, equipment purchasing, augmentations and their improvement effects.
- Full calculations, skill/attribute advancement and derived effects for imported characters.
- Contacts, lifestyle, vehicles/drones and detailed combat/Matrix/magic workflows.
- Custom-data merging, house-rule settings, sourcebook selection and translations.
- Printing/PDF, original sheet templates and remaining import/export utilities.
- Dice reroll/extended-test modes, full initiative tie-breaking and other desktop tools.
- Hub and plugin integrations. Desktop plugin binaries require platform-specific ports.
- Production signing, updated release toolchain, device testing, accessibility and Android lifecycle QA.

Creation drafts cannot be finalized into career characters in this preview.
Imported characters retain unimplemented XML fields unchanged; their derived effects are not recalculated.
No network permission is requested. Export backups before uninstalling; Android removes private app storage.
The APK uses a development signing key and requires Android 8 or later.

Validation:
- 34 upstream plain character fixtures preserved through XML round trips.
- The same 34 fixtures preserved through compressed round trips using the upstream codec.
- Truncated and oversized compressed input rejected.
- Profile, damage, ledger atomicity, dice, initiative, catalog and creation-budget checks pass.
- Automated library checks do not establish complete desktop parity or Android UI correctness.

From this Android directory:
  dotnet run --project Chummer.Portable.Checks -- ..
  dotnet workload install android --skip-manifest-update --source https://api.nuget.org/v3/index.json
  dotnet build Chummer.Android -c Release
A Java 17 SDK and Android API 34 SDK/build tools are required. Set AndroidSdkDirectory and
JavaSdkDirectory MSBuild properties if they are outside the default locations.

The existing Windows solution is retained as the reference implementation.

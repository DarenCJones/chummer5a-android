Chummer standalone Android port — preview 0.10 (development checkpoint)

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
- Human, Elf, Dwarf, Ork and Troll creation drafts using Standard Priority or Sum-to-Ten budgets.
- Mundane, Magician, Adept, Mystic Adept, Aspected Magician and Technomancer talent priorities.
- Priority bonus skills/groups, magical skill restrictions and technomancer living persona records.
- Core Hermetic/Shamanic traditions, default technomancer stream and unaugmented drain/fading pools.
- Core spell, ritual, preparation and complex-form learning, free allowances and creation Karma costs/refunds.
- Drain/fading expression calculator and magic details in printable summaries.
- All 27 visible core SR5 adept powers: choices, rating limits, power-point costs, save records and removal.
- Mystic adept creation power-point purchases/refunds at 5 Karma each, capped by Magic.
- Permanent power effects on attributes, initiative dice, limits, armor, trained skill pools and unarmed attacks.
- Wound penalties, armor accessory caps/encumbrance, defense and damage resistance pools.
- Purchase/equip/unequip/remove for 35 supported fixed-price core armor and weapon entries, with creation Nuyen refunds.
- Strength-linked melee damage, accuracy bonuses, reach and equipment state in summaries.
- 77 supported core quality templates: one-level purchases, positive/negative Karma limits, prerequisites, conflicts, refunds and saved improvement records.
- Aptitude skill maxima, Exceptional Attribute natural maxima and Lucky Edge maxima, including allocation and removal validation.
- Loss of Confidence skill prerequisites/penalties and specialization suppression; Ambidextrous for core two-arm bodies and contextual off-hand rolls.
- Supported quality effects on trained skill pools, limits, damage resistance, wound thresholds and native-language allowances; conditional effects are shown separately.
- One active skill specialization per skill during creation, costing 1 skill point or 7 Karma, with refunds and +2 situational pools.
- Character-linked skill rolls with optional specialization and applicable conditional skill bonuses; untrained defaulting follows the catalog flags and standard -1 penalty.
- 16 core cyberware/bioware templates for priority drafts, with Standard/Used/Alphaware prices, Essence costs, creation availability and grade exclusions.
- Implant conflict checks, unique-effect non-stacking, Essence-based social limits and creation removal/refunds.
- 101 supported fixed-price unrated gear/ammunition inventory entries, quantity/pack pricing, weapon categories and preserved ammunition bonus metadata.
- Laser Sight/Bipod/Tripod weapon accessories, compatible unoccupied mounts, enabled effects, deployed recoil bonuses and parent/child refunds.
- Standard magazine/cylinder/belt loading, separate loaded gear stacks, compatible ammunition categories, unloading and supported firing consumption.
- Loaded ammunition damage/AP/type changes, remaining-round refunds and desktop-compatible clip references.
- Core creation Essence loss for Magic/Resonance, restored ratings on implant removal and adept/mystic adept power-point revalidation.
- Draft attribute, individual active skill, skill group, knowledge and language allocation.
- Standard draft point budgets, natural maxima and one-free-native-language validation.
- Unaugmented core metatype draft limits, initiative and condition monitor calculations.
- Dice pools with exploding sixes, cinematic/rushed options, Gremlins, variable glitches and bubble dice.
- Desktop Standard/Large/Really Large roll methods, success thresholds and miss rerolls with retained hits and desktop glitch/bubble handling.
- Initiative order, pass advancement and persistence; ties use entry order for manual resolution.
- Standard draft contacts with Charisma budgets and per-contact caps.
- Android printing / Save as PDF for a portable character summary.
- Searchable offline catalog browsing across bundled upstream data files.
- Characters/Create/Play/Library navigation and an in-app license view.

Still pending for desktop feature parity:
- Metavariants, other metatypes, Karma/Life Module creation and final creation validation.
- Adept Way discounts, initiation, activated/situational power tests, summoning/binding, sprites and detailed magic/Matrix play.
- Remaining quality types (including mentor spirits), knowledge specializations and strict-group house rules.
- Full equipment catalogs, unusual loading systems, integrated attack/recoil workflows, remaining/bundled accessories, modifications and active electronics.
- Burnout to zero, combined Improved Reflexes/initiative-implant interactions, cyberlimbs, modular/nested implants, remaining augmentations, Essence holes and additional grades.
- Active spell effects, wireless bonuses, weapon firing workflows, carrying-weight penalties and equipment house rules.
- Full calculations, skill/attribute advancement and derived effects for imported characters.
- Imported-contact editing, lifestyle, vehicles/drones and detailed combat/Matrix/magic workflows.
- Custom-data merging, house-rule settings, sourcebook selection and translations.
- Original desktop sheet templates and remaining import/export utilities.
- Extended-test workflows, full initiative tie-breaking and remaining desktop tools.
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
- Talent priorities, bonus skill choices, traditions, learning costs/refunds and older draft compatibility checks pass.
- Core power cost/choice coverage, saved improvements, augmented stats, skill caps and power removal/refunds pass.
- Equipment wallet atomicity, equipped effects, armor stacking caps/penalties and melee damage updates pass.
- Quality costs/limits, bidirectional conflicts, Bilingual removal safety, conditional bonuses and specialization cost/refund checks pass.
- Miss rerolls, preserved/new glitches, retained hits, exploding/cinematic/alternative methods and success-threshold checks pass.
- Implant grades/Essence/conflicts/non-stacking, gear/ammo pricing/quantities and accessory mount/refund/recoil checks pass.
- Loaded ammo split/consumption, category/capacity checks, damage/AP modifiers, no double loading/refunding and clip save/reopen checks pass.
- Magician/adept/technomancer Essence loss, min/max improvement records, power-budget rejection and creation removal restoration checks pass.
- Automated library checks do not establish complete desktop parity or Android UI correctness.

From this Android directory:
  dotnet run --project Chummer.Portable.Checks -- ..
  dotnet workload install android --skip-manifest-update --source https://api.nuget.org/v3/index.json
  dotnet build Chummer.Android -c Release
A Java 17 SDK and Android API 34 SDK/build tools are required. Set AndroidSdkDirectory and
JavaSdkDirectory MSBuild properties if they are outside the default locations.

The existing Windows solution is retained as the reference implementation.

Android runtime checks on the API 34 emulator:
- Launch, Human draft creation/save/reopen, profile editing/persistence, and dice rolling passed.
- Android print flow produced a valid PDF summary.
- Older draft reopening and Magician creation/tradition/spell persistence are tested on the emulator.
- Adept draft creation, Improved Reflexes learning, Armor Jacket/Combat Knife purchases and their effects/wallet persist after force-stop/relaunch.
- Selected quality and active specialization workflows/persistence are checked separately from desktop parity; see PARITY.txt for outstanding areas.
- Catlike selection, Pistols specialization point spending and their records/pools survive force-stop/relaunch on the emulator.
- Dice miss reroll retained hits, threshold success display, Really Large mode and no-misses handling passed emulator checks.
- A fresh mundane draft purchased Alphaware Wired Reflexes, 100 regular rounds, an AK-97 and a Laser Sight. Wallet, Essence, initiative, quantities, mount and accuracy effects survived force-stop/relaunch.
- An AK-97 loaded 38 APDS rounds, applied AP -6, fired a 3-round burst and retained 35 rounds/AP effects after restart. A Magician installed Alphaware Dermal Plating and retained Magic 5 / Essence 5.60 after restart.
- Switching tabs resets the content scroll position so the selected tab's tools remain discoverable.
- Aptitude allowed Spellcasting 7 with the expected 12-die pool, cost and persistence; Exceptional Attribute allowed Body 7 with 12 physical boxes and the expected point spending in the emulator.
- Loss of Confidence reduced that casting pool to 10, disabled the owned specialization's bonus, and persisted; Ambidextrous kept the pool at 10 for a selected off-hand test after restart.
- Loss of Confidence and Ambidextrous costs, prerequisites, modifier removal, core-body level limits and specialization suppression have portable regression coverage.
- Preview trimming and AOT are disabled after the trimmed build aborted in Mono startup.

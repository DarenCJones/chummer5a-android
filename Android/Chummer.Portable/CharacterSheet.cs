// SPDX-License-Identifier: GPL-3.0-or-later
using System.Net;
using System.Text;

namespace Chummer.Portable;

public static class CharacterSheet
{
    /// <summary>Portable summary, not the full upstream XSL character-sheet templates.</summary>
    public static string SummaryHtml(CharacterDocument character)
    {
        static string Encode(string value) => WebUtility.HtmlEncode(value);
        var html = new StringBuilder("<!doctype html><html><head><meta charset='utf-8'><title>Character summary</title><style>" +
            "body{font:11pt sans-serif;color:#16232b;margin:24pt}h1{font-size:24pt}h2{font-size:14pt;border-bottom:1pt solid #888;padding-top:12pt}" +
            "table{border-collapse:collapse;width:100%}td,th{text-align:left;padding:5pt;border-bottom:1pt solid #ddd}p{white-space:pre-wrap;overflow-wrap:anywhere}" +
            "tr{break-inside:avoid}h2{break-after:avoid}</style></head><body>");
        html.Append("<h1>").Append(Encode(character.Name)).Append("</h1><p>").Append(Encode(character.Summary())).Append("</p>");
        if (character.IsAndroidDraft)
        {
            var stats = character.DraftStats();
            var effective = character.EffectiveStats();
            html.Append("<h2>Implants</h2><p>").Append(Encode($"Essence remaining: {character.DraftEssence:0.00}")).Append("</p><ul>");
            foreach(var implant in character.InstalledAugmentations)
                html.Append("<li>").Append(Encode($"{implant.Name} {implant.Rating} — {implant.Grade}; {implant.Essence:0.00} Essence")).Append("</li>");
            html.Append("</ul>");
            html.Append("<h2>Purchased gear and ammunition</h2><ul>");
            foreach(var gear in character.DraftPurchasedGear)html.Append("<li>").Append(Encode($"{gear.Name} {gear.Extra} × {gear.Quantity}{(gear.LoadedInto.Length>0?" (loaded)":"")}")).Append("</li>");
            html.Append("</ul>");
            html.Append("<h2>Loaded ammunition</h2><ul>");
            foreach(var weapon in character.DraftLoadedAmmunition)html.Append("<li>").Append(Encode($"{weapon.Weapon}: {weapon.Rounds}/{weapon.Capacity} rounds; {weapon.Name}")).Append("</li>");
            html.Append("</ul>");
            html.Append("<h2>Weapon accessories</h2><ul>");
            foreach(var accessory in character.DraftAccessories)html.Append("<li>").Append(Encode($"{accessory.Weapon}: {accessory.Name} ({accessory.Mount}); {(accessory.Equipped?"Enabled":"Disabled")}")).Append("</li>");
            html.Append("</ul>");
            html.Append("<h2>Selected qualities</h2><p>").Append(Encode($"Positive: {character.PositiveQualityKarma}/25 Karma; negative: {character.NegativeQualityKarma}/25 Karma"))
                .Append("</p><ul>");
            foreach (var quality in character.SelectedQualities)
                html.Append("<li>").Append(Encode($"{quality.Name} {quality.Extra}: {quality.Karma} Karma")).Append("</li>");
            html.Append("</ul><h2>Active skill specializations</h2><ul>");
            foreach (var spec in character.DraftSpecializations)
                html.Append("<li>").Append(Encode($"{spec.Skill}: {spec.Name} (+2 only when applicable)")).Append("</li>");
            html.Append("</ul><h2>Permanent effect modifiers</h2><ul>");
            foreach (var effect in character.DraftEffectDescriptions()) html.Append("<li>").Append(Encode(effect)).Append("</li>");
            html.Append("</ul>");
            html.Append("<h2>Calculated permanent effects</h2><p>").Append(Encode(
                $"Attributes: {string.Join(", ", effective.Attributes.Select(a => $"{a.Key} {a.Value}"))}\n" +
                $"Limits: Physical {effective.PhysicalLimit}, Mental {effective.MentalLimit}, Social {effective.SocialLimit}\n" +
                $"Initiative: {effective.Initiative} + {effective.InitiativeDice}D6\nArmor: {effective.Armor}; armor penalty: {effective.ArmorPenalty}; wound penalty: {effective.WoundPenalty}\n" +
                $"Defense: {effective.DefensePool}; damage resistance before AP: {effective.SoakPool}\nCondition monitors: {effective.PhysicalBoxes} Physical / {effective.StunBoxes} Stun"))
                .Append("</p><p>Active powers, attack/recoil penalties, situational modifiers and carrying-weight penalties require manual handling.</p>");
            if (character.Field("adept") == "True")
            {
                html.Append("<h2>Adept powers</h2><p>").Append(Encode($"Power points remaining: {character.PowerPointsRemaining:0.##} / {character.PowerPointsTotal:0.##}"))
                    .Append("</p><ul>");
                foreach (var power in character.LearnedPowers)
                    html.Append("<li>").Append(Encode($"{power.Name} {power.Extra}: rating {power.Rating}; {power.Cost:0.##} PP")).Append("</li>");
                html.Append("</ul>");
            }
            html.Append("<h2>Weapon statistics</h2><ul>");
            foreach (var weapon in character.DraftWeapons().Prepend(character.DraftUnarmedAttack()))
                html.Append("<li>").Append(Encode($"{weapon.Name}: {weapon.Damage}; accuracy {weapon.Accuracy}; AP {weapon.AP}; reach {weapon.Reach}; attachment RC bonus {weapon.AccessoryRecoil} / {weapon.DeployedAccessoryRecoil} when deployed")).Append("</li>");
            html.Append("</ul>");
            if (character.DraftEquipment.Count > 0)
            {
                html.Append("<h2>Equipment state</h2><ul>");
                foreach (var item in character.DraftEquipment)
                    html.Append("<li>").Append(Encode($"{item.Name}: {(item.Equipped ? "Equipped" : "Stored")}")).Append("</li>");
                html.Append("</ul>");
            }
            html.Append("<p>").Append(Encode($"Unfinished {character.Field("prioritytalent").ToLowerInvariant()} {character.Metatype} draft. Creation has not been finalized."))
                .Append("</p><h2>Natural attributes</h2><table><tr><th>Attribute</th><th>Rating</th></tr>");
            foreach (var a in character.DraftAttributes)
                html.Append("<tr><td>").Append(Encode(a.Name)).Append("</td><td>").Append(a.Rating).Append("</td></tr>");
            html.Append("</table><h2>Unaugmented statistics</h2><p>")
                .Append(Encode($"Limits: Physical {stats.PhysicalLimit}, Mental {stats.MentalLimit}, Social {stats.SocialLimit}\n" +
                    $"Initiative: {stats.Initiative} + 1D6\nCondition monitors: {stats.PhysicalBoxes} Physical / {stats.StunBoxes} Stun"))
                .Append("</p>");
            html.Append("<h2>Racial traits</h2><p>").Append(Encode(string.Join(", ", character.DraftRacialQualities)))
                .Append("</p><p>").Append(Encode($"Natural armor: {character.DraftRacialBonus("Armor")}; reach bonus: {character.DraftRacialBonus("Reach")}; lifestyle cost adjustment: {character.DraftRacialBonus("LifestyleCost")}%"))
                .Append("</p>");
            var knowledge = character.DraftKnowledgeSkills;
            if (knowledge.Count > 0)
            {
                html.Append("<h2>Knowledge and languages</h2><ul>");
                foreach (var k in knowledge)
                    html.Append("<li>").Append(Encode($"{k.Name} ({k.Type}): {(k.Native ? "Native" : k.Rating.ToString())}")).Append("</li>");
                html.Append("</ul>");
            }
            if (character.Field("magenabled") == "True" || character.Field("resenabled") == "True")
            {
                html.Append("<h2>Magic and resonance</h2><p>").Append(Encode(
                    $"Talent: {character.Field("prioritytalent")}\nTradition / stream: {character.TraditionName}\n" +
                    $"Priority bonus skills selected: {character.TalentSkillChoices.Count}/{character.TalentSkillChoicesRequired}"));
                if (character.TraditionName.Length > 0)
                    html.Append(Encode($"\nUnaugmented drain / fading resistance: {character.DraftDrainPool()} dice"));
                html.Append("</p><table><tr><th>Ability</th><th>Type</th><th>Drain / fading</th></tr>");
                foreach (var (ability, kind) in character.LearnedSpells.Select(a => (a, a.Preparation ? "Preparation" : a.Category))
                    .Concat(character.LearnedForms.Select(a => (a, "Complex form"))))
                    html.Append("<tr><td>").Append(Encode($"{ability.Name} {ability.Extra}".Trim()))
                        .Append("</td><td>").Append(Encode(kind))
                        .Append("</td><td>").Append(Encode(ability.Difficulty)).Append("</td></tr>");
                html.Append("</table>");
            }
        }
        else html.Append("<p>Stored character data only. Imported-character effects and derived ratings have not been recalculated.</p>");
        foreach (var (key, title) in new[] { ("playername", "Player"), ("concept", "Concept"), ("description", "Description"), ("background", "Background") })
        {
            var value = character.Field(key);
            if (!string.IsNullOrWhiteSpace(value)) html.Append("<h2>").Append(title).Append("</h2><p>").Append(Encode(value)).Append("</p>");
        }
        foreach (var (collection, item, title) in new[] { ("weapons", "weapon", "Weapons"), ("armors", "armor", "Armor"),
            ("gears", "gear", "Gear"), ("cyberwares", "cyberware", "Augmentations"), ("vehicles", "vehicle", "Vehicles"), ("spells", "spell", "Spells"), ("complexforms", "complexform", "Complex forms"), ("qualities", "quality", "Qualities") })
        {
            var items = character.OwnedItems(collection, item);
            if (character.IsAndroidDraft && collection is "spells" or "complexforms") continue;
            if (items.Count == 0) continue;
            html.Append("<h2>").Append(title).Append("</h2><ul>");
            foreach (var name in items) html.Append("<li>").Append(Encode(name)).Append("</li>");
            html.Append("</ul>");
        }
        if (character.Contacts.Count > 0)
        {
            html.Append("<h2>Contacts</h2><table><tr><th>Name</th><th>Role / location</th><th>Connection</th><th>Loyalty</th></tr>");
            foreach (var c in character.Contacts)
                html.Append("<tr><td>").Append(Encode(c.Name)).Append("</td><td>").Append(Encode($"{c.Role} / {c.Location}"))
                    .Append("</td><td>").Append(c.Connection).Append("</td><td>").Append(c.Loyalty).Append("</td></tr>");
            html.Append("</table>");
        }
        html.Append("<h2>Career history</h2><table><tr><th>Date</th><th>Currency</th><th>Amount</th><th>Reason</th></tr>");
        foreach (var t in character.Transactions.OrderByDescending(t => t.Date))
            html.Append("<tr><td>").Append(Encode(t.Date)).Append("</td><td>").Append(Encode(t.Currency)).Append("</td><td>")
                .Append(Encode(t.Amount.ToString(System.Globalization.CultureInfo.InvariantCulture))).Append("</td><td>").Append(Encode(t.Reason)).Append("</td></tr>");
        return html.Append("</table><p>Chummer Offline development preview — summary sheet</p></body></html>").ToString();
    }
}

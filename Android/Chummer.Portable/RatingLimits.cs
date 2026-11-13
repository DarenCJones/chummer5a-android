// SPDX-License-Identifier: GPL-3.0-or-later
using System.Xml.Linq;

namespace Chummer.Portable;
public sealed partial class CharacterDocument
{
    internal IReadOnlyList<string> QualityRatingChoices(string name)
    {
        RequireDraft();return name switch {
            "Aptitude"=>SkillNodes.Select(s=>s.Element("name")!.Value).OrderBy(s=>s).ToArray(),
            "Loss of Confidence"=>SkillNodes.Where(s=>DraftSkillRating(s.Element("suid")!.Value)>=4).Select(s=>s.Element("name")!.Value).OrderBy(s=>s).ToArray(),
            "Exceptional Attribute"=>DraftAttributes.Where(a=>a.Name!="EDG").Select(a=>a.Name).ToArray(),_=>[]};
    }
    public int DraftSkillMaximum(string name)
    {
        RequireDraft();if(!SkillNodes.Any(s=>(string?)s.Element("name")==name))throw new ArgumentException("Unknown skill.");
        return 6+(int)DraftEffect("Skill",name,"max");
    }
    internal void ValidateDraftRatingLimits()
    {
        var attributes=DraftAttributes;
        if(attributes.Any(a=>a.Rating<a.Minimum || a.Rating>a.Maximum))throw new ArgumentException("Lower the attribute before removing its maximum-rating quality.");
        if(attributes.Count(a=>StandardAttributeNames.Contains(a.Name) && a.Rating==a.Maximum)>1)throw new ArgumentException("Only one physical or mental attribute may start at its natural maximum.");
        foreach(var skill in SkillNodes)
            if(DraftSkillRating(skill.Element("suid")!.Value)>DraftSkillMaximum(skill.Element("name")!.Value))
                throw new ArgumentException("Lower the skill rating before removing Aptitude.");
        foreach(var quality in SelectedQualities.Where(q=>q.Name=="Loss of Confidence"))
            if(!SkillNodes.Any(s=>(string?)s.Element("name")==quality.Extra && DraftSkillRating(s.Element("suid")!.Value)>=4))
                throw new ArgumentException("Loss of Confidence requires its selected skill to remain at rating 4 or higher.");
        ValidatePowerConstraints();
    }
}

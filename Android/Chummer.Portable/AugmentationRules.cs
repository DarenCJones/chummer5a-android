// SPDX-License-Identifier: GPL-3.0-or-later
using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace Chummer.Portable;

public sealed record AugmentationEntry(string Id,string Name,string Kind,int Minimum,int Maximum,string Page);
public sealed record AugmentationQuote(decimal Cost,decimal Essence,int Availability,string Legality);
public sealed record InstalledAugmentation(string Id,string Name,string Kind,int Rating,string Grade,decimal Cost,decimal Essence);

/// <summary>Core, non-modular implants with supported permanent effects. Wireless bonuses remain inactive.</summary>
public sealed class AugmentationRules
{
    private readonly Dictionary<string,(string Kind,XElement Data)> items = new();
    private readonly Dictionary<string,XElement> grades;
    private static readonly HashSet<string> SupportedNames = ["Dermal Plating","Muscle Replacement","Wired Reflexes","Reaction Enhancers",
        "Bone Lacing (Plastic)","Bone Lacing (Aluminum)","Bone Lacing (Titanium)","Muscle Augmentation","Muscle Toner","Synaptic Booster",
        "Cerebral Booster","Orthoskin","Bone Density Augmentation","Enhanced Articulation","Pathogenic Defense","Toxin Extractor"];
    private static XDocument Read(Stream data)
    {
        using var reader=XmlReader.Create(data,new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=32*1024*1024});
        var xml=XDocument.Load(reader); if(xml.Root?.Name!="chummer")throw new InvalidDataException("Invalid implant catalog."); return xml;
    }
    public AugmentationRules(Stream cyberware,Stream bioware)
    {
        var cyber=Read(cyberware); var bio=Read(bioware);
        grades=cyber.Root!.Element("grades")!.Elements("grade").Where(g=>(string?)g.Element("name") is "Standard" or "Used" or "Alphaware")
            .ToDictionary(g=>g.Element("name")!.Value,g=>new XElement(g));
        foreach(var (kind,xml,collection,node) in new[]{("Cyberware",cyber,"cyberwares","cyberware"),("Bioware",bio,"biowares","bioware")})
            foreach(var e in xml.Root!.Element(collection)!.Elements(node).Where(e=>(string?)e.Element("source")=="SR5" && SupportedNames.Contains(e.Element("name")!.Value)))
                items.Add(e.Element("id")!.Value,(kind,new XElement(e)));
    }
    public IReadOnlyList<string> Grades=>grades.Keys.ToArray();
    public IReadOnlyList<AugmentationEntry> Catalog=>items.Select(p=>new AugmentationEntry(p.Key,p.Value.Data.Element("name")!.Value,p.Value.Kind,
        (int?)p.Value.Data.Element("rating") is null?0:(int?)p.Value.Data.Element("minrating")??1,(int?)p.Value.Data.Element("rating")??0,p.Value.Data.Element("page")!.Value)).OrderBy(e=>e.Name).ToArray();
    // Catalog expressions are deliberately restricted to simple numeric arithmetic and FixedValues.
    internal static string SelectValue(string expression,int rating)
    {
        if(!expression.StartsWith("FixedValues(",StringComparison.Ordinal))return expression;
        if(!expression.EndsWith(')'))throw new InvalidDataException("Invalid fixed-value expression.");
        var values=expression[12..^1].Split(',');
        if(rating<1 || rating>values.Length)throw new ArgumentOutOfRangeException(nameof(rating));
        return values[rating-1].Trim();
    }
    internal static decimal Evaluate(string expression,int rating)
    {
        var text=SelectValue(expression,rating).Replace("Rating",rating.ToString(CultureInfo.InvariantCulture)).Trim();
        if(decimal.TryParse(text,NumberStyles.Number,CultureInfo.InvariantCulture,out var number))return number;
        var product=Regex.Match(text,@"^([0-9]+(?:\.[0-9]+)?)\s*\*\s*([0-9]+(?:\.[0-9]+)?)$");
        if(product.Success)return decimal.Parse(product.Groups[1].Value,CultureInfo.InvariantCulture)*decimal.Parse(product.Groups[2].Value,CultureInfo.InvariantCulture);
        var difference=Regex.Match(text,@"^(\d+)\s*-\s*(\d+)$");
        if(difference.Success)return int.Parse(difference.Groups[1].Value)-int.Parse(difference.Groups[2].Value);
        throw new InvalidDataException("This implant expression needs additional rule support.");
    }
    public AugmentationQuote Quote(string id,int rating,string grade,CharacterDocument? character=null)
    {
        if(!items.TryGetValue(id,out var item) || !grades.TryGetValue(grade,out var g))throw new ArgumentException("Choose a supported implant and grade.");
        var entry=Catalog.Single(e=>e.Id==id); var e=item.Data;
        if(item.Kind=="Bioware" && character?.DraftBiowareDisabled==true)throw new ArgumentException("A selected quality prevents bioware installation.");
        if(rating<entry.Minimum || rating>entry.Maximum)throw new ArgumentException("Rating is outside this implant's limits.");
        if(e.Element("bannedgrades")?.Elements("grade").Any(n=>n.Value==grade)==true)throw new ArgumentException("This grade is not available for this implant.");
        var availability=SelectValue(e.Element("avail")!.Value,rating); var legality=Regex.Match(availability,@"[RF]$").Value;
        var expression=legality.Length==0?availability:availability[..^1]; expression=expression.Trim().Trim('(',')');
        var avail=checked((int)Evaluate(expression,rating)+(int)g.Element("avail")!);
        if(avail>12)throw new ArgumentException("This implant/grade exceeds standard creation availability 12.");
        return new AugmentationQuote(Evaluate(e.Element("cost")!.Value,rating)*(decimal)g.Element("cost")!,
            decimal.Round(Evaluate(e.Element("ess")!.Value,rating)*(decimal)g.Element("ess")!*(item.Kind=="Cyberware"?character?.DraftCyberwareEssenceMultiplier??1:1),2,MidpointRounding.AwayFromZero),Math.Max(0,avail),legality);
    }
    public void Purchase(CharacterDocument character,string id,int rating,string grade)
    {
        _=character.DraftAttributes;
        var quote=Quote(id,rating,grade,character);var item=items[id];var data=item.Data;
        if(character.LearnedPowers.Any(p=>p.Name=="Improved Reflexes") && data.Element("name")!.Value is "Wired Reflexes" or "Reaction Enhancers" or "Synaptic Booster")
            throw new InvalidOperationException("Combining Improved Reflexes with initiative implants needs additional interaction support.");
        if(character.Nuyen<quote.Cost)throw new ArgumentException("Not enough Nuyen.");
        if(character.InstalledAugmentations.Any(a=>a.Name==data.Element("name")!.Value))throw new ArgumentException("This implant is already installed. Remove it before changing its rating or grade.");
        var guid=Guid.NewGuid().ToString("D");
        var node=new XElement("cyberware",new XElement("guid",guid),new XElement("sourceid",id),new XElement("improvementsource",item.Kind),
            new XElement("grade",grade),new XElement("rating",rating),new XElement("minrating",(int?)data.Element("minrating")??(rating==0?0:1)),
            new XElement("maxrating",(int?)data.Element("rating")??0),new XElement("extra",""),new XElement("parentid",""),
            new XElement("wirelesson","False"),new XElement("modularpluggedin","True"),new XElement("essdiscount",0),
            new XElement("androidbuiltin","True"),new XElement("androidpurchasecost",quote.Cost),new XElement("androidessencecost",quote.Essence),new XElement("notes",""));
        foreach(var name in new[]{"name","category","ess","capacity","avail","cost","source","page","bonus"})
            if(data.Element(name) is {} field)node.Add(new XElement(field));
        var updated=character.Clone();updated.AddDraftAugmentation(node,Effects(data.Element("bonus"),guid,item.Kind,rating),quote.Cost);
        updated.RefreshDraftEssenceEffects();Validate(updated);updated.ValidateAugmentationEffects();character.ReplaceDraftWith(updated);
    }
    public void Validate(CharacterDocument character)
    {
        var installed=character.InstalledAugmentations;
        foreach(var implant in installed)
        {
            var data=items.Values.Single(p=>p.Data.Element("name")!.Value==implant.Name).Data;
            foreach(var conflict in data.Element("forbidden")?.Descendants().Where(e=>e.Name.LocalName is "cyberware" or "bioware")??[])
                if(installed.Any(a=>a.Name==conflict.Value))throw new ArgumentException($"{implant.Name} conflicts with {conflict.Value}.");
        }
        if(installed.Any(a=>a.Name=="Bone Density Augmentation") && installed.Any(a=>a.Name.StartsWith("Bone Lacing (",StringComparison.Ordinal)))
            throw new ArgumentException("Bone Density Augmentation conflicts with bone lacing.");
    }
    public void Remove(CharacterDocument character,string id)
    {
        var updated=character.Clone();updated.RemoveDraftAugmentation(id);updated.RefreshDraftEssenceEffects();Validate(updated);updated.ValidateAugmentationEffects();character.ReplaceDraftWith(updated);
    }
    private static XElement[] Effects(XElement? bonus,string guid,string kind,int rating)
    {
        var effects=new List<XElement>();
        foreach(var b in bonus?.Elements()??[])
        {
            if(b.Name=="disablequality")continue;
            var attribute=b.Name=="specificattribute";var skill=b.Name=="specificskill";
            var type=b.Name.LocalName switch {"specificattribute"=>"Attribute","specificskill"=>"Skill","armor"=>"Armor","initiativepass"=>"InitiativeDice",
                "damageresistance"=>"DamageResistance","unarmeddv"=>"UnarmedDV","unarmeddvphysical"=>"UnarmedDVPhysical","physicallimit"=>"PhysicalLimit",
                "pathogencontactresist"=>"PathogenContactResist","pathogeningestionresist"=>"PathogenIngestionResist","pathogeninhalationresist"=>"PathogenInhalationResist","pathogeninjectionresist"=>"PathogenInjectionResist",
                "toxincontactresist"=>"ToxinContactResist","toxiningestionresist"=>"ToxinIngestionResist","toxininhalationresist"=>"ToxinInhalationResist","toxininjectionresist"=>"ToxinInjectionResist",
                _=>throw new InvalidDataException($"Unsupported implant effect: {b.Name}")};
            var name=attribute||skill?b.Element("name")!.Value:type=="PhysicalLimit"?"Physical":"";
            var value=type=="UnarmedDVPhysical"?0:Evaluate(attribute?b.Element("val")!.Value:skill?b.Element("bonus")!.Value:b.Value,rating);
            var unique=(string?)b.Attribute("precedence") is {} precedence?"precedence"+precedence:
                (string?)b.Attribute("group") is {} group?"group"+group:(string?)bonus!.Attribute("unique")??"";
            var improvement=CreationRules.BuiltInImprovement(type,kind,guid,name,attribute?0:value,unique);
            if(attribute)improvement.SetElementValue("aug",value);effects.Add(improvement);
        }
        return effects.ToArray();
    }
}

public sealed partial class CharacterDocument
{
    public IReadOnlyList<InstalledAugmentation> InstalledAugmentations
    {
        get {RequireDraft();return (document.Root!.Element("cyberwares")?.Elements("cyberware")??[]).Select(a=>new InstalledAugmentation(a.Element("guid")!.Value,
            a.Element("name")!.Value,a.Element("improvementsource")!.Value,(int)a.Element("rating")!,a.Element("grade")!.Value,(decimal)a.Element("androidpurchasecost")!,CurrentImplantEssence(a))).ToArray();}
    }
    internal bool DraftBiowareDisabled=>document.Root!.Element("improvements")!.Elements().Any(e=>(string?)e.Element("improvementttype")=="DisableBioware" && (int?)e.Element("enabled")==1);
    internal decimal DraftCyberwareEssenceMultiplier=>document.Root!.Element("improvements")!.Elements().Where(e=>(string?)e.Element("improvementttype")=="CyberwareTotalEssMultiplier" && (int?)e.Element("enabled")==1)
        .Aggregate(1m,(factor,e)=>factor*(decimal)e.Element("val")!/100m);
    private decimal CurrentImplantEssence(XElement item)
    {
        var grade=(string?)item.Element("grade") switch {"Standard"=>1m,"Used"=>1.25m,"Alphaware"=>0.8m,_=>throw new InvalidDataException("Unsupported implant grade.")};
        return decimal.Round(AugmentationRules.Evaluate(item.Element("ess")!.Value,(int)item.Element("rating")!)*grade*
            ((string?)item.Element("improvementsource")=="Cyberware"?DraftCyberwareEssenceMultiplier:1m),2,MidpointRounding.AwayFromZero);
    }
    public decimal DraftEssence=>6-InstalledAugmentations.Sum(a=>a.Essence);
    internal void AddDraftAugmentation(XElement node,XElement[] effects,decimal cost)
    {
        RequireDraft();if(document.Root!.Element("cyberwares") is null)document.Root.Add(new XElement("cyberwares"));
        document.Root.Element("cyberwares")!.Add(node);document.Root.Element("improvements")!.Add(effects);SetField("nuyen",(Nuyen-cost).ToString(CultureInfo.InvariantCulture));
    }
    internal void ValidateAugmentationEffects()
    {
        if(DraftEssence<=0)throw new ArgumentException("Implants must leave Essence above zero.");
        if(DraftBiowareDisabled && InstalledAugmentations.Any(a=>a.Kind=="Bioware"))throw new ArgumentException("Remove bioware before selecting a quality that prevents bioware installation.");
        foreach(var attribute in StandardAttributeNames)if(DraftEffect("Attribute",attribute,"aug")>4)throw new ArgumentException("Attribute augmentations cannot exceed +4.");
        foreach(var attribute in DraftAttributes)
        {
            if(attribute.Rating>attribute.Maximum)throw new ArgumentException("Reduce the special attribute allocation before installing this much Essence cost.");
            if(attribute.Name is "MAG" or "RES" && attribute.Rating<1)throw new InvalidOperationException("Burning Magic or Resonance to zero is not supported yet. Raise the attribute or reduce the implant Essence cost.");
        }
        ValidatePowerConstraints();
    }
    private DraftAttribute ReadDraftAttribute(XElement node)
    {
        var name=node.Element("name")!.Value;
        var essence=document.Root!.Element("improvements")!.Elements("improvement").Where(e=>(string?)e.Element("androidbuiltin")=="True" &&
            (string?)e.Element("improvementttype")=="Attribute" && string.IsNullOrEmpty((string?)e.Element("condition")) &&
            (string?)e.Element("improvedname")==name && (int?)e.Element("enabled")==1).ToArray();
        var minimum=Math.Max(0,AttributeInt(node,"metatypemin")+essence.Sum(e=>(int?)e.Element("min")??0));
        var maximum=Math.Max(minimum,AttributeInt(node,"metatypemax")+essence.Sum(e=>(int?)e.Element("max")??0));
        return new DraftAttribute(name,minimum,maximum,minimum+AttributeInt(node,"base")+AttributeInt(node,"karma"),AttributeInt(node,"karma"));
    }
    internal void RefreshDraftEssenceEffects()
    {
        RequireDraft();var improvements=document.Root!.Element("improvements")!;
        foreach(var implant in document.Root.Element("cyberwares")?.Elements("cyberware")??[])implant.SetElementValue("androidessencecost",CurrentImplantEssence(implant));
        improvements.Elements().Where(e=>(string?)e.Element("improvementsource")=="EssenceLossChargen" && (string?)e.Element("androidbuiltin")=="True").Remove();
        var loss=(int)decimal.Ceiling(6-DraftEssence);
        foreach(var attribute in new[]{"MAG","MAGAdept","RES"})
        {
            if(attribute=="RES"?Field("resenabled")!="True":Field("magenabled")!="True")continue;
            if(loss>0)
            {
                var effect=CreationRules.BuiltInImprovement("Attribute","EssenceLossChargen","",attribute,0);
                effect.SetElementValue("min",-loss);effect.SetElementValue("max",-loss);improvements.Add(effect);
            }
            var node=AttributeNodes.Single(e=>(string?)e.Element("name")==attribute);node.SetElementValue("totalvalue",ReadDraftAttribute(node).Rating);
        }
        RepriceDraftAttributeKarma();
    }
    internal void RemoveDraftAugmentation(string id)
    {
        var implant=InstalledAugmentations.SingleOrDefault(a=>a.Id==id)??throw new ArgumentException("Unknown implant.");
        document.Root!.Element("cyberwares")!.Elements().Single(a=>(string?)a.Element("guid")==id).Remove();
        document.Root.Element("improvements")!.Elements().Where(e=>(string?)e.Element("sourcename")==id && (string?)e.Element("improvementsource") is "Cyberware" or "Bioware").Remove();
        SetField("nuyen",(Nuyen+implant.Cost).ToString(CultureInfo.InvariantCulture));
    }
}

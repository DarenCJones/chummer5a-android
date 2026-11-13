// SPDX-License-Identifier: GPL-3.0-or-later
using System.Xml;
using System.Xml.Linq;
using System.Globalization;

namespace Chummer.Portable;

/// <summary>Preserves the full upstream XML tree, including fields not yet ported.</summary>
public sealed partial class CharacterDocument
{
    private readonly XDocument document;
    private CharacterDocument(XDocument document) => this.document = document;

    public string Name => Field("name");
    public string Alias => Field("alias");
    public string Metatype => Field("metatype");
    public CharacterDocument Clone() => new(new XDocument(document));
    public string Field(string name) => document.Root!.Element(name)?.Value ??
        (name == "gender" ? document.Root.Element("sex")?.Value : null) ?? "";

    private static readonly HashSet<string> EditableFields = new(StringComparer.Ordinal)
    { "name", "alias", "playername", "age", "gender", "height", "weight", "eyes", "hair", "skin", "concept", "description", "background" };

    public void SetProfile(IReadOnlyDictionary<string, string> fields)
    {
        if (fields.Keys.Any(k => !EditableFields.Contains(k)))
            throw new ArgumentException("Unsupported profile field.");
        foreach (var (key, value) in fields) SetField(key, value);
        if (fields.TryGetValue("gender", out var gender) && document.Root!.Element("sex") is not null) SetField("sex", gender);
    }

    private void SetField(string name, string value) => document.Root!.SetElementValue(name, value);
    private decimal Number(string name) => decimal.TryParse(Field(name), NumberStyles.Number,
        CultureInfo.InvariantCulture, out var value) ? value : 0;
    public decimal Nuyen => Number("nuyen");
    public decimal Karma => Number("karma");
    public bool IsCareer => bool.TryParse(Field("created"), out var value) && value;
    public int PhysicalDamage => (int)Number("physicalcmfilled");
    public int StunDamage => (int)Number("stuncmfilled");

    public void SetDamage(int physical, int stun)
    {
        if (physical < 0 || physical > 100 || stun < 0 || stun > 100)
            throw new ArgumentOutOfRangeException(nameof(physical), "Damage must be between 0 and 100.");
        SetField("physicalcmfilled", physical.ToString(CultureInfo.InvariantCulture));
        SetField("stuncmfilled", stun.ToString(CultureInfo.InvariantCulture));
    }

    public IReadOnlyList<CareerTransaction> Transactions =>
        (document.Root!.Element("expenses")?.Elements("expense") ?? [])
        .Select(e => new CareerTransaction((string?)e.Element("guid") ?? "",
            (string?)e.Element("date") ?? "", (string?)e.Element("type") ?? "",
            decimal.TryParse((string?)e.Element("amount"), NumberStyles.Number, CultureInfo.InvariantCulture, out var amount) ? amount : 0,
            (string?)e.Element("reason") ?? "")).ToArray();

    /// <summary>Uses the desktop manual-add/subtract expense schema. Never buys or upgrades an item.</summary>
    public void AddTransaction(string currency, decimal amount, string reason, DateTime date)
    {
        if (!IsCareer) throw new InvalidOperationException("Transactions require a character in career mode.");
        if (currency is not ("Karma" or "Nuyen")) throw new ArgumentException("Choose Karma or Nuyen.");
        if (amount == 0 || string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("Enter a nonzero amount and a reason.");
        if (currency == "Karma" && decimal.Truncate(amount) != amount) throw new ArgumentException("Karma must be a whole number.");
        if (currency == "Nuyen" && decimal.Round(amount, 3) != amount) throw new ArgumentException("Nuyen supports at most three decimal places.");
        var field = currency == "Karma" ? "karma" : "nuyen";
        var balance = checked(Number(field) + amount);
        if (balance < 0) throw new InvalidOperationException("The transaction would make the balance negative.");
        var manual = amount > 0 ? "ManualAdd" : "ManualSubtract";
        var expense = new XElement("expense",
            new XElement("guid", Guid.NewGuid().ToString("D")),
            new XElement("date", date.ToString("s", CultureInfo.InvariantCulture)),
            new XElement("amount", amount.ToString(CultureInfo.InvariantCulture)),
            new XElement("reason", reason.Trim()), new XElement("type", currency),
            new XElement("refund", "False"), new XElement("forcecareervisible", "False"),
            new XElement("undo", new XElement("karmatype", currency == "Karma" ? manual : "ImproveAttribute"),
                new XElement("nuyentype", currency == "Nuyen" ? manual : "AddCyberware"),
                new XElement("objectid", ""), new XElement("qty", "0"), new XElement("extra", "")));
        var expenses = document.Root!.Element("expenses");
        if (expenses is null) { expenses = new XElement("expenses"); document.Root.Add(expenses); }
        // Desktop sorts expense records by date on load; preserve existing records untouched.
        expenses.Add(expense);
        SetField(field, balance.ToString(CultureInfo.InvariantCulture));
    }

    public IReadOnlyList<string> OwnedItems(string collection, string item) =>
        (document.Root!.Element(collection)?.Elements(item) ?? [])
        .Select(e => (string?)e.Element("name") ?? (string?)e.Element("english") ?? item).ToArray();

    public string Summary() => $"{Name}\nAlias: {Alias}\nMetatype: {Metatype}\nTalent: {Field("prioritytalent")}\nMode: {(IsCareer ? "Career" : "Creation")}\nKarma: {Karma}\nNuyen: {Nuyen}\nPhysical damage: {PhysicalDamage}\nStun damage: {StunDamage}";

    public static CharacterDocument Load(Stream source)
    {
        using var reader = XmlReader.Create(source, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = 64 * 1024 * 1024,
            CloseInput = false
        });
        var xml = XDocument.Load(reader, LoadOptions.PreserveWhitespace);
        if (xml.Root?.Name != "character")
            throw new InvalidDataException("Expected an uncompressed .chum5 character file.");
        return new CharacterDocument(xml);
    }

    public void Save(Stream destination) => document.Save(destination, SaveOptions.DisableFormatting);
}

public sealed record CareerTransaction(string Id, string Date, string Currency, decimal Amount, string Reason);

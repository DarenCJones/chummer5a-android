// SPDX-License-Identifier: GPL-3.0-or-later
using System.Xml.Linq;

namespace Chummer.Portable;

public sealed record ContactRecord(string Id, string Name, string Role, string Location, int Connection, int Loyalty);

public sealed partial class CharacterDocument
{
    public IReadOnlyList<ContactRecord> Contacts => (document.Root!.Element("contacts")?.Elements("contact") ?? [])
        .Select(c => new ContactRecord((string?)c.Element("guid") ?? "", (string?)c.Element("name") ?? "",
            (string?)c.Element("role") ?? "", (string?)c.Element("location") ?? "",
            (int?)c.Element("connection") ?? 0, (int?)c.Element("loyalty") ?? 0)).ToArray();
    public int DraftContactPointsRemaining => DraftStats().ContactPoints - Contacts.Sum(c => c.Connection + c.Loyalty);
    public void SetDraftContact(string? id, string name, string role, string location, int connection, int loyalty)
    {
        RequireDraft();
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Enter a contact name.");
        if (connection is < 1 or > 6 || loyalty is < 1 or > 6 || connection + loyalty > 7)
            throw new ArgumentException("At standard creation, connection and loyalty must be 1–6 and total no more than 7 per contact.");
        var records = Contacts;
        if (id is not null && !records.Any(c => c.Id == id)) throw new ArgumentException("Unknown contact.");
        if (records.Where(c => c.Id != id).Sum(c => c.Connection + c.Loyalty) + connection + loyalty > DraftStats().ContactPoints)
            throw new ArgumentException("Not enough contact points.");
        var parent = document.Root!.Element("contacts");
        var old = parent?.Elements("contact").SingleOrDefault(c => (string?)c.Element("guid") == id);
        if (old is not null && (old.Element("readonly") is not null || new[] { "free", "group", "family", "blackmail" }
            .Any(flag => string.Equals((string?)old.Element(flag), "True", StringComparison.OrdinalIgnoreCase))))
            throw new InvalidOperationException("This contact has special rules that the draft editor does not support.");
        var contact = old is not null ? new XElement(old) : new XElement("contact",
            new XElement("guid", Guid.NewGuid().ToString("D")), new XElement("type", "Contact"),
            new XElement("free", "False"), new XElement("group", "False"), new XElement("family", "False"),
            new XElement("blackmail", "False"), new XElement("groupenabled", "True"),
            new XElement("mainmugshotindex", -1), new XElement("mugshots"));
        contact.SetElementValue("name", name.Trim()); contact.SetElementValue("role", role);
        contact.SetElementValue("location", location); contact.SetElementValue("connection", connection); contact.SetElementValue("loyalty", loyalty);
        if (parent is null) { parent = new XElement("contacts"); document.Root.Add(parent); }
        if (old is null) parent.Add(contact); else old.ReplaceWith(contact);
    }
    public void RemoveDraftContact(string id)
    {
        RequireDraft();
        var contact = document.Root!.Element("contacts")?.Elements("contact").SingleOrDefault(c => (string?)c.Element("guid") == id)
            ?? throw new ArgumentException("Unknown contact.");
        if (contact.Element("readonly") is not null) throw new InvalidOperationException("This contact is read-only.");
        contact.Remove();
    }
}

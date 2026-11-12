// SPDX-License-Identifier: GPL-3.0-or-later
using System.Xml;
using System.Xml.Linq;

namespace Chummer.Portable;

public sealed record CatalogEntry(string Id, string Name, string Source, string Page, string Details = "");

/// <summary>Reads bundled upstream data. This does not implement rule calculations or custom-data merging.</summary>
public static class RuleCatalog
{
    public static IReadOnlyList<CatalogEntry> LoadAll(Stream source)
    {
        using var reader = XmlReader.Create(source, new XmlReaderSettings
        { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 32 * 1024 * 1024 });
        var document = XDocument.Load(reader);
        if (document.Root?.Name != "chummer") throw new InvalidDataException("Invalid rules data.");
        return document.Root.Elements().SelectMany(c => c.Elements())
            .Where(e => e.Element("name") is not null)
            .Select(e => new CatalogEntry((string?)e.Element("id") ?? "", e.Element("name")!.Value,
                (string?)e.Element("source") ?? "", (string?)e.Element("page") ?? "",
                string.Join("; ", e.Elements().Where(f => !f.HasElements && f.Name.LocalName is not ("id" or "name"))
                    .Select(f => $"{f.Name.LocalName}: {f.Value}")))).ToArray();
    }
    public static IReadOnlyList<CatalogEntry> Load(Stream source, string collection, string item)
    {
        using var reader = XmlReader.Create(source, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null,
            MaxCharactersInDocument = 32 * 1024 * 1024
        });
        var document = XDocument.Load(reader);
        if (document.Root?.Name != "chummer") throw new InvalidDataException("Invalid rules data.");
        var entries = document.Root.Element(collection)?.Elements(item)
            ?? throw new InvalidDataException($"Missing catalog collection: {collection}");
        return entries.Select(e => new CatalogEntry(
            (string?)e.Element("id") ?? "", (string?)e.Element("name") ?? "",
            (string?)e.Element("source") ?? "", (string?)e.Element("page") ?? "")).ToArray();
    }
}

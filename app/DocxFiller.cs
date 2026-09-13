using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace WordMaker;

/// <summary>
/// Fills Word mail-merge templates (MERGEFIELD constructs) with values,
/// writing out a new .docx. Works directly on the OOXML so no external
/// packages are needed.
/// </summary>
public static class DocxFiller
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    // Matches both `MERGEFIELD "Name"` and `MERGEFIELD Name` instruction forms.
    private static readonly Regex MergeFieldName = new(@"MERGEFIELD\s+""?([A-Za-z_0-9]+)""?", RegexOptions.Compiled);

    public static byte[] LoadEmbeddedTemplate()
    {
        var asm = typeof(DocxFiller).Assembly;
        var name = asm.GetManifestResourceNames().FirstOrDefault(n =>
            n.EndsWith("contract.docx", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("Embedded template contract.docx not found.");
        using var stream = asm.GetManifestResourceStream(name)!;
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    public static byte[] Fill(byte[] template, IReadOnlyDictionary<string, string> values)
    {
        using var src = new MemoryStream(template);
        using var archive = new ZipArchive(src, ZipArchiveMode.Read);

        var docEntry = archive.GetEntry("word/document.xml")
            ?? throw new InvalidDataException("word/document.xml not found in template.");

        XDocument doc;
        using (var s = docEntry.Open())
        {
            doc = XDocument.Load(s);
        }

        ReplaceComplexFields(doc, values);
        ReplaceSimpleFields(doc, values);

        using var outMs = new MemoryStream();
        using (var outArchive = new ZipArchive(outMs, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var entry in archive.Entries)
            {
                var newEntry = outArchive.CreateEntry(entry.FullName, CompressionLevel.Optimal);
                using var os = newEntry.Open();
                if (entry.FullName == "word/document.xml")
                {
                    doc.Save(os);
                }
                else
                {
                    using var es = entry.Open();
                    es.CopyTo(os);
                }
            }
        }
        return outMs.ToArray();
    }

    /// <summary>
    /// Extracts the visible paragraph texts from a .docx, skipping empty
    /// paragraphs. Used for the in-app live preview.
    /// </summary>
    public static IReadOnlyList<string> ExtractParagraphs(byte[] docx)
    {
        using var src = new MemoryStream(docx);
        using var archive = new ZipArchive(src, ZipArchiveMode.Read);
        var entry = archive.GetEntry("word/document.xml")
            ?? throw new InvalidDataException("word/document.xml not found.");

        XDocument doc;
        using (var s = entry.Open())
        {
            doc = XDocument.Load(s);
        }

        var paragraphs = new List<string>();
        foreach (var p in doc.Descendants(W + "p"))
        {
            var text = string.Concat(p.Descendants(W + "t").Select(t => (string?)t ?? string.Empty));
            if (!string.IsNullOrWhiteSpace(text))
            {
                paragraphs.Add(text.Trim());
            }
        }
        return paragraphs;
    }

    /// <summary>
    /// Handles classic complex fields: a run with fldChar "begin", one or more
    /// runs with instrText (the instruction may be split across runs — common
    /// with RTL content), an optional "separate" plus cached-result runs, and a
    /// run with fldChar "end". The whole span is replaced by a single run
    /// carrying the value, with formatting copied from the field's runs.
    /// </summary>
    private static void ReplaceComplexFields(XDocument doc, IReadOnlyDictionary<string, string> values)
    {
        var allRuns = doc.Descendants(W + "r").ToList();

        int i = 0;
        while (i < allRuns.Count)
        {
            if (!IsFieldChar(allRuns[i], "begin"))
            {
                i++;
                continue;
            }

            // Find the matching "end" run, tracking nested begin/end pairs.
            int depth = 1;
            int j = i + 1;
            while (j < allRuns.Count && depth > 0)
            {
                var type = FieldCharType(allRuns[j]);
                if (type == "begin") depth++;
                else if (type == "end") depth--;
                if (depth > 0) j++;
            }

            if (depth != 0)
            {
                // Unbalanced field — leave the document alone rather than corrupt it.
                break;
            }

            var span = allRuns.GetRange(i, j - i + 1);

            // Only replace spans that live within a single paragraph.
            if (span.Select(r => r.Parent).Distinct().Count() == 1)
            {
                var instr = string.Concat(span.Elements(W + "instrText").Select(e => (string?)e ?? string.Empty));
                var match = MergeFieldName.Match(instr);
                if (match.Success && values.TryGetValue(match.Groups[1].Value, out var value))
                {
                    ReplaceSpan(span, value);
                }
            }

            i = j + 1;
        }
    }

    /// <summary>Handles the compact w:fldSimple form, for robustness.</summary>
    private static void ReplaceSimpleFields(XDocument doc, IReadOnlyDictionary<string, string> values)
    {
        foreach (var fs in doc.Descendants(W + "fldSimple").ToList())
        {
            var instr = (string?)fs.Attribute(W + "instr");
            if (instr is null) continue;

            var match = MergeFieldName.Match(instr);
            if (match.Success && values.TryGetValue(match.Groups[1].Value, out var value))
            {
                var rPr = fs.Elements(W + "r").Select(r => r.Element(W + "rPr")).FirstOrDefault(p => p != null);
                fs.ReplaceWith(MakeRun(rPr, value));
            }
        }
    }

    private static void ReplaceSpan(List<XElement> span, string value)
    {
        // Prefer the formatting of the cached-result runs (what the reader sees);
        // fall back to any run in the span that carries formatting.
        int separate = span.FindIndex(r => IsFieldChar(r, "separate"));
        XElement? rPr =
            (separate >= 0
                ? span.Skip(separate + 1).Select(r => r.Element(W + "rPr")).FirstOrDefault(p => p != null)
                : null)
            ?? span.Select(r => r.Element(W + "rPr")).FirstOrDefault(p => p != null);

        span[0].ReplaceWith(MakeRun(rPr, value));
        foreach (var run in span.Skip(1))
        {
            run.Remove();
        }
    }

    private static XElement MakeRun(XElement? rPr, string value)
    {
        var run = new XElement(W + "r");
        if (rPr is not null)
        {
            run.Add(new XElement(rPr));
        }
        var text = new XElement(W + "t", value);
        text.SetAttributeValue(XNamespace.Xml + "space", "preserve");
        run.Add(text);
        return run;
    }

    private static bool IsFieldChar(XElement run, string type) => FieldCharType(run) == type;

    private static string? FieldCharType(XElement run)
    {
        var fc = run.Element(W + "fldChar");
        return (string?)fc?.Attribute(W + "fldCharType");
    }
}

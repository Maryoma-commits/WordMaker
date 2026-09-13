namespace WordMaker;

public sealed record MergeField(string Name, string Label, string Section, bool Rtl);

/// <summary>
/// The single source of truth for the contract's merge fields.
/// Names must match the MERGEFIELD instructions in the embedded contract.docx.
/// </summary>
public static class MergeFields
{
    public const string SectionShared = "Shared";
    public const string SectionEnglish = "English";
    public const string SectionArabic = "Arabic";

    public static readonly IReadOnlyDictionary<string, string> SectionTitles = new Dictionary<string, string>
    {
        [SectionShared] = "Shared details (used in both language sections)",
        [SectionEnglish] = "English section",
        [SectionArabic] = "Arabic section — القسم العربي",
    };

    public static readonly IReadOnlyList<MergeField> All = new[]
    {
        // Shared — these fields appear in both the English and the Arabic halves of the contract
        new MergeField("M_700_No", "Unified / M-700 No.", SectionShared, false),
        new MergeField("Passport_No", "Passport No.", SectionShared, false),
        new MergeField("Visa_No", "Work Visa No.", SectionShared, false),
        new MergeField("Border_No", "Border No.", SectionShared, false),

        // English section
        new MergeField("English_Company_Name", "Company name (English)", SectionEnglish, false),
        new MergeField("English_Employee_Name", "Employee name (English)", SectionEnglish, false),
        new MergeField("Nationality_E", "Nationality (English)", SectionEnglish, false),
        new MergeField("English_Occupation", "Occupation (English)", SectionEnglish, false),

        // Arabic section
        new MergeField("Arabic_Company_name_", "Company name (Arabic)", SectionArabic, true),
        new MergeField("Arabic_Employee_Name", "Employee name (Arabic)", SectionArabic, true),
        new MergeField("Nationality_A", "Nationality (Arabic)", SectionArabic, true),
        new MergeField("Arabic_Occupation", "Occupation (Arabic)", SectionArabic, true),
    };
}

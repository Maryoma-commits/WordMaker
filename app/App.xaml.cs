using Microsoft.UI.Xaml;

namespace WordMaker;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Headless mode used for automated verification:
    ///   WordMaker.exe --selftest [output.docx]
    /// Fills every merge field with a known sample value and writes the
    /// result without opening the window. Errors go to selftest-error.txt.
    /// </summary>
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var argv = Environment.GetCommandLineArgs();
        if (argv.Length > 1 && argv[1].Equals("--selftest", StringComparison.OrdinalIgnoreCase))
        {
            RunSelfTest(argv.Length > 2 ? argv[2] : "selftest-output.docx");
            Exit();
            return;
        }

        var window = new MainWindow();
        window.Activate();
    }

    private static void RunSelfTest(string outputPath)
    {
        try
        {
            var values = MergeFields.All.ToDictionary(f => f.Name, f => "ZZ_TEST_" + f.Name);
            var template = DocxFiller.LoadEmbeddedTemplate();
            File.WriteAllBytes(outputPath, DocxFiller.Fill(template, values));

            // Round-trip a company profile through JSON (temp path, so the
            // real store next to the exe is not touched).
            var storePath = Path.Combine(Path.GetTempPath(), "wordmaker-selftest-companies.json");
            var profile = new CompanyProfile
            {
                CompanyNameEnglish = "ZZ_TEST_CO",
                CompanyNameArabic = "شركة اختبار",
                M700No = "7000000000",
                BorderNo = "9999999999",
                VisaNo = "8888888888",
            };
            CompanyProfiles.Save(new List<CompanyProfile> { profile }, storePath);
            var loaded = CompanyProfiles.Load(storePath);
            File.Delete(storePath);
            if (loaded.Count != 1 || loaded[0].CompanyNameEnglish != "ZZ_TEST_CO"
                || loaded[0].VisaNo != "8888888888")
            {
                throw new InvalidOperationException("Company profile round-trip failed.");
            }
        }
        catch (Exception ex)
        {
            File.WriteAllText(Path.ChangeExtension(outputPath, ".error.txt"), ex.ToString());
        }
    }
}

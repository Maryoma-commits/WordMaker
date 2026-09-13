using System.IO;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;

namespace WordMaker;

public sealed partial class MainWindow : Window
{
    private readonly Dictionary<MergeField, TextBox> _inputs = new();
    private byte[]? _template;
    private string _englishText = string.Empty;
    private string _arabicText = string.Empty;
    private List<CompanyProfile> _companies = new();
    private bool _loadingCompanies;

    private byte[] Template => _template ??= DocxFiller.LoadEmbeddedTemplate();

    public MainWindow()
    {
        InitializeComponent();

        Title = "WordMaker — Contract Export";

        // Replace the default title bar with our transparent one so the
        // Mica backdrop extends across the whole window. The drag region is
        // defined manually (see UpdateTitleBarDragRegion) so the Export
        // button inside the title bar stays clickable.
        ExtendsContentIntoTitleBar = true;

        // Windows 11 Mica material as the window backdrop.
        SystemBackdrop = new Microsoft.UI.Xaml.Media.MicaBackdrop();

        // Wide enough for the two-column layout, tall enough for the form.
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1120, 900));

        BuildForm();
        UpdatePreview();
        LoadCompanyList();
        SetAppIcon();

        VersionLabel.Text = $"WordMaker v{UpdateService.CurrentVersion}";

        // Caption button inset is only known once the window is shown.
        Activated += (_, _) => UpdateTitleBarDragRegion();

        // Silently check for updates once, shortly after launch.
        var autoChecked = false;
        Activated += async (_, _) =>
        {
            if (autoChecked)
            {
                return;
            }
            autoChecked = true;
            await Task.Delay(1500);
            await CheckForUpdateAsync(manual: false);
        };
    }

    // ------------------------------------------------------------------
    // Updates
    // ------------------------------------------------------------------

    private async void OnCheckForUpdates(object sender, RoutedEventArgs e) =>
        await CheckForUpdateAsync(manual: true);

    private async Task CheckForUpdateAsync(bool manual)
    {
        UpdateInfo? update;
        try
        {
            update = await UpdateService.CheckForUpdateAsync();
        }
        catch (Exception ex)
        {
            if (manual)
            {
                Show(InfoBarSeverity.Error, "Update check failed", ex.Message);
            }
            return; // Silent for the automatic startup check.
        }

        if (update is null)
        {
            if (manual)
            {
                Show(InfoBarSeverity.Success, "You're up to date",
                    $"WordMaker v{UpdateService.CurrentVersion} is the latest version.");
            }
            return;
        }

        var content = new StackPanel { Spacing = 12, MinWidth = 360 };
        content.Children.Add(new TextBlock
        {
            Text = $"WordMaker v{update.Version} is available (you have v{UpdateService.CurrentVersion}).",
            TextWrapping = TextWrapping.Wrap,
        });
        if (!string.IsNullOrWhiteSpace(update.ReleaseNotes))
        {
            var notes = update.ReleaseNotes.Trim();
            if (notes.Length > 400)
            {
                notes = notes[..400] + "…";
            }
            content.Children.Add(new TextBlock
            {
                Text = notes,
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.7,
                FontSize = 12,
            });
        }

        var dialog = new ContentDialog
        {
            Title = "Update available",
            Content = content,
            PrimaryButtonText = "Update now",
            CloseButtonText = "Later",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = Content.XamlRoot,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        await DownloadAndApplyUpdateAsync(update);
    }

    private async Task DownloadAndApplyUpdateAsync(UpdateInfo update)
    {
        var progressBar = new ProgressBar { Minimum = 0, Maximum = 100 };
        var progressText = new TextBlock();
        var progressContent = new StackPanel { Spacing = 12, MinWidth = 360 };
        progressContent.Children.Add(progressBar);
        progressContent.Children.Add(progressText);

        var progressDialog = new ContentDialog
        {
            Title = "Downloading update",
            Content = progressContent,
            XamlRoot = Content.XamlRoot,
            // No buttons — the dialog cannot be dismissed mid-download.
        };
        _ = progressDialog.ShowAsync();

        var progress = new Progress<int>(pct =>
        {
            progressBar.Value = pct;
            progressText.Text = $"{pct}%";
        });

        try
        {
            var downloadedPath = await UpdateService.DownloadAsync(update, progress);
            progressDialog.Hide();
            UpdateService.ApplyUpdate(downloadedPath);
            Close(); // The helper script takes over from here.
        }
        catch (Exception ex)
        {
            progressDialog.Hide();
            Show(InfoBarSeverity.Error, "Update failed", ex.Message);
        }
    }

    /// <summary>
    /// Unpackaged WinUI 3 windows do not inherit the exe's embedded icon for
    /// the taskbar and Alt-Tab — it must be set explicitly. The icon ships as
    /// an embedded resource and is extracted to temp (single-file exe can't
    /// serve a path to an icon that isn't on disk).
    /// </summary>
    private void SetAppIcon()
    {
        try
        {
            var icoPath = Path.Combine(Path.GetTempPath(), "WordMaker.app.ico");
            File.WriteAllBytes(icoPath, ReadResource("app.ico"));
            AppWindow.SetIcon(icoPath);

            var pngPath = Path.Combine(Path.GetTempPath(), "WordMaker.titlebar.png");
            File.WriteAllBytes(pngPath, ReadResource("titlebar-icon.png"));
            TitleBarIcon.Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(pngPath));

            // Company logo as a faint watermark behind both preview panes.
            var wmPath = Path.Combine(Path.GetTempPath(), "WordMaker.watermark.png");
            File.WriteAllBytes(wmPath, ReadResource("watermark.png"));
            var watermark = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(wmPath));
            EnglishWatermark.Source = watermark;
            ArabicWatermark.Source = watermark;
        }
        catch
        {
            // Cosmetic only — never block startup over the icon.
        }
    }

    private static byte[] ReadResource(string suffix)
    {
        var asm = typeof(MainWindow).Assembly;
        var name = asm.GetManifestResourceNames()
            .First(n => n.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
        using var stream = asm.GetManifestResourceStream(name)!;
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    // ------------------------------------------------------------------
    // Company profiles
    // ------------------------------------------------------------------

    private void LoadCompanyList()
    {
        _companies = CompanyProfiles.Load()
            .OrderBy(c => c.CompanyNameEnglish, StringComparer.OrdinalIgnoreCase)
            .ToList();

        _loadingCompanies = true;
        CompanySelector.Items.Clear();
        foreach (var company in _companies)
        {
            CompanySelector.Items.Add(company.CompanyNameEnglish);
        }
        CompanySelector.SelectedIndex = -1;
        _loadingCompanies = false;
    }

    private void OnCompanySelected(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingCompanies || CompanySelector.SelectedIndex < 0)
        {
            return;
        }

        var profile = _companies[CompanySelector.SelectedIndex];

        // Only company-level fields are loaded; employee details are left as typed.
        BoxFor("English_Company_Name").Text = profile.CompanyNameEnglish;
        BoxFor("Arabic_Company_name_").Text = profile.CompanyNameArabic;
        BoxFor("M_700_No").Text = profile.M700No;
        BoxFor("Border_No").Text = profile.BorderNo;
        BoxFor("Visa_No").Text = profile.VisaNo;

        Show(InfoBarSeverity.Informational, "Company loaded", profile.CompanyNameEnglish);
    }

    /// <summary>
    /// Opens the "Add company" dialog. Only company-level fields are entered
    /// here; the main form is filled afterwards by selecting the new profile.
    /// </summary>
    private async void OnAddCompany(object sender, RoutedEventArgs e)
    {
        var nameEl = MakeLabeledField("Company name (English)", rtl: false, out var nameBox);
        var arabicEl = MakeLabeledField("Company name (Arabic)", rtl: true, out var arabicBox);
        var m700El = MakeLabeledField("Unified / M-700 No.", rtl: false, out var m700Box);
        var borderEl = MakeLabeledField("Border No.", rtl: false, out var borderBox);
        var visaEl = MakeLabeledField("Work Visa No.", rtl: false, out var visaBox);

        var error = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed,
        };
        if (Application.Current.Resources.TryGetValue("SystemFillColorCriticalBrush", out var brush))
        {
            error.Foreground = (Microsoft.UI.Xaml.Media.Brush)brush;
        }

        var panel = new StackPanel { Spacing = 14, MinWidth = 360 };
        panel.Children.Add(nameEl);
        panel.Children.Add(arabicEl);
        panel.Children.Add(m700El);
        panel.Children.Add(borderEl);
        panel.Children.Add(visaEl);
        panel.Children.Add(error);

        var dialog = new ContentDialog
        {
            Title = "Add company",
            Content = panel,
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = Content.XamlRoot,
        };

        // Validate while the dialog is still open.
        dialog.Closing += (_, args) =>
        {
            if (args.Result != ContentDialogResult.Primary)
            {
                return;
            }
            if (string.IsNullOrWhiteSpace(nameBox.Text.Trim()))
            {
                args.Cancel = true;
                error.Text = "Company name (English) is required — it identifies the profile.";
                error.Visibility = Visibility.Visible;
                nameBox.Focus(FocusState.Programmatic);
            }
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return; // Cancelled.
        }

        var profile = new CompanyProfile
        {
            CompanyNameEnglish = nameBox.Text.Trim(),
            CompanyNameArabic = arabicBox.Text.Trim(),
            M700No = m700Box.Text.Trim(),
            BorderNo = borderBox.Text.Trim(),
            VisaNo = visaBox.Text.Trim(),
        };

        try
        {
            // Adding with an existing name overwrites that profile.
            _companies.RemoveAll(c =>
                string.Equals(c.CompanyNameEnglish, profile.CompanyNameEnglish, StringComparison.OrdinalIgnoreCase));
            _companies.Add(profile);
            CompanyProfiles.Save(_companies);

            LoadCompanyList();
            CompanySelector.SelectedIndex = _companies.FindIndex(c =>
                string.Equals(c.CompanyNameEnglish, profile.CompanyNameEnglish, StringComparison.OrdinalIgnoreCase));

            Show(InfoBarSeverity.Success, "Company saved",
                $"“{profile.CompanyNameEnglish}” was added and loaded into the form.");
        }
        catch (Exception ex)
        {
            Show(InfoBarSeverity.Error, "Could not save company", ex.Message);
        }
    }

    /// <summary>
    /// Builds a labeled input. For RTL fields the label is a separate,
    /// always-left LTR TextBlock sitting above a fully RTL TextBox — the
    /// typed Arabic flows right-to-left while the label stays aligned with
    /// every other label in the form.
    /// </summary>
    /// <summary>
    /// Opens the "Manage Companies" dialog: a scrollable list of every saved
    /// profile, each with a delete button. Changes are saved immediately.
    /// </summary>
    private async void OnManageCompanies(object sender, RoutedEventArgs e)
    {
        var listPanel = new StackPanel { Spacing = 8 };
        var emptyNote = new TextBlock
        {
            Text = "No saved companies yet. Add one with “Add company”.",
            Opacity = 0.6,
            TextWrapping = TextWrapping.Wrap,
        };

        void RebuildList()
        {
            listPanel.Children.Clear();

            if (_companies.Count == 0)
            {
                listPanel.Children.Add(emptyNote);
                return;
            }

            foreach (var company in _companies
                .OrderBy(c => c.CompanyNameEnglish, StringComparer.OrdinalIgnoreCase)
                .ToList())
            {
                var profile = company;

                var nameBlock = new StackPanel();
                nameBlock.Children.Add(new TextBlock
                {
                    Text = profile.CompanyNameEnglish,
                    TextWrapping = TextWrapping.Wrap,
                });
                if (!string.IsNullOrWhiteSpace(profile.CompanyNameArabic))
                {
                    nameBlock.Children.Add(new TextBlock
                    {
                        Text = profile.CompanyNameArabic,
                        FontSize = 12,
                        Opacity = 0.6,
                    });
                }

                var deleteButton = new Button
                {
                    Content = new FontIcon { Glyph = "\uE74D", FontSize = 14 },
                    VerticalAlignment = VerticalAlignment.Center,
                };
                ToolTipService.SetToolTip(deleteButton, $"Delete {profile.CompanyNameEnglish}");
                deleteButton.Click += (_, _) =>
                {
                    _companies.Remove(profile);
                    CompanyProfiles.Save(_companies);
                    RebuildList();
                };

                var row = new Grid { ColumnSpacing = 12 };
                row.ColumnDefinitions.Add(new ColumnDefinition());
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                Grid.SetColumn(nameBlock, 0);
                Grid.SetColumn(deleteButton, 1);
                row.Children.Add(nameBlock);
                row.Children.Add(deleteButton);

                var card = new Border
                {
                    Child = row,
                    Padding = new Thickness(12, 8, 12, 8),
                    CornerRadius = new CornerRadius(6),
                };
                if (Application.Current.Resources.TryGetValue("CardStrokeColorDefaultBrush", out var stroke))
                {
                    card.BorderBrush = (Microsoft.UI.Xaml.Media.Brush)stroke;
                    card.BorderThickness = new Thickness(1);
                }
                if (Application.Current.Resources.TryGetValue("CardBackgroundFillColorDefaultBrush", out var fill))
                {
                    card.Background = (Microsoft.UI.Xaml.Media.Brush)fill;
                }

                listPanel.Children.Add(card);
            }
        }

        RebuildList();

        var dialog = new ContentDialog
        {
            Title = "Manage Companies",
            Content = new ScrollViewer
            {
                Content = listPanel,
                MaxHeight = 400,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            },
            CloseButtonText = "Done",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = Content.XamlRoot,
        };

        await dialog.ShowAsync();

        // Refresh the dropdown to reflect any deletions.
        var wasSelected = CompanySelector.SelectedIndex;
        LoadCompanyList();
        if (wasSelected >= 0 && wasSelected < _companies.Count)
        {
            CompanySelector.SelectedIndex = -1;
        }
    }

    private static FrameworkElement MakeLabeledField(string label, bool rtl, out TextBox box)
    {
        box = new TextBox
        {
            IsSpellCheckEnabled = false,
        };

        if (!rtl)
        {
            box.Header = label;
            return box;
        }

        box.FlowDirection = FlowDirection.RightToLeft;

        // Matches the native TextBox header exactly (WinUI default style):
        // primary-text foreground, inherited 14px font, normal weight,
        // 8px gap below — see TextControlHeaderForeground/TextBoxTopHeaderMargin.
        var header = new TextBlock
        {
            Text = label,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8),
        };
        if (Application.Current.Resources.TryGetValue("SystemControlForegroundBaseHighBrush", out var brush))
        {
            header.Foreground = (Microsoft.UI.Xaml.Media.Brush)brush;
        }

        var panel = new StackPanel();
        panel.Children.Add(header);
        panel.Children.Add(box);
        return panel;
    }

    private TextBox BoxFor(string fieldName)
    {
        var match = _inputs.FirstOrDefault(kv => kv.Key.Name == fieldName);
        return match.Key is null
            ? throw new InvalidOperationException($"Unknown field: {fieldName}")
            : match.Value;
    }

    private void BuildForm()
    {
        SharedHeader.Text = MergeFields.SectionTitles[MergeFields.SectionShared];

        foreach (var group in MergeFields.All.GroupBy(f => f.Section))
        {
            if (group.Key == MergeFields.SectionShared)
            {
                // Shared fields flow directly into the responsive top grid.
                foreach (var field in group)
                {
                    SharedGrid.Children.Add(MakeFieldBox(field));
                }
            }
            else
            {
                // English and Arabic sections each get their own panel,
                // placed side by side (or stacked) by ArrangeGrid().
                var panel = new StackPanel { Spacing = 10 };
                panel.Children.Add(new TextBlock
                {
                    Text = MergeFields.SectionTitles[group.Key],
                    Style = (Style)Application.Current.Resources["SubtitleTextBlockStyle"],
                });
                foreach (var field in group)
                {
                    panel.Children.Add(MakeFieldBox(field));
                }
                LanguageGrid.Children.Add(panel);
            }
        }
    }

    private FrameworkElement MakeFieldBox(MergeField field)
    {
        var element = MakeLabeledField(field.Label, field.Rtl, out var box);
        box.TextChanged += (_, _) => UpdatePreview();
        _inputs[field] = box;
        return element;
    }

    // ------------------------------------------------------------------
    // Live preview
    // ------------------------------------------------------------------

    /// <summary>
    /// Rebuilds the preview from the actual filled document, so what you see
    /// is exactly what Export produces. Empty fields preview as "____".
    /// Each paragraph renders as its own centered TextBlock, which keeps
    /// mixed Arabic/English lines ordered correctly by the bidi algorithm.
    /// </summary>
    private void UpdatePreview()
    {
        var values = new Dictionary<string, string>();
        foreach (var (field, box) in _inputs)
        {
            values[field.Name] = string.IsNullOrWhiteSpace(box.Text) ? "____" : box.Text.Trim();
        }

        var paragraphs = DocxFiller.ExtractParagraphs(DocxFiller.Fill(Template, values));

        // The document holds the English contract first, then the Arabic one
        // starting at the "عقد عمل" heading.
        int arabicStart = paragraphs.ToList().FindIndex(p => p == "عقد عمل");
        if (arabicStart < 0)
        {
            arabicStart = paragraphs.Count;
        }

        var english = paragraphs.Take(arabicStart).ToList();
        var arabic = paragraphs.Skip(arabicStart).ToList();

        _englishText = string.Join(Environment.NewLine, english);
        _arabicText = string.Join(Environment.NewLine, arabic);

        EnglishPreviewPanel.Children.Clear();
        for (int i = 0; i < english.Count; i++)
        {
            EnglishPreviewPanel.Children.Add(MakePreviewLine(english[i], arabic: false, isTitle: i == 0));
        }

        ArabicPreviewPanel.Children.Clear();
        for (int i = 0; i < arabic.Count; i++)
        {
            ArabicPreviewPanel.Children.Add(MakePreviewLine(arabic[i], arabic: true, isTitle: i == 0));
        }
    }

    private static TextBlock MakePreviewLine(string text, bool arabic, bool isTitle)
    {
        var line = new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            FontSize = isTitle ? 18 : (arabic ? 14 : 13),
            FontWeight = isTitle ? FontWeights.Bold : FontWeights.Normal,
            IsTextSelectionEnabled = true,
        };

        if (!isTitle)
        {
            // Generous line height keeps Arabic diacritics and mixed
            // Latin/digit fragments from colliding with the line above.
            line.LineHeight = arabic ? 34 : 24;
        }

        if (arabic)
        {
            line.FlowDirection = FlowDirection.RightToLeft;
        }
        return line;
    }

    private void OnCopyEnglish(object sender, RoutedEventArgs e) =>
        CopyToClipboard(_englishText, CopyEnglishButton);

    private void OnCopyArabic(object sender, RoutedEventArgs e) =>
        CopyToClipboard(_arabicText, CopyArabicButton);

    private async void CopyToClipboard(string text, Button button)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
        package.SetText(text);
        Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);

        button.Content = "Copied ✓";
        await Task.Delay(1200);
        button.Content = "Copy";
    }

    // ------------------------------------------------------------------
    // Export
    // ------------------------------------------------------------------

    private async void OnExport(object sender, RoutedEventArgs e)
    {
        var missing = _inputs
            .Where(kv => string.IsNullOrWhiteSpace(kv.Value.Text))
            .ToList();

        if (missing.Count > 0)
        {
            Show(InfoBarSeverity.Error, "Missing details",
                "Please fill in: " + string.Join(", ", missing.Select(kv => kv.Key.Label)));
            missing[0].Value.Focus(FocusState.Programmatic);
            return;
        }

        var values = _inputs.ToDictionary(kv => kv.Key.Name, kv => kv.Value.Text.Trim());

        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
        };
        // Unpackaged WinUI 3 apps must attach the picker to a window handle.
        WinRT.Interop.InitializeWithWindow.Initialize(
            picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
        picker.FileTypeChoices.Add("Word document", new List<string> { ".docx" });
        picker.SuggestedFileName = "Contract_" + SanitizeFileName(values["English_Employee_Name"]);

        var file = await picker.PickSaveFileAsync();
        if (file is null)
        {
            return; // User cancelled the dialog.
        }

        try
        {
            ExportButton.IsEnabled = false;
            var filled = DocxFiller.Fill(Template, values);
            File.WriteAllBytes(file.Path, filled);
            Show(InfoBarSeverity.Success, "Contract exported", file.Path);
        }
        catch (Exception ex)
        {
            Show(InfoBarSeverity.Error, "Export failed", ex.Message);
        }
        finally
        {
            ExportButton.IsEnabled = true;
        }
    }

    private void Show(InfoBarSeverity severity, string title, string message)
    {
        StatusBar.Severity = severity;
        StatusBar.Title = title;
        StatusBar.Message = message;
        StatusBar.IsOpen = true;
    }

    private static string SanitizeFileName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(c, '_');
        }
        return name.Trim();
    }

    // ------------------------------------------------------------------
    // Responsive layout
    // ------------------------------------------------------------------

    /// <summary>
    /// Wide window: form on the left, preview on the right. Narrow window:
    /// everything stacks into a single column. Field grids go two-up when
    /// there is room for it.
    /// </summary>
    private void OnRootSizeChanged(object sender, SizeChangedEventArgs e)
    {
        double width = e.NewSize.Width;
        bool sideBySide = width >= 1000;

        Grid.SetColumn(PreviewPanel, sideBySide ? 1 : 0);
        Grid.SetRow(PreviewPanel, sideBySide ? 0 : 1);
        ContentGrid.ColumnDefinitions[1].Width = sideBySide
            ? new GridLength(1, GridUnitType.Star)
            : new GridLength(0);
        PreviewPanel.Margin = sideBySide ? new Thickness(0) : new Thickness(0, 24, 0, 0);

        int fieldColumns = sideBySide
            ? (width >= 1200 ? 2 : 1)
            : (width >= 800 ? 2 : 1);
        ArrangeGrid(SharedGrid, fieldColumns);
        ArrangeGrid(LanguageGrid, fieldColumns);

        UpdateTitleBarDragRegion();
    }

    private static void ArrangeGrid(Grid grid, int columns)
    {
        columns = Math.Min(columns, grid.Children.Count);

        grid.ColumnDefinitions.Clear();
        grid.RowDefinitions.Clear();
        for (int c = 0; c < columns; c++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition());
        }

        var children = grid.Children.OfType<FrameworkElement>().ToList();
        int rows = (int)Math.Ceiling(children.Count / (double)columns);
        for (int r = 0; r < rows; r++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }

        for (int i = 0; i < children.Count; i++)
        {
            Grid.SetColumn(children[i], i % columns);
            Grid.SetRow(children[i], i / columns);
        }
    }

    // ------------------------------------------------------------------
    // Title bar drag region (keeps the Export button clickable)
    // ------------------------------------------------------------------

    private void UpdateTitleBarDragRegion()
    {
        if (AppWindow is null || RootGrid.XamlRoot is null || AppTitleBar.ActualWidth == 0)
        {
            return;
        }

        double scale = RootGrid.XamlRoot.RasterizationScale;

        var titleBounds = AppTitleBar.TransformToVisual(null)
            .TransformBounds(new Windows.Foundation.Rect(0, 0, AppTitleBar.ActualWidth, AppTitleBar.ActualHeight));

        // Keep the title-bar controls clear of the system caption buttons.
        RightPaddingColumn.Width = new GridLength(AppWindow.TitleBar.RightInset / scale);

        int left = (int)Math.Round(titleBounds.X * scale);
        int top = (int)Math.Round(titleBounds.Y * scale);
        int height = (int)Math.Round(titleBounds.Height * scale);
        int dragEnd = (int)Math.Round((titleBounds.X + titleBounds.Width) * scale) - AppWindow.TitleBar.RightInset;

        // Interactive zones (update check panel, export button) are excluded
        // from the drag region; everything around them stays draggable.
        var zones = new FrameworkElement[] { TitleBarUpdatePanel, ExportButton }
            .Select(el => el.TransformToVisual(null)
                .TransformBounds(new Windows.Foundation.Rect(0, 0, el.ActualWidth, el.ActualHeight)))
            .Select(b => (Left: (int)Math.Round(b.X * scale), Right: (int)Math.Round((b.X + b.Width) * scale)))
            .Where(z => z.Right > z.Left)
            .OrderBy(z => z.Left)
            .ToList();

        var regions = new List<Windows.Graphics.RectInt32>();
        int cursor = left;
        foreach (var zone in zones)
        {
            if (zone.Left > cursor)
            {
                regions.Add(new Windows.Graphics.RectInt32(cursor, top, zone.Left - cursor, height));
            }
            cursor = Math.Max(cursor, zone.Right);
        }
        if (dragEnd > cursor)
        {
            regions.Add(new Windows.Graphics.RectInt32(cursor, top, dragEnd - cursor, height));
        }

        AppWindow.TitleBar.SetDragRectangles(regions.ToArray());
    }
}

using System.Text;
using System.Text.Json;

/// <summary>
/// Screenshot Analyzer — Claude Vision API를 사용하여 다국어 UI 잘림 문제를 자동 감지.
///
/// Usage:
///   set ANTHROPIC_API_KEY=sk-ant-...
///   dotnet run --project src/Span/Span.ScreenshotAnalyzer [screenshots-dir]
///
/// Default screenshots-dir: src/Span/Span.UITests/TestResults/Screenshots/
/// </summary>

var screenshotDir = args.Length > 0
    ? args[0]
    : FindScreenshotDir();

if (!Directory.Exists(screenshotDir))
{
    Console.Error.WriteLine($"Screenshots directory not found: {screenshotDir}");
    Console.Error.WriteLine("Run ScreenshotReviewTests first to capture screenshots.");
    return 1;
}

var apiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
if (string.IsNullOrEmpty(apiKey))
{
    Console.Error.WriteLine("ANTHROPIC_API_KEY environment variable not set.");
    Console.Error.WriteLine("Set it: set ANTHROPIC_API_KEY=sk-ant-...");
    return 1;
}

var langDirs = Directory.GetDirectories(screenshotDir)
    .Where(d => Directory.GetFiles(d, "*.png").Length > 0)
    .OrderBy(d => d)
    .ToArray();

if (langDirs.Length == 0)
{
    Console.Error.WriteLine($"No language directories with screenshots found in: {screenshotDir}");
    return 1;
}

Console.WriteLine($"Found {langDirs.Length} language(s) to analyze:");
foreach (var d in langDirs)
    Console.WriteLine($"  {Path.GetFileName(d)}/  ({Directory.GetFiles(d, "*.png").Length} screenshots)");
Console.WriteLine();

var report = new StringBuilder();
report.AppendLine("# Screenshot UI Review Report");
report.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm}");
report.AppendLine();

using var httpClient = new HttpClient();
httpClient.DefaultRequestHeaders.Add("x-api-key", apiKey);
httpClient.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");

foreach (var langDir in langDirs)
{
    var langCode = Path.GetFileName(langDir);
    Console.WriteLine($"=== Analyzing: {langCode} ===");
    report.AppendLine($"## {langCode}");
    report.AppendLine();

    var pngFiles = Directory.GetFiles(langDir, "*.png").OrderBy(f => f).ToArray();

    foreach (var pngFile in pngFiles)
    {
        var fileName = Path.GetFileName(pngFile);
        Console.Write($"  {fileName}...");

        try
        {
            var result = await AnalyzeScreenshot(httpClient, pngFile, langCode);
            report.AppendLine($"### {fileName}");
            report.AppendLine();
            report.AppendLine(result);
            report.AppendLine();
            Console.WriteLine(" done");
        }
        catch (Exception ex)
        {
            Console.WriteLine($" ERROR: {ex.Message}");
            report.AppendLine($"### {fileName}");
            report.AppendLine($"**Error:** {ex.Message}");
            report.AppendLine();
        }

        // Rate limit
        await Task.Delay(1000);
    }
}

// Save report
var reportPath = Path.Combine(screenshotDir, "review_report.md");
File.WriteAllText(reportPath, report.ToString());
Console.WriteLine($"\nReport saved to: {reportPath}");
return 0;

// ── Functions ──

async Task<string> AnalyzeScreenshot(HttpClient client, string imagePath, string langCode)
{
    var imageBytes = await File.ReadAllBytesAsync(imagePath);
    var base64 = Convert.ToBase64String(imageBytes);
    var fileName = Path.GetFileName(imagePath);

    var screenType = fileName switch
    {
        _ when fileName.Contains("full_window") => "main explorer window with sidebar and file list",
        _ when fileName.Contains("context_menu") => "right-click context menu",
        _ when fileName.Contains("settings") => "settings page",
        _ => "application screen"
    };

    var prompt = $"""
        You are a UI/UX quality reviewer for a Windows desktop file explorer app (SPAN Finder).
        This screenshot shows the {screenType} in language: {langCode}.

        Analyze this screenshot for TEXT TRUNCATION and LAYOUT issues:

        1. **Text Truncation**: Look for any text that appears cut off, ends with "..." unexpectedly,
           or is clearly too long for its container. Check: sidebar labels, menu items, buttons,
           headers, status bar, dialog labels.

        2. **Text Overflow**: Look for text overlapping other elements, breaking out of its container,
           or causing layout shifts.

        3. **Alignment Issues**: Labels misaligned with their controls, inconsistent spacing,
           elements pushed off-screen.

        4. **Empty/Wasted Space**: Unusually large gaps that suggest a layout problem
           (not normal empty file list areas).

        For each issue found, report:
        - **Location**: Where in the UI (e.g., "sidebar > favorites section header")
        - **Issue**: What's wrong (e.g., "text truncated with ...")
        - **Severity**: Critical / Warning / Info
        - **Visible text**: The actual text shown (if readable)

        If NO issues are found, state "No truncation or layout issues detected."

        Be specific and concise. Only report actual visible issues, not hypothetical ones.
        """;

    var requestBody = new
    {
        model = "claude-sonnet-4-20250514",
        max_tokens = 1024,
        messages = new[]
        {
            new
            {
                role = "user",
                content = new object[]
                {
                    new
                    {
                        type = "image",
                        source = new
                        {
                            type = "base64",
                            media_type = "image/png",
                            data = base64
                        }
                    },
                    new
                    {
                        type = "text",
                        text = prompt
                    }
                }
            }
        }
    };

    var json = JsonSerializer.Serialize(requestBody);
    var content = new StringContent(json, Encoding.UTF8, "application/json");

    var response = await client.PostAsync("https://api.anthropic.com/v1/messages", content);
    var responseBody = await response.Content.ReadAsStringAsync();

    if (!response.IsSuccessStatusCode)
    {
        throw new Exception($"API error {response.StatusCode}: {responseBody[..Math.Min(200, responseBody.Length)]}");
    }

    var doc = JsonDocument.Parse(responseBody);
    var textContent = doc.RootElement
        .GetProperty("content")[0]
        .GetProperty("text")
        .GetString();

    return textContent ?? "No response";
}

string FindScreenshotDir()
{
    var candidates = new[]
    {
        Path.Combine("src", "Span", "Span.UITests", "TestResults", "Screenshots"),
        Path.Combine("..", "Span.UITests", "TestResults", "Screenshots"),
        Path.Combine("Span.UITests", "TestResults", "Screenshots"),
    };

    foreach (var candidate in candidates)
    {
        var full = Path.GetFullPath(candidate);
        if (Directory.Exists(full)) return full;
    }

    return Path.GetFullPath(candidates[0]);
}

using System.Text.Json;
using Helios.Application.Abstractions.Documents;
using Helios.Application.DependencyInjection;
using Helios.Application.Features.Evaluation;
using Helios.Application.Features.Products;
using Helios.Infrastructure.Documents;
using Microsoft.Extensions.DependencyInjection;

// helios-evaluate <dataset-folder> [--out <folder>] [--include-values]
//
// Runs a product version over an owner-supplied labelled dataset on this machine and writes
// report.json and report.md. Exit codes: 0 all written targets met, 2 targets missed or absent,
// 1 the run could not start. See docs/EVALUATION.md.

var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);

if (args.Length == 0 || args[0].StartsWith("--", StringComparison.Ordinal))
{
    Console.Error.WriteLine("Usage: helios-evaluate <dataset-folder> [--out <folder>] [--include-values]");
    return 1;
}

var dataset = Path.GetFullPath(args[0]);
var includeValues = args.Contains("--include-values");
var outIndex = Array.IndexOf(args, "--out");

if (DatasetGuard.InsideRepository(dataset) is { } repository)
{
    Console.Error.WriteLine(
        $"Refusing to read a dataset inside the HELIOS repository ({repository}). Keep customer documents out of source control; " +
        "put the dataset elsewhere on this machine.");
    return 1;
}

var manifestPath = Path.Combine(dataset, "manifest.json");
if (!File.Exists(manifestPath))
{
    Console.Error.WriteLine($"No manifest.json in {dataset}.");
    return 1;
}

var manifest = JsonSerializer.Deserialize<EvaluationManifest>(await File.ReadAllTextAsync(manifestPath), json)
    ?? throw new InvalidOperationException("manifest.json is empty.");

var services = new ServiceCollection()
    .AddSingleton(TimeProvider.System)
    .AddSingleton<IPdfTextReader, PdfPigTextReader>()
    .AddHeliosApplication()
    .BuildServiceProvider();

var executor = services.GetServices<IProductExecutor>()
    .FirstOrDefault(e => e.ProductSlug == manifest.Product && (manifest.Version is null || e.Version == manifest.Version));

if (executor is null)
{
    Console.Error.WriteLine($"This build has no executor for {manifest.Product} v{manifest.Version ?? "(current)"}.");
    return 1;
}

var report = await Evaluator.RunAsync(
    manifest,
    executor,
    file => File.ReadAllBytesAsync(DatasetGuard.Within(dataset, file)),
    includeValues,
    TimeProvider.System,
    CancellationToken.None);

var output = outIndex >= 0 && outIndex + 1 < args.Length
    ? Path.GetFullPath(args[outIndex + 1])
    : Path.Combine(Path.GetDirectoryName(dataset)!, $"{Path.GetFileName(dataset)}-evaluation-{report.RanAt:yyyyMMdd-HHmmss}");

Directory.CreateDirectory(output);
await File.WriteAllTextAsync(Path.Combine(output, "report.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions(json) { WriteIndented = true }));
await File.WriteAllTextAsync(Path.Combine(output, "report.md"), Evaluator.ToMarkdown(report));

Console.WriteLine(Evaluator.ToMarkdown(report));
Console.WriteLine($"Reports written to {output}");

return report.AllTargetsMet ? 0 : 2;

/// <summary>Keeps datasets out of the repository and document paths inside their dataset.</summary>
internal static class DatasetGuard
{
    /// <summary>The repository root when <paramref name="folder"/> is inside a HELIOS checkout, else null.</summary>
    public static string? InsideRepository(string folder)
    {
        for (var dir = new DirectoryInfo(folder); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Helios.sln")))
            {
                return dir.FullName;
            }
        }

        return null;
    }

    public static string Within(string dataset, string file)
    {
        var full = Path.GetFullPath(Path.Combine(dataset, file));
        var root = dataset.EndsWith(Path.DirectorySeparatorChar) ? dataset : dataset + Path.DirectorySeparatorChar;

        return full.StartsWith(root, StringComparison.OrdinalIgnoreCase)
            ? full
            : throw new InvalidOperationException($"'{file}' is outside the dataset folder.");
    }
}

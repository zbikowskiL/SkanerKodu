using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.CodeAnalysis.Text;
using Microsoft.CodeAnalysis.VisualBasic;
using SolutionScanner.Core.Files;
using SolutionScanner.Core.Infrastructure;
using SolutionScanner.Core.Models;

namespace SolutionScanner.Core;

/// <summary>Wczytana solucja Roslyn wraz z diagnostyką. Zwolnij po zakończeniu analizy.</summary>
public sealed class LoadedSolution : IDisposable
{
    public LoadedSolution(Workspace workspace, Solution solution, SolutionDescriptor descriptor,
        IReadOnlyList<ProjectLoadStatus> statuses, IReadOnlyList<LoadDiagnostic> diagnostics)
    {
        Workspace = workspace;
        Solution = solution;
        Descriptor = descriptor;
        Statuses = statuses;
        Diagnostics = diagnostics;
    }

    public Workspace Workspace { get; }
    public Solution Solution { get; }
    public SolutionDescriptor Descriptor { get; }
    public IReadOnlyList<ProjectLoadStatus> Statuses { get; }
    public IReadOnlyList<LoadDiagnostic> Diagnostics { get; }

    public void Dispose() => Workspace.Dispose();
}

public interface ISolutionLoader
{
    SolutionDescriptor ReadDescriptor(string solutionPath);

    Task<LoadedSolution> LoadAsync(SolutionDescriptor descriptor, AnalysisSettings settings, IScanLogger logger,
        IProgress<AnalysisProgress>? progress, CancellationToken cancellationToken);
}

/// <summary>
/// Wczytuje solucję Visual Studio do Roslyn za pomocą MSBuildWorkspace.
/// <para>
/// Strategia odporności: projekty, których MSBuildWorkspace nie wczyta (np. brak SDK, brak Visual Studio
/// dla klasycznych projektów .NET Framework, uszkodzony plik), są dodawane „awaryjnie” na podstawie
/// odczytu XML pliku projektu (elementy Compile, HintPath, ProjectReference) z referencjami bibliotek
/// bieżącego środowiska .NET. Analiza semantyczna takich projektów jest przybliżona i oznaczana w wynikach.
/// </para>
/// </summary>
public sealed class SolutionLoader : ISolutionLoader
{
    private static readonly object RegistrationLock = new();
    private static readonly Lazy<IReadOnlyList<MetadataReference>> RuntimeReferences = new(LoadRuntimeReferences);
    private static readonly ConcurrentDictionary<string, MetadataReference> ReferenceCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Opis zarejestrowanej instancji MSBuild (lub błędu rejestracji).</summary>
    public static string MsBuildDescription { get; private set; } = "MSBuild niezarejestrowany";

    public static bool IsMsBuildAvailable { get; private set; }

    /// <summary>
    /// Rejestruje MSBuild (Microsoft.Build.Locator). Musi zostać wywołane przed pierwszym użyciem typów MSBuild,
    /// najlepiej na starcie aplikacji. Wybierana jest najnowsza instancja zgodna z bieżącym środowiskiem uruchomieniowym.
    /// </summary>
    public static bool EnsureMsBuildRegistered()
    {
        lock (RegistrationLock)
        {
            if (IsMsBuildAvailable) return true;
            try
            {
                if (MSBuildLocator.IsRegistered)
                {
                    IsMsBuildAvailable = true;
                    MsBuildDescription = "MSBuild zarejestrowany wcześniej";
                    return true;
                }

                var runtimeMajor = Environment.Version.Major;
                var instances = MSBuildLocator.QueryVisualStudioInstances(VisualStudioInstanceQueryOptions.Default)
                    .Where(i => i.DiscoveryType != DiscoveryType.DotNetSdk || i.Version.Major <= runtimeMajor)
                    .OrderByDescending(i => i.Version)
                    .ToList();
                if (instances.Count == 0)
                {
                    MsBuildDescription = "Nie znaleziono zgodnej instancji MSBuild / .NET SDK – projekty zostaną wczytane awaryjnie.";
                    return false;
                }

                var chosen = instances[0];
                MSBuildLocator.RegisterInstance(chosen);
                IsMsBuildAvailable = true;
                MsBuildDescription = $"{chosen.Name} {chosen.Version} ({chosen.MSBuildPath})";
                return true;
            }
            catch (Exception ex)
            {
                MsBuildDescription = "Błąd rejestracji MSBuild: " + ex.Message;
                return false;
            }
        }
    }

    public SolutionDescriptor ReadDescriptor(string solutionPath) => SolutionFileReader.Read(solutionPath);

    public async Task<LoadedSolution> LoadAsync(SolutionDescriptor descriptor, AnalysisSettings settings, IScanLogger logger,
        IProgress<AnalysisProgress>? progress, CancellationToken cancellationToken)
    {
        var diagnostics = new ConcurrentBag<LoadDiagnostic>();
        var statuses = new List<ProjectLoadStatus>();

        if (settings.RestorePackagesBeforeLoad)
        {
            progress?.Report(new AnalysisProgress("Wczytywanie", "Przywracanie pakietów NuGet (dotnet restore)..."));
            await RestoreAsync(descriptor.SolutionPath, logger, diagnostics, cancellationToken).ConfigureAwait(false);
        }

        Workspace workspace;
        Solution solution;
        var msbuildOk = EnsureMsBuildRegistered();
        logger.Info("MSBuild: " + MsBuildDescription);

        if (msbuildOk)
        {
            try
            {
                (workspace, solution) = await OpenWithMsBuildAsync(descriptor.SolutionPath, diagnostics, logger, progress, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.Error("MSBuildWorkspace nie wczytał solucji – przełączenie na wczytywanie awaryjne", ex);
                diagnostics.Add(new LoadDiagnostic(Severity.Error, string.Empty, "MSBuildWorkspace: " + ex.Message));
                workspace = new AdhocWorkspace();
                solution = workspace.CurrentSolution;
            }
        }
        else
        {
            diagnostics.Add(new LoadDiagnostic(Severity.Warning, string.Empty, MsBuildDescription));
            workspace = new AdhocWorkspace();
            solution = workspace.CurrentSolution;
        }

        // Projekty niewczytane przez MSBuild – wczytanie awaryjne
        var fallbackIds = new HashSet<ProjectId>();
        foreach (var entry in descriptor.Projects.Where(p => p.Exists && !p.IsSqlProject && p.Language is "C#" or "VB"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (solution.Projects.Any(p => PathEquals(p.FilePath, entry.FilePath))) continue;
            try
            {
                progress?.Report(new AnalysisProgress("Wczytywanie", $"Wczytywanie awaryjne: {entry.Name}", entry.Name));
                solution = AddFallbackProject(solution, entry, out var id);
                fallbackIds.Add(id);
                diagnostics.Add(new LoadDiagnostic(Severity.Warning, entry.Name,
                    "Projekt wczytany awaryjnie (bez MSBuild): analiza semantyczna jest przybliżona – referencje do bibliotek spoza .NET mogą być nierozpoznane."));
                logger.Warning($"Projekt {entry.Name} wczytany awaryjnie.");
            }
            catch (Exception ex)
            {
                diagnostics.Add(new LoadDiagnostic(Severity.Error, entry.Name, "Nie udało się wczytać projektu: " + ex.Message));
                logger.Error($"Nie udało się wczytać projektu {entry.Name}", ex);
            }
        }

        // Uzupełnienie referencji fallbackowych referencjami projektów (po dodaniu wszystkich)
        solution = LinkFallbackProjectReferences(solution, fallbackIds);

        // Projekty wczytane przez MSBuild bez biblioteki bazowej (nieudany design-time build)
        foreach (var project in solution.Projects.ToList())
        {
            if (fallbackIds.Contains(project.Id)) continue;
            if (HasCoreReference(project)) continue;
            solution = solution.AddMetadataReferences(project.Id, RuntimeReferences.Value
                .Where(r => !project.MetadataReferences.Contains(r)));
            diagnostics.Add(new LoadDiagnostic(Severity.Warning, RoslynHelpers.CleanProjectName(project.Name),
                "Brak referencji do biblioteki bazowej (nieudany design-time build lub brak restore). Dodano referencje bieżącego środowiska .NET – analiza semantyczna przybliżona."));
        }

        foreach (var project in solution.Projects)
        {
            statuses.Add(new ProjectLoadStatus
            {
                Name = RoslynHelpers.CleanProjectName(project.Name),
                FilePath = project.FilePath ?? string.Empty,
                Language = project.Language == LanguageNames.VisualBasic ? "VB" : project.Language == LanguageNames.CSharp ? "C#" : project.Language,
                IsFallback = fallbackIds.Contains(project.Id),
                Status = fallbackIds.Contains(project.Id) ? "Wczytany awaryjnie" : "Wczytany (MSBuild)",
                DocumentCount = project.DocumentIds.Count
            });
        }

        foreach (var entry in descriptor.Projects.Where(p => p.IsSqlProject))
        {
            statuses.Add(new ProjectLoadStatus
            {
                Name = entry.Name,
                FilePath = entry.FilePath,
                Language = "SQL",
                Status = entry.Exists ? "Projekt bazy danych – analiza plików SQL" : "Brak pliku projektu"
            });
        }

        foreach (var entry in descriptor.Projects.Where(p => !p.Exists))
            diagnostics.Add(new LoadDiagnostic(Severity.Error, entry.Name, "Plik projektu nie istnieje: " + entry.FilePath));

        return new LoadedSolution(workspace, solution, descriptor, statuses, diagnostics.ToList());
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<(Workspace, Solution)> OpenWithMsBuildAsync(string solutionPath, ConcurrentBag<LoadDiagnostic> diagnostics,
        IScanLogger logger, IProgress<AnalysisProgress>? progress, CancellationToken ct)
    {
        var properties = new Dictionary<string, string>
        {
            ["Configuration"] = "Debug",
            ["AlwaysCompileMarkupFilesSynchronously"] = "true",
            ["CheckForSystemRuntimeDependency"] = "true"
        };
        var workspace = MSBuildWorkspace.Create(properties);
        workspace.SkipUnrecognizedProjects = true;
        workspace.LoadMetadataForReferencedProjects = false;
#pragma warning disable CS0618 // zdarzenie WorkspaceFailed jest dostępne we wszystkich wersjach Roslyn 4.x
        workspace.WorkspaceFailed += (_, e) =>
        {
            var severity = e.Diagnostic.Kind == WorkspaceDiagnosticKind.Failure ? Severity.Error : Severity.Warning;
            diagnostics.Add(new LoadDiagnostic(severity, string.Empty, e.Diagnostic.Message));
            if (severity == Severity.Error) logger.Warning("Workspace: " + e.Diagnostic.Message);
        };
#pragma warning restore CS0618

        var loadProgress = new Progress<ProjectLoadProgress>(p =>
        {
            var name = Path.GetFileNameWithoutExtension(p.FilePath);
            progress?.Report(new AnalysisProgress("Wczytywanie", $"{p.Operation}: {name} {p.TargetFramework}", name, p.FilePath));
        });

        var solution = await workspace.OpenSolutionAsync(solutionPath, progress: loadProgress, cancellationToken: ct).ConfigureAwait(false);
        return (workspace, solution);
    }

    private static async Task RestoreAsync(string solutionPath, IScanLogger logger, ConcurrentBag<LoadDiagnostic> diagnostics, CancellationToken ct)
    {
        try
        {
            var psi = new ProcessStartInfo("dotnet", $"restore \"{solutionPath}\"")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(solutionPath)!
            };
            using var process = Process.Start(psi) ?? throw new InvalidOperationException("Nie można uruchomić dotnet restore.");
            var stdout = process.StandardOutput.ReadToEndAsync(ct);
            var stderr = process.StandardError.ReadToEndAsync(ct);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromMinutes(10));
            try
            {
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch { /* proces mógł się już zakończyć */ }
                throw;
            }
            logger.Info("dotnet restore: " + TextUtil.Shorten(await stdout.ConfigureAwait(false), 2000));
            if (process.ExitCode != 0)
            {
                var err = await stderr.ConfigureAwait(false);
                diagnostics.Add(new LoadDiagnostic(Severity.Warning, string.Empty, "dotnet restore zakończył się kodem " + process.ExitCode + ": " + TextUtil.Shorten(err, 500)));
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            diagnostics.Add(new LoadDiagnostic(Severity.Warning, string.Empty, "Nie udało się wykonać dotnet restore: " + ex.Message));
            logger.Error("dotnet restore", ex);
        }
    }

    /// <summary>
    /// Tworzy projekt Roslyn na podstawie XML pliku projektu: pliki Compile (projekt klasyczny) lub wszystkie pliki
    /// źródłowe katalogu (projekt SDK), referencje HintPath istniejące na dysku oraz biblioteki bieżącego .NET.
    /// </summary>
    internal static Solution AddFallbackProject(Solution solution, ProjectEntry entry, out ProjectId projectId)
    {
        var info = ProjectFileReader.Read(entry.FilePath);
        var isVb = entry.Language == "VB";
        var ext = isVb ? ".vb" : ".cs";
        var excluded = new HashSet<string>(new[] { "bin", "obj", ".git", ".vs", "packages", "node_modules" }, StringComparer.OrdinalIgnoreCase);

        IEnumerable<string> sources = info.IsSdkStyle || info.CompileItems.Count == 0
            ? Directory.EnumerateFiles(entry.Directory, "*" + ext, SearchOption.AllDirectories)
                .Where(f => !SourceFileEnumerator.IsInExcludedDirectory(f, excluded))
            : info.CompileItems.Where(f => f.EndsWith(ext, StringComparison.OrdinalIgnoreCase) && File.Exists(f));

        projectId = ProjectId.CreateNewId(entry.Name);
        var pid = projectId;
        var documents = sources
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(f => DocumentInfo.Create(
                DocumentId.CreateNewId(pid, f),
                Path.GetFileName(f),
                loader: TextLoader.From(TextAndVersion.Create(SourceText.From(FileContentCache.Load(f).Text), VersionStamp.Create(), f)),
                filePath: f))
            .ToList();

        var references = new List<MetadataReference>(RuntimeReferences.Value);
        foreach (var hint in info.HintPathReferences)
        {
            try { references.Add(ReferenceCache.GetOrAdd(hint, p => MetadataReference.CreateFromFile(p))); }
            catch { /* uszkodzona biblioteka – pomijamy */ }
        }

        CompilationOptions compilationOptions = isVb
            ? new VisualBasicCompilationOptions(OutputKind.DynamicallyLinkedLibrary, globalImports: GlobalImport.Parse("System", "System.Collections.Generic", "System.Linq"))
            : new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable);
        ParseOptions parseOptions = isVb
            ? new VisualBasicParseOptions(Microsoft.CodeAnalysis.VisualBasic.LanguageVersion.Latest)
            : new CSharpParseOptions(Microsoft.CodeAnalysis.CSharp.LanguageVersion.Latest);

        var projectInfo = ProjectInfo.Create(
            projectId,
            VersionStamp.Create(),
            entry.Name,
            entry.Name,
            isVb ? LanguageNames.VisualBasic : LanguageNames.CSharp,
            filePath: entry.FilePath,
            compilationOptions: compilationOptions,
            parseOptions: parseOptions,
            documents: documents,
            metadataReferences: references);

        return solution.AddProject(projectInfo);
    }

    private static Solution LinkFallbackProjectReferences(Solution solution, HashSet<ProjectId> fallbackIds)
    {
        foreach (var id in fallbackIds)
        {
            var project = solution.GetProject(id);
            if (project?.FilePath is null) continue;
            ProjectFileInfo info;
            try { info = ProjectFileReader.Read(project.FilePath); }
            catch { continue; }
            foreach (var refPath in info.ProjectReferences)
            {
                var target = solution.Projects.FirstOrDefault(p => PathEquals(p.FilePath, refPath) && p.Id != id);
                if (target is null) continue;
                var reference = new ProjectReference(target.Id);
                if (solution.GetProject(id)!.ProjectReferences.Contains(reference)) continue;
                try { solution = solution.AddProjectReference(id, reference); }
                catch { /* cykl referencji – pomijamy */ }
            }
        }
        return solution;
    }

    private static bool HasCoreReference(Project project)
    {
        foreach (var r in project.MetadataReferences)
        {
            if (r is not PortableExecutableReference pe || pe.FilePath is null) continue;
            var name = Path.GetFileName(pe.FilePath);
            if (name.Equals("mscorlib.dll", StringComparison.OrdinalIgnoreCase)
                || name.Equals("System.Runtime.dll", StringComparison.OrdinalIgnoreCase)
                || name.Equals("netstandard.dll", StringComparison.OrdinalIgnoreCase)
                || name.Equals("System.Private.CoreLib.dll", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    /// <summary>Biblioteki bieżącego środowiska .NET (Trusted Platform Assemblies) – referencje dla projektów awaryjnych.</summary>
    private static IReadOnlyList<MetadataReference> LoadRuntimeReferences()
    {
        var tpa = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ?? string.Empty;
        var list = new List<MetadataReference>();
        foreach (var path in tpa.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var name = Path.GetFileName(path);
            if (!(name.StartsWith("System.", StringComparison.OrdinalIgnoreCase)
                  || name.Equals("mscorlib.dll", StringComparison.OrdinalIgnoreCase)
                  || name.Equals("netstandard.dll", StringComparison.OrdinalIgnoreCase)
                  || name.Equals("Microsoft.CSharp.dll", StringComparison.OrdinalIgnoreCase)
                  || name.Equals("Microsoft.VisualBasic.dll", StringComparison.OrdinalIgnoreCase)
                  || name.Equals("Microsoft.VisualBasic.Core.dll", StringComparison.OrdinalIgnoreCase)))
                continue;
            try { list.Add(ReferenceCache.GetOrAdd(path, p => MetadataReference.CreateFromFile(p))); }
            catch { /* pomijamy nieczytelne biblioteki */ }
        }
        return list;
    }

    /// <summary>Referencje środowiska .NET – używane również w testach jednostkowych.</summary>
    public static IReadOnlyList<MetadataReference> GetRuntimeReferences() => RuntimeReferences.Value;

    private static bool PathEquals(string? a, string? b) =>
        a is not null && b is not null &&
        string.Equals(TextUtil.NormalizePath(a), TextUtil.NormalizePath(b), StringComparison.OrdinalIgnoreCase);
}

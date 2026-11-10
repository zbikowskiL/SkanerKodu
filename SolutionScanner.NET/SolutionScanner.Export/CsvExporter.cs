using System.Globalization;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;
using SolutionScanner.Core.Models;

namespace SolutionScanner.Export;

/// <summary>
/// Eksport wyników do CSV: UTF-8 z BOM (polskie znaki w Excelu), separator „;”, pola z „;”, cudzysłowem
/// lub nową linią są cytowane przez CsvHelper (cudzysłów podwajany). Jeden rekord = jedno wystąpienie.
/// </summary>
public sealed class CsvExporter
{
    private static readonly CsvConfiguration Configuration = new(CultureInfo.InvariantCulture)
    {
        Delimiter = ";",
        HasHeaderRecord = true,
        NewLine = "\r\n"
    };

    public void Write<T>(string path, IEnumerable<T> rows)
    {
        using var writer = new StreamWriter(path, false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        using var csv = new CsvWriter(writer, Configuration);
        csv.WriteRecords(rows);
    }

    /// <summary>Zapisuje komplet raportów do katalogu. Zwraca listę utworzonych plików.</summary>
    public IReadOnlyList<string> ExportAll(AnalysisResult result, string directory)
    {
        Directory.CreateDirectory(directory);
        var files = new List<string>();
        void Save<T>(string name, IEnumerable<T> rows)
        {
            var path = Path.Combine(directory, name);
            Write(path, rows);
            files.Add(path);
        }

        Save("01_Wyniki_Wyszukiwania.csv", ReportRowMapper.Search(result));
        Save("02_Deklaracje_Zmiennych.csv", ReportRowMapper.Declarations(result));
        Save("03_Odwolania.csv", ReportRowMapper.References(result));
        Save("04_Powiazania_Projektow.csv", ReportRowMapper.Dependencies(result));
        Save("05_Typy_Danych.csv", ReportRowMapper.DataTypes(result));
        Save("06_Precyzja.csv", ReportRowMapper.Precision(result));
        Save("07_Zaokraglenia.csv", ReportRowMapper.Roundings(result));
        Save("08_Konwersje.csv", ReportRowMapper.Conversions(result));
        Save("11_Przeplyw_Danych.csv", ReportRowMapper.DataFlow(result));
        Save("12_Potencjalne_Problemy.csv", ReportRowMapper.Problems(result));
        Save("13_Podsumowanie.csv", ReportGenerator.BuildSummary(result).Select(s => new SummaryRow { Sekcja = s.Section, Metryka = s.Metric, Wartosc = s.Value }));
        Save("14_Ograniczenia_Analizy.csv", ReportRowMapper.Limitations(result));
        return files;
    }
}

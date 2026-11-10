# SolutionScanner.NET

Lokalne narzędzie (WPF, .NET 8) do analizy solucji Visual Studio (.sln) oparte na Roslyn.
Kod analizowanych projektów nie jest wysyłany do żadnych usług zewnętrznych.

## Zakres tej wersji

Wersja ogranicza się do **analizy kodu** (bez modułów SQL / ScriptDom, mapowań EF6 i porównania C#↔SQL):

| Moduł | Opis |
|---|---|
| Wyszukiwanie | Fragmenty nazw w identyfikatorach C#/VB („opr” → `Oprocentowanie`, `oprKredytu`), bez komentarzy i literałów tekstowych. Opcjonalnie ignorowanie polskich znaków. Także XML/JSON/config/Razor (bez semantyki). |
| Symbole | Rozpoznanie semantyczne (SemanticModel), rodzaj użycia: deklaracja, odczyt, zapis, odczyt i zapis, wywołanie, przekazanie argumentu, zwrócenie wartości. |
| Odwołania | `SymbolFinder.FindReferencesAsync` w całej solucji (również między projektami). |
| Typy danych | Typ, pełna nazwa CLR, nullable, `var`, generyki, dostęp, zakres, const/static/readonly, wartość początkowa (stała). |
| Precyzja | Miejsca w literale, skala decimal, dokładność reprezentacji binarnej float/double, formatowanie (`N2`, `F4`, `0.00`, interpolacja, `string.Format`). |
| Zaokrąglenia | `Math.Round`, `decimal.Round`, Truncate/Floor/Ceiling, liczba miejsc, `MidpointRounding` (domyślnie ToEven). |
| Konwersje i obliczenia | Konwersje jawne/niejawne (IOperation), `Convert.To*`, `Parse` bez kultury, dzielenie całkowite, przepełnienia. |
| Zależności | Referencje projektów, pakiety NuGet, użycie symboli między projektami. |
| Przepływ danych | Deklaracja → odczyty/zapisy → konwersje/zaokrąglenia → przekazania do parametrów (do 2 poziomów); oznaczanie niekompletności (refleksja, dynamic, dynamiczny SQL). |
| Potencjalne problemy | INFO / WARNING z uzasadnieniem i lokalizacją (np. double w obliczeniach finansowych, decimal→double, dzielenie całkowite). |

## Struktura

```
SolutionScanner.NET.sln
SolutionScanner.Core     – modele, SearchEngine, SolutionLoader (MSBuildWorkspace + wczytywanie awaryjne),
                           ScanOrchestrator, SymbolAnalyzer, DependencyAnalyzer, DataFlowAnalyzer
SolutionScanner.CSharp   – RoslynAnalyzer, DataTypeAnalyzer, PrecisionAnalyzer, RoundingAnalyzer,
                           ConversionAnalyzer, ArithmeticAnalyzer
SolutionScanner.Export   – CsvExporter (CsvHelper), ReportGenerator, wiersze raportów
SolutionScanner.UI       – aplikacja WPF (MVVM, CommunityToolkit.Mvvm, DI)
SolutionScanner.Cli      – wersja konsolowa (automatyzacja / duże solucje)
```

## Kompilacja

Wymagane: .NET 8 SDK (8.0.100+), Windows. Visual Studio 2022+ zalecane (umożliwia wczytywanie klasycznych projektów .NET Framework przez MSBuild).

```
dotnet build SolutionScanner.NET.sln -c Release
```

## Uruchomienie

* UI: `SolutionScanner.UI\bin\Release\net8.0-windows\SolutionScanner.exe` (lub F5 w Visual Studio).
* CLI: `dotnet SolutionScanner.Cli.dll <plik.sln> "opr;odsetki;kapital" <katalog_wyjściowy> [--all] [--restore]`

> Jeżeli Windows zgłasza „Zasady kontroli aplikacji zablokowały ten plik”, to Smart App Control blokuje
> lokalnie zbudowane, niepodpisane biblioteki. Należy podpisać pliki lub wyłączyć Smart App Control
> (Zabezpieczenia Windows → Kontrola aplikacji i przeglądarki).

Analizowana solucja powinna mieć przywrócone pakiety (`dotnet restore` lub opcja w ustawieniach) –
inaczej referencje do bibliotek zewnętrznych będą nierozpoznane.

## Raporty CSV (UTF-8 z BOM, separator `;`)

`01_Wyniki_Wyszukiwania`, `02_Deklaracje_Zmiennych`, `03_Odwolania`, `04_Powiazania_Projektow`, `05_Typy_Danych`,
`06_Precyzja`, `07_Zaokraglenia`, `08_Konwersje` (z operacjami arytmetycznymi), `11_Przeplyw_Danych`,
`12_Potencjalne_Problemy`, `13_Podsumowanie`, `14_Ograniczenia_Analizy`.
(Raporty `09_SQL_Schemat` i `10_Niezgodnosci` nie występują w tej wersji.)

## Znane ograniczenia

* Brak analizy plików SQL, mapowań EF6/EDMX i porównania typów C# z SQL.
* Domyślnie analiza typów/precyzji/konwersji obejmuje tylko wyrażenia powiązane z frazami (opcja „cały kod” to zmienia).
* Projekty, których MSBuild nie wczyta, są wczytywane awaryjnie z pliku projektu – semantyka przybliżona (oznaczane w wynikach).
* Razor (.razor/.cshtml), XML, JSON – wyszukiwanie leksykalne/strukturalne, bez modelu semantycznego.
* Pełna analiza (precyzja, EF) dotyczy C#; dla VB działa wyszukiwanie, odwołania, typy, konwersje i zaokrąglenia.
* Refleksja, `dynamic`, dynamiczny SQL i wartości zwracane z metod ograniczają przepływ danych – takie przepływy są oznaczane jako niekompletne.
* Brak testów jednostkowych w tej wersji.
* Ostrzeżenia są potencjalnymi problemami do weryfikacji, nie dowiedzionymi błędami.

# ADR-0001: C# a .NET 10

- Stav: Přijato
- Datum: 2026-11-04

## Kontext

Python prototyp měl asi 200 řádků funkčního kódu a řadu chyb (duplicitní definice tříd
a metod, které Python tiše přepsal). Nástroj zpracovává výsledky testů xUnit a NUnit,
tedy .NET ekosystém, a má se šířit jako jedna binárka bez instalace runtime.

## Rozhodnutí

CTA se píše v C# na .NET 10. Python prototyp se nepřenáší, začíná se na čisto.

## Důsledky

- Duplicity, které v Pythonu prošly, kompilátor odmítne.
- Stejný ekosystém jako testovaný kód (xUnit, NUnit, `dotnet test`).
- Native AOT dává jednu binárku pro Windows a Linux. Knihovny je nutné ověřovat na AOT
  kompatibilitu (`IsAotCompatible`).
- Ad-hoc analýzy dat v Pythonu zůstávají možné: stačí číst SQLite soubor.

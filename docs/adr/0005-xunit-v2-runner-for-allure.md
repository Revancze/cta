# ADR-0005: Runner xUnit 2.x kvůli Allure

- Stav: Přijato
- Datum: 2026-11-04

## Kontext

Šablona `dotnet new xunit` nasadila `xunit.runner.visualstudio` 3.1.4. Allure.Xunit 2.15.0
je postavený na xUnit v2 a s tímto runnerem se jeho reporter nenačte
(`Could not find requested reporter 'allure'`), takže se výsledky tiše nevytvoří.

## Rozhodnutí

Runner je připnutý na `xunit.runner.visualstudio` 2.8.2 (poslední 2.x) a balíček `xunit`
zůstává na 2.9.3. Výsledky Allure se ukládají do `allure-results/` v kořeni repozitáře
a před každým během testů se složka maže.

## Důsledky

- Allure funguje s obyčejným `dotnet test`, bez přepínačů.
- Dependabot nesmí runner posunout na 3.x. Přechod na xUnit v3 vyžaduje novou verzi
  Allure.Xunit nebo jiný způsob reportování.
- Po downgradu balíčku je potřeba `dotnet clean`, jinak v `bin/` zůstane starý adaptér.

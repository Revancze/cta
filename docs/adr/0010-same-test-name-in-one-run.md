# ADR-0010: Stejné jméno testu v jednom runu

- Stav: Přijato
- Datum: 2026-11-06

## Kontext

Skutečné reporty obsahují v jednom runu stejné jméno vícekrát. xUnit ořezává dlouhé parametry
a různé případy Theory mohou mít stejný text i stejné `testId`. Pořadí se navíc mezi běhy mění,
takže výsledky nelze spolehlivě přiřadit konkrétnímu případu.

Číslování výskytů by při prohození pořadí vytvářelo falešně flaky testy. Pád importu by zase
kvůli jednomu jménu zahodil celý run.

## Rozhodnutí

Výsledky se stejným plným jménem se v jednom runu sloučí do jednoho řádku. Ukládá se počet
výskytů a součet doby běhu. Vyhrává horší výsledek v pořadí NotRun, Skipped, Passed,
Inconclusive, Failed, Error, bez ohledu na pořadí v reportu.

## Důsledky

- Pád se neschová a výsledek nezávisí na pořadí. `SUM(Occurrences)` sedí s počtem výsledků.
- Různé testy se stejným jménem v historii splynou. `cta show` by to mělo označit, když je
  `Occurrences` větší než 1.
- Ze dvou chyb se stejnou závažností a ze dvou výstupů se uloží jen první.
- Otisk parametrů se zatím nepoužívá, protože reporty pro něj nedávají spolehlivá data.

# ADR-0004: Vše běží jen na vyžádání

- Stav: Přijato
- Datum: 2026-11-04

## Kontext

Nástroj nemá zatěžovat počítač ani běžet bez vědomí uživatele.

## Rozhodnutí

Žádná služba na pozadí, žádné hlídání složek. `cta run` a `cta pull` spouští uživatel,
`cta show` jen čte z databáze. CI workflow má jediný spouštěč `workflow_dispatch`.
Exporty vznikají jen na vyžádání.

## Důsledky

- Předvídatelné chování a nulová režie mimo vlastní spuštění.
- Aktuálnost dat závisí na tom, jak často uživatel spouští `cta pull`. Artefakty v Actions
  mají omezenou retenci, takže se `pull` musí spouštět aspoň jednou za její dobu.

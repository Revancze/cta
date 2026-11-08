# ADR-0013: Detail výsledku

- Stav: Přijato
- Datum: 2026-11-08

## Kontext

Šest stavů výsledku je společný jmenovatel formátů, takže se při normalizaci ztrácí, co report
říká navíc: Timeout, Warning, xfail, chyba v setupu. XML se po importu maže, proto se to později
nedá dohledat.

## Rozhodnutí

Verdikt zůstává v `Outcome`, doplněk je ve volném textovém sloupci `OutcomeDetail`.

- Detail se uloží, jen když ho report říká v pevné podobě (atribut nebo přesný text). Obecné
  hledání slova „timeout“ ve zprávách se nedělá.
- Na detailu nezávisí verdikt. Neznámý `label` z NUnit se uloží beze změny.
- U sloučených výsledků patří detail k výsledku, který vyhrál.
- NUnit `Warning` je Passed s detailem `Warning`.
- TRX bez výsledků z běhu, který skončil chybou, se odmítne.

## Důsledky

- Zprávy MSTestu jsou lokalizované, proto se timeout z TRX pozná jen podle atributu `outcome`,
  a reálné TRX ho píší jako `Failed`. Takový timeout detail nedostane.
- Hodnoty `Timeout`, `Aborted` a `Disconnected` z TRX jsou ověřené jen ručně vytvořeným souborem.
- Pád test hostu v půlce běhu TRX nerozliší od úplného běhu. Řeší se až v `cta run`.

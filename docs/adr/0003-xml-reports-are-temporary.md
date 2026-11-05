# ADR-0003: XML reporty jsou dočasné

- Stav: Přijato
- Datum: 2026-11-04

## Kontext

TRX, NUnit i xUnit reporty jsou objemné a po importu do databáze duplicitní.

## Rozhodnutí

XML se po úspěšném importu smaže. Při chybě se ponechá a vypíše se jeho cesta.
Jeho otisk (SHA-256) zůstává v databázi a zajišťuje idempotenci importu.

## Důsledky

- Na disku nezůstávají žádné zbytečné soubory.
- Opakovaný import téhož runu se přeskočí podle otisku.
- Chybu v parseru nejde zpětně ověřit na smazaném reportu. Pro ladění slouží snapshot
  testy na vzorových reportech.

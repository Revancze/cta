# ADR-0002: Jedna lokální SQLite databáze mimo git

- Stav: Přijato
- Datum: 2026-11-04

## Kontext

Výsledky testů se mají uchovávat dlouhodobě a nástroj má fungovat offline, bez serveru.
Každý commit databáze v gitu by ukládal její celou kopii.

## Rozhodnutí

Jediné trvalé úložiště je jeden SQLite soubor na disku uživatele. Jeho umístění se volí
při `cta init`, soubor se přidá do `.gitignore`. Podrobná data (zprávy, stack trace,
výstup testů) jsou jen v něm.

## Důsledky

- Žádný databázový server ani cloud.
- Ztráta disku znamená ztrátu historie, proto později `cta backup`.
- CI drží jen poslední výsledky jako dočasný artefakt, ostatní si uživatel stáhne
  příkazem `cta pull`.

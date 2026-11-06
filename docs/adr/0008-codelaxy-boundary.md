# ADR-0008: Hranice mezi CTA a Codelaxy

- Stav: Přijato
- Datum: 2026-11-06

## Kontext

Codelaxy rozhoduje, co je prokázané pro jeden přesný stav repozitáře. CTA uchovává historii
výsledků testů a odpovídá, co se s testy dělo v čase. Nástroje se doplňují, ale Codelaxy je ve
vývoji a jeho rozhraní pro providery zatím není ustálené.

Run s necommitnutými změnami byl navíc přiřazený commitu, ve kterém testovaný kód nebyl.

## Rozhodnutí

- CTA a Codelaxy na sebe nezávisí v kódu. Data si předávají jen přes strojově čitelný výstup CTA,
  například JSON z `cta show --format json` nebo `cta export`.
- Run ukládá `WorkingTreeDirty`. Analýzy historie nepovažují takový run za důkaz o commitu.
- CTA nezaručuje, že testy jsou správné. Může doložit úplnost reportu, jeho přiřazení ke stavu
  a historii spolehlivosti testu.
- Identita snapshotu, verze nástroje a napojení na Codelaxy se doplní až po ustálení jeho
  rozhraní pro providery. Do té doby je nesmíme navrhovat podle odhadu.

## Důsledky

- Oba nástroje se dají vyvíjet a vydávat samostatně.
- `cta gate` z F4 může později převzít verdikt Codelaxy. Rozhodne se to při návrhu F4.
- Bez identity snapshotu zatím neumí CTA doložit přesný rozpracovaný stav, jen to, že rozpracovaný byl.

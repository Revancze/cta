# ADR-0007: Šetrnost za běhu

- Stav: Přijato
- Datum: 2026-11-05

## Kontext

CTA zpracovává reporty, které mohou mít desítky tisíc výsledků, a běží na stejném počítači,
na kterém se vyvíjí. Nesmí ho zahltit. Velikost samotného programu na disku nehraje roli.

## Rozhodnutí

Nástroj se za běhu chová šetrně, jako by měl k dispozici málo paměti:

- Reporty se čtou proudově (`XmlReader`), nikdy celé do paměti. Paměť nesmí růst
  s velikostí vstupu.
- Stejná data (jména testů, chybové zprávy) se neukládají ani nealokují opakovaně.
- Import běží v jedné transakci s jednou připravenými SQL příkazy.
- Nic neběží na pozadí (viz ADR-0004) a na disku nezůstávají dočasné soubory (viz ADR-0003).
- Čas i alokace se měří v testech, limity jsou součástí bran.

Velikost binárky se neoptimalizuje.

## Důsledky

- Brány obsahují vedle času i paměťové limity a ověření, že paměť neroste s velikostí vstupu.
- Proudové čtení je pracnější než načtení celého dokumentu. To je vědomá cena.
- Knihovny, které načítají celý vstup do paměti, se nepoužijí na velká data.

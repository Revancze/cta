# ADR-0012: NUnit 3 XML

- Stav: Přijato
- Datum: 2026-11-08

## Kontext

TRX ztrácí výsledek `Inconclusive` a označení, proč test selhal. NUnit 3 XML obojí obsahuje.
Report z `nunit3-console` je ale podstatně bohatší než report z NunitXml.TestLoggeru: logger
nepíše `label`, `site` ani `warnings` a `classname` zkracuje.

## Rozhodnutí

Parser se řídí skutečnými reporty z obou nástrojů:

- Plné jméno se bere z `fullname` beze změny, u parametrizované fixture nejde složit ze třídy
  a metody.
- `Failed` s označením Error, Invalid nebo Cancelled je Error, jinak Failed.
- `Warning` je Passed, text varování se uloží do `Failures`.
- Kontrolují se počty na kořeni `test-run`. Povinný je `total`, ostatní jen když je nástroj uvádí.
- Vzorky reportů se neformátují (`.csharpierignore`), musí zůstat tak, jak je nástroj zapsal.

## Důsledky

- `Inconclusive` se neztrácí, pokud se použije NUnit 3 XML místo TRX.
- Timeout je v NUnit `Failed` bez označení, v TRX Error. Sjednotí to až `OutcomeDetail`.
- Stejný test má v NUnit 3 XML i TRX stejné jméno, změna formátu historii nerozpadne.

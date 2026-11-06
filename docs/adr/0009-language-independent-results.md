# ADR-0009: Výsledky testů nezávislé na jazyku

- Stav: Přijato
- Datum: 2026-11-06

## Kontext

CTA vzniklo pro xUnit a NUnit. Historie, flaky testy a rozbor pádů ale nepotřebují vědět, v jakém
jazyce test běžel. Vazbu na .NET mají jen formáty reportů, spuštění testů a rozbor stack trace.
Ani jedna z těchto částí zatím není napsaná, takže změna plánu teď nic nestojí.

## Rozhodnutí

- JUnit XML je podporovaný formát na stejné úrovni jako TRX, NUnit 3 a xUnit v2.
- Testovací příkaz v `cta run` je nastavitelný v `cta.json`. Výchozí zůstává `dotnet test`.
- Otisk chyby má záložní postup: když stack trace nepozná, použije typ výjimky a normalizovanou
  zprávu.
- Jádro analýz nesmí záviset na jazyku ani na testovacím frameworku.

## Důsledky

- První cílovka zůstává .NET, proto se brány ověřují hlavně na něm.
- Formát JUnit XML nemá jednotný standard, takže potřebuje snapshot testy na reportech z více
  nástrojů.
- `AssemblyName` a `Framework` zůstávají nepovinné, protože je jiné jazyky nemusí mít.
- Jazyky bez JUnit XML vyžadují nový parser.

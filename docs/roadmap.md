# CTA – Roadmapa

4. 11. 2026 · @Zbyněk

## Cíl a principy

CTA (pracovní název) dlouhodobě uchovává výsledky testů xUnit a NUnit a odpovídá na otázky,
na které poslední zelený build neodpoví: kdy test začal padat, které testy jsou nestabilní
(flaky), co zpomaluje sadu a jak se kvalita vyvíjí po commitech. Jde o štíhlou, offline
náhradu Azure DevOps Test Analytics.

- **Offline a bez serveru.** Žádná cloudová služba ani databázový server. Síť je potřeba jen
  pro stažení výsledků z GitHub Actions, a to na vyžádání.
- **Jedna databáze u tebe na disku.** SQLite soubor mimo git, umístění zvolíš při `cta init`.
  Podrobná data (zprávy, stack trace, výstup testu) jsou jen v ní.
- **Žádný balast.** XML reporty jsou jen dočasné a po úspěšném importu se smažou. Nic se
  neukládá samo, exporty vznikají jen na vyžádání.
- **Na vyžádání.** Nástroj běží jen tehdy, když ho spustíš. Nic neběží na pozadí.
- **Jeden binární soubor.** Příkaz `cta` (pracovní název) se spustí bez instalace runtime.
- **Idempotence.** Opakovaný import téhož runu nic nezdvojí.
- **Odpověď na „proč“.** Ke každému pádu umí nástroj říct commit, od kterého test padá, co se
  mezi tím změnilo a jakou chybou.
- **Výstup v příkazové řádce.** Například `cta show report 14` nebo `cta show graph`.

## Výchozí stav (2. 11. 2026)

Hotové je SQLite schéma se 4 tabulkami, repozitář s 5 operacemi a 8 testů. Parser i exportér
jsou jen prázdné stuby, takže do DB zatím nevede žádná cesta z reálného reportu. Složka není
git repozitář.

| # | Závažnost | Soubor | Problém | Náprava |
|---|---|---|---|---|
| 1 | Vysoká | `models/domain.py` | `RunEntry` a `ResultEntry` jsou definované dvakrát, import `dataclass` je uprostřed souboru. Platí jen poslední definice, první `ResultEntry` má jiná pole. | Jedna definice od každé třídy. |
| 2 | Vysoká | `repository/analytics_repository.py` | `__init__`, `initialize` a `insert_test` jsou tam dvakrát. Python tiše použije poslední verzi. | Odstranit duplicity, zapnout linter (ruff/pyflakes hlásí F811). |
| 3 | Vysoká | schéma + repozitář | `PRAGMA foreign_keys = ON` platí jen pro spojení v `initialize`. Každá další metoda otevírá nové spojení, takže cizí klíče se nekontrolují. | Nastavit PRAGMA při každém otevření spojení. |
| 4 | Vysoká | repozitář | Každý řádek = nové spojení + commit. `with sqlite3.connect()` spojení nezavírá. Import 5 000 testů = 5 000 transakcí a otevřené handly (na Windows zamknutý soubor). | Jeden import = jedno spojení a jedna transakce. |
| 5 | Střední | repozitář | `connection.commit()` v `insert_source`, `insert_test` a `insert_failure` je odsazený mimo blok `with`. | Vyřeší se přepisem na jednu transakci. |
| 6 | Střední | `schema.sql` | Sloupec `PythonVersion` u .NET testů nedává smysl. Chybí target framework, konfigurace, workflow, job, attempt a matrix. | Datový model v2 (níže). |
| 7 | Střední | `schema.sql` | `Status` je volný text bez `CHECK`, není `UNIQUE (RunId, TestId)`. `Failures` nemá stack trace ani otisk chyby pro seskupování. | `CHECK` na výčet stavů, unikátní klíč, sloupce `StackTrace` a `Signature`. |
| 8 | Střední | `schema.sql` | Verze schématu je sloupec v `Runs`, nejsou migrace. | `PRAGMA user_version` + číslované migrace. |
| 9 | Střední | `Runs.Fingerprint` | Není definováno, z čeho se počítá. Na tom stojí idempotence. | Specifikovat (návrh v části Rozhodnutí). |
| 10 | Nízká | `tests/` | Stejná příprava DB a `RunEntry` se opakuje v 5 souborech. `print` v testech. `test_version` testuje interpret, ne kód. | Sdílená fixture, builder testovacích dat. |
| 11 | Nízká | kořen projektu | Chybí git, `.gitignore`, CI a pinned závislosti. V kořeni leží `analytics.db` a dva snímky README (`image.png`, `image-1.png`). Venv je na Pythonu 3.15, ale soubory se jmenují 3.14. | `git init`, `.gitignore` pro DB a venv, snímky smazat. |
| 12 | Nízká | `analytics.py` | CLI umí jen `init` a ručně parsuje `sys.argv`. | CLI framework (viz Rozhodnutí). |

Testy podle `.pytest_cache` naposledy prošly. Problémy 1 až 5 ale testy neodhalí, protože každý
test používá jen poslední, funkční definici.

## Rozhodnutí

### Jazyk: C# / .NET 10 (potvrzeno)

Projekt se přepíše do C#, potvrzeno 4. 11. 2026. Dnes je v Pythonu asi 200 řádků funkčního
kódu, takže přechod skoro nic nestojí. Později by byl drahý.

| Kritérium | C# / .NET 10 | Python | Go / Rust |
|---|---|---|---|
| Stejný ekosystém jako testovaný kód (xUnit, NUnit, `dotnet test`) | Ano | Ne | Ne |
| Spuštění bez instalace runtime | Native AOT binárka nebo dotnet tool | Potřebuje Python + pip | Jedna binárka |
| SQLite | Microsoft.Data.Sqlite | Vestavěný sqlite3 | Knihovny třetích stran |
| Rychlost prototypování | Dobrá | Nejlepší | Pomalejší |
| Typová bezpečnost (duplicity jako v `domain.py` by kompilátor odmítl) | Ano | Jen s linterem | Ano |

Python by dával smysl, kdyby hlavní hodnota byla v ad-hoc analýzách dat (pandas, notebooky).
K tomu stačí SQLite soubor, který Python přečte i bez přepisu. Go nebo Rust přinášejí jen
binárku, kterou .NET dá také.

### Úložiště: jedna lokální databáze

Jediné trvalé úložiště je `analytics.db` u tebe na disku. Do gitu nepatří, protože každý commit
by uložil celou její kopii. GitHub Actions si drží jen výsledky posledních běhů jako dočasný
artefakt, který GitHub po době retence sám smaže. Data se do DB dostanou dvěma cestami:

**Lokální běh – `cta run`**

1. Spustí `dotnet test` s TRX výstupem do dočasné složky.
2. Report naimportuje do DB v jedné transakci.
3. Po úspěšném importu XML smaže. Při chybě ho nechá a vypíše cestu.

**Výsledky z CI – `cta pull`**

1. Workflow v GitHub Actions spustí testy a `cta pack` převede TRX na malý komprimovaný soubor
   (řádově kB). Jako artefakt se nahraje jen ten, XML ne.
2. Ty na vyžádání spustíš `cta pull`. Stáhne jen runy, které v DB ještě nejsou, naimportuje je
   a stažené soubory smaže.
3. `cta show compare` porovná poslední CI run s posledním lokálním: co padá jen v CI, co jen
   u tebe, nové a zmizelé testy.

Každý run si nese commit SHA, větev a původ (local / ci). Díky tomu DB ví, ve kterém commitu se
test objevil, kdy naposledy prošel a kdy poprvé spadl.

Místo na disku šetří tři věci: XML se po importu maže, stejné chybové zprávy se v DB ukládají
jen jednou a stará data jde na vyžádání prořídit (`cta prune`, `cta vacuum`).

### Spouštění na vyžádání

Nic neběží na pozadí: žádná služba ani hlídání složek. `cta run` a `cta pull` spouštíš ty,
`cta show` jen čte z DB. Testy v GitHub Actions běží na serverech GitHubu a počítač nezatěžují.
I CI se spouští jen ručně, workflow má jediný spouštěč `workflow_dispatch`.

### Fingerprint a idempotence

Fingerprint = SHA-256 ze seřazeného seznamu SHA-256 všech zdrojových reportů. Stejné soubory
dávají stejný otisk, takže druhý import se přeskočí. XML se sice maže, ale jeho otisk zůstává
v DB. RunKey = `ci/<workflow run id>/<attempt>/<job>` pro CI a `local/<počítač>/<čas UTC>` pro
lokální běh.

### Struktura řešení

Pět projektů: `Cta.Contracts` (doménové typy), `Cta.Core` (parsery, analýzy), `Cta.Storage`
(SQLite, migrace), `Cta.Cli` (System.CommandLine), `Cta.Tests` (xUnit, snapshot testy na
vzorových reportech).

### Vývojové nástroje

Tyto čtyři nástroje slouží jen při vývoji CTA. Do výsledné binárky se nedostanou.

| Nástroj | K čemu v projektu | Poznámka |
|---|---|---|
| ADR | Každé rozhodnutí jako krátký záznam v `docs/adr/` (kontext, rozhodnutí, důsledky) | První záznamy: C#, jedna lokální DB, mazání XML, ruční CI |
| StrictDoc | Požadavky v textových souborech `.sdoc` v repu a trasování požadavek → test | Jde o Python nástroj, jen pro vývoj |
| ArchUnitNET | Testy architektury: Contracts nezávisí na ničem, Core nezávisí na Storage ani Cli | ArchUnit je pro Javu, pro C# je port ArchUnitNET |
| Allure | Přehledný report vlastních testů CTA během vývoje | Allure Report je open source statické HTML. Self-hosted server je Allure TestOps, který je placený. |

## Roadmapa

Roadmapa má 8 fází od úklidu po verzi 1.0. Každou fázi uzavírá brána s měřitelným kritériem
a další fáze začíná až po jejím splnění. První užitečný výsledek (lokální DB plněná z tvých
běhů i z CI) přijde po F3. Termíny nejsou stanovené, číselné limity v branách jsou návrh
k potvrzení.

> F3 je první bod, kdy nástroj přináší užitek: lokální DB se plní z tvých běhů i z CI.

### F0 – Základ

- [x] Nový čistý repozitář (Python prototyp se nepřenáší), `.gitignore` pro DB, `bin/`, `obj/`
      a `allure-results/`
- [x] .NET 10 solution: Contracts, Core, Storage, Cli, Tests; `Directory.Build.props`
      s nullable, warnings as errors a analyzátory
- [x] ADR: šablona v `docs/adr/` a první záznamy z části Rozhodnutí
- [x] StrictDoc: požadavky z této roadmapy v `docs/requirements/`, ID požadavku uvedené
      u testů (zatím architektura `CTA-ARCH-001` až `003`, další přibydou s fázemi)
- [x] ArchUnitNET: testy závislostí mezi projekty
- [x] Allure: Allure.Xunit v testech, report jen na vyžádání (výsledky se sbírají, generování
      HTML čeká na volbu Node.js / Java)
- [x] CI workflow: build a testy na Windows i Linuxu, spouštění ručně (`workflow_dispatch`)
- [x] README: účel, principy, odkaz na tuto roadmapu

**Brána G0:** CI je zelené na obou OS bez varování, testy ArchUnitNET procházejí a StrictDoc
vygeneruje trasovací matici bez chyb.

### F1 – Datový model a úložiště

- [ ] Doménové typy (Run, TestCase, TestResult, Failure, Source) jako immutable recordy
- [ ] Schéma v2 (viz Datový model) + `PRAGMA user_version` + číslované migrace
- [ ] Jedno spojení na import, jedna transakce, `foreign_keys = ON`, WAL
- [ ] Výpočet Fingerprint a RunKey, idempotentní import
- [ ] Testy: vytvoření DB, migrace z každé verze, duplicitní import, porušení cizího klíče

**Brána G1:** import 10 000 výsledků do 2 s, druhý import téhož runu nezmění počet řádků.

### F2 – Parsery

- [ ] TRX (`dotnet test --logger trx`), základní formát
- [ ] NUnit 3 XML
- [ ] xUnit v2 XML; JUnit XML jako volitelný
- [ ] Detekce formátu podle obsahu, ne podle přípony
- [ ] Normalizace stavů na výčet Passed / Failed / Error / Skipped / NotRun / Inconclusive
- [ ] Stabilní identita testu: plné jméno + otisk parametrů (Theory/TestCase)
- [ ] Otisk chyby: typ výjimky + normalizovaná zpráva (bez čísel, GUIDů, cest) + první rámec
      stacku
- [ ] Snapshot testy na sadě vzorových reportů, včetně poškozených a prázdných

**Brána G2:** všechny reporty ze vzorového .NET projektu se naimportují bez ztráty a součty
sedí s výstupem `dotnet test`.

### F3 – Lokální běh a výsledky z CI

- [ ] `cta init`: interaktivně nabídne umístění DB (mimo git), přidá ji do `.gitignore`, volby
      uloží do `cta.json`
- [ ] `cta run`: `dotnet test` s TRX do dočasné složky → import → smazání XML (při chybě
      ponechat)
- [ ] `cta import <soubor>`: ruční import existujícího reportu, volitelně `--delete`
- [ ] Metadata runu: commit, větev, původ local / ci, OS, framework; lokálně z git, v CI
      z proměnných Actions
- [ ] `cta pack`: TRX → komprimovaný soubor runu pro artefakt
- [ ] Vzorový workflow: test → `cta pack` → `actions/upload-artifact`, jen ruční spouštěč
      (`workflow_dispatch`)
- [ ] `cta pull`: stáhne nové artefakty přes GitHub API (token z `gh auth` nebo proměnné),
      naimportuje, smaže
- [ ] Nasadit na první vlastní repozitář

**Brána G3:** po 20 lokálních a 20 CI bězích nezůstane na disku žádný XML ani stažený soubor
a opakovaný `cta pull` nic nezdvojí.

### F4 – Analýzy

- [ ] Historie testu: posledních N výsledků, commit, kdy se test objevil
- [ ] Proč spadl: poslední zelený a první červený commit, počet commitů mezi nimi, seznam
      změněných souborů z lokálního git a chybová zpráva se stack trace
- [ ] Nové a zmizelé testy mezi dvěma commity nebo runy
- [ ] Porovnání CI vs. lokální run
- [ ] Flaky detekce: různé výsledky na stejném commitu, míra překlopení v okně posledních runů
- [ ] Seskupení selhání podle otisku chyby
- [ ] Nejpomalejší testy a regrese doby běhu
- [ ] `cta gate`: pravidla v `cta.json`, návratový kód pro CI

**Brána G4:** na reálné historii najde flaky testy a commit prvního pádu, které sedí s ruční
kontrolou.

### F5 – Příkazy show

- [ ] `cta show report [dny]`: souhrn za posledních N dní (výchozí 14) – runy, úspěšnost, nová
      selhání, flaky, nejpomalejší
- [ ] `cta show graph [dny]`: úspěšnost a doba běhu po dnech jako sloupcový graf v terminálu
- [ ] `cta show test <jméno>`: historie testu a odpověď, proč a od kdy padá
- [ ] `cta show why <jméno>`: jen rozbor pádu (commity, změněné soubory, chyba)
- [ ] `cta show compare`: poslední CI run vs. poslední lokální
- [ ] `cta show new [dny]`: nové a zmizelé testy
- [ ] `cta show flaky`, `cta show failures`, `cta show slowest`, `cta show run <id>`
- [ ] Společné volby `--branch`, `--since` a `--format table|json|csv|md`

**Brána G5:** `cta show report 14` nad 1 000 runy odpoví do 1 s.

### F6 – Export

- [ ] `cta export csv|xlsx|json [dny] [--out soubor]`: soubor vznikne jen na vyžádání
- [ ] Volitelně `cta export html`: jeden statický soubor, otevře se v prohlížeči i offline

**Brána G6:** export 1 000 runů do 2 s.

### F7 – Verze 1.0

- [ ] Native AOT binárky pro win-x64 a linux-x64, release přes GitHub Releases
- [ ] `cta prune --older-than <dny>` a `cta vacuum` pro úklid DB na vyžádání
- [ ] Dokumentace: rychlý start, příkazy, pravidla gate, migrace
- [ ] SemVer, CHANGELOG, migrace DB mezi verzemi bez ztráty dat

**Brána G7:** cizí .NET repozitář nasadí nástroj podle README do 15 minut.

## Datový model v2

Data jsou podrobná, ale nic se neukládá dvakrát. Stejná chybová zpráva se stack trace se uloží
jednou a výsledky na ni odkazují. Hlavní změna je samostatná tabulka `TestCases`. Jméno testu
se uloží jednou a výsledky na něj odkazují číslem, takže historie jednoho testu je jeden
indexovaný dotaz a DB je menší. Otisk chyby umí seskupovat stejná selhání napříč runy.

```sql
PRAGMA user_version = 2;

CREATE TABLE Runs (
    RunId          INTEGER PRIMARY KEY,
    RunKey         TEXT NOT NULL UNIQUE,   -- ci/... nebo local/...
    Fingerprint    TEXT NOT NULL UNIQUE,   -- SHA-256 zdrojových reportů
    Origin         TEXT NOT NULL CHECK (Origin IN ('local','ci')),
    StartedUtc     TEXT NOT NULL,
    Repository     TEXT,
    Branch         TEXT,
    CommitSha      TEXT NOT NULL,
    Os             TEXT,
    Framework      TEXT,                   -- net10.0 …
    Configuration  TEXT,                   -- Debug / Release
    DurationMs     INTEGER NOT NULL
);

CREATE TABLE TestCases (
    TestCaseId     INTEGER PRIMARY KEY,
    FullName       TEXT NOT NULL UNIQUE,   -- Namespace.Class.Method(paramHash)
    Assembly       TEXT,
    ClassName      TEXT,
    MethodName     TEXT,
    FirstSeenRunId INTEGER REFERENCES Runs(RunId)
);

CREATE TABLE Failures (                   -- každá zpráva uložená jen jednou
    FailureId      INTEGER PRIMARY KEY,
    ContentHash    TEXT NOT NULL UNIQUE,   -- SHA-256 typu + zprávy + stacku
    Signature      TEXT NOT NULL,          -- normalizovaný otisk pro seskupení
    ExceptionType  TEXT,
    Message        TEXT,
    StackTrace     TEXT
);

CREATE TABLE Results (
    RunId          INTEGER NOT NULL REFERENCES Runs(RunId) ON DELETE CASCADE,
    TestCaseId     INTEGER NOT NULL REFERENCES TestCases(TestCaseId),
    Outcome        TEXT NOT NULL CHECK (Outcome IN
                   ('Passed','Failed','Error','Skipped','NotRun','Inconclusive')),
    DurationMs     INTEGER NOT NULL,
    FailureId      INTEGER REFERENCES Failures(FailureId),
    Output         TEXT,                   -- stdout/stderr testu
    PRIMARY KEY (RunId, TestCaseId)
) WITHOUT ROWID;

CREATE TABLE Sources (                    -- záznam o smazaném XML
    RunId          INTEGER NOT NULL REFERENCES Runs(RunId) ON DELETE CASCADE,
    FileName       TEXT NOT NULL,
    Format         TEXT NOT NULL,          -- trx / nunit3 / xunit2 / junit
    Sha256         TEXT NOT NULL,
    PRIMARY KEY (RunId, Sha256)
);

CREATE INDEX IX_Results_TestCase ON Results(TestCaseId, RunId);
CREATE INDEX IX_Runs_Branch_Time ON Runs(Branch, StartedUtc);
CREATE INDEX IX_Runs_Commit ON Runs(CommitSha);
CREATE INDEX IX_Failures_Signature ON Failures(Signature);
```

Souhrnné počty (passed, failed…) se neukládají, počítají se pohledem nad `Results`, aby se
nemohly rozejít s detailem. `GateStatus` se také neukládá, protože závisí na pravidlech, která
se mohou změnit.

## Rizika a otevřené otázky

| Riziko | Dopad | Opatření |
|---|---|---|
| DB je jen na jednom disku | Ztráta disku = ztráta historie | `cta backup` na vyžádání (zkopíruje DB kamkoli, např. na flash nebo cloudový disk) |
| Artefakty v Actions mají omezenou retenci | CI runy starší než retence už nejde stáhnout | `cta pull` spouštět aspoň jednou za dobu retence; `cta show report` upozorní, když poslední pull je starý |
| `cta pull` potřebuje síť a token | Bez sítě nejdou CI výsledky | Lokální běhy fungují vždy, pull je volitelný |
| „Proč spadl“ potřebuje commity v lokálním gitu | Commit z CI, který lokálně nemáš, nejde rozebrat | Před rozborem `git fetch`, jinak ukázat aspoň chybu a SHA |
| Identita testu se změní při přejmenování | Historie se rozpadne na dva testy | Zatím akceptovat, později ruční mapa přejmenování v `cta.json` |
| Nekonzistentní TRX mezi `dotnet test` a Microsoft.Testing.Platform | Špatně načtené stavy | Snapshot testy na reportech z obou platforem |
| Native AOT a knihovny pro XLSX a grafy v terminálu | Export nebo grafy nepůjdou v AOT binárce | Ověřit v F5, jinak XLSX ručně přes OpenXML |

### Otevřené otázky

- [x] Přechod na C# – potvrzeno.
- [x] Úložiště – jedna DB u tebe na disku, CI drží jen poslední výsledky, `cta pull` je na
      vyžádání stáhne.
- [x] XML reporty – jen dočasné, po importu se mažou.
- [x] Výstupy a exporty – jen na vyžádání, v příkazové řádce.
- [x] Spouštění – jen na vyžádání, lokálně i v CI (`workflow_dispatch`).
- [x] Název – pracovní název CTA zůstává.
- [ ] Výstup testu (stdout) – ukládat u všech výsledků, úspěšných i neúspěšných.
- [x] Projekt začne znovu na čisto s nástroji ADR, StrictDoc, ArchUnitNET a Allure.
- [x] Allure – stačí open source Allure Report, žádný server.
- [ ] Potvrdit číselné limity v branách G1, G5 a G6.

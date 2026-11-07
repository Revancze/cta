# ADR-0011: Dialekty JUnit XML

- Stav: Přijato
- Datum: 2026-11-07

## Kontext

`dotnet test` umí sám vytvořit jen TRX. NUnit 3 XML i xUnit v2 XML potřebují konzolový runner
nebo logger třetí strany. JUnit XML naopak píše skoro každý nástroj mimo .NET (pytest, CTest,
Jest, Gradle), jenže nemá jeden standard a každý nástroj ho píše po svém.

## Rozhodnutí

JUnit XML má přednost před NUnit 3 XML. Parser se řídí skutečnými vzorky z pytestu, CTestu
a JunitXml.TestLoggeru, ne domnělou specifikací:

- `classname` stejné jako jméno testu znamená, že test nemá třídu (CTest).
- `status="disabled"` nebo `notrun` bez elementu výsledku je NotRun, ne Passed.
- `failure` je Failed, `error` je Error, `skipped` je Skipped; při více elementech vyhraje horší.
- Čas startu se převezme, jen když uvádí časovou zónu.
- Počet testů se kontroluje proti atributu `tests` na nejvyšší úrovni sad (vnořené sady by
  se jinak započítaly dvakrát) a proti `tests` na `<testsuites>`, pokud ho report uvádí.

## Důsledky

- CTA přečte výsledky z jiných jazyků než .NET (ADR-0009).
- `Inconclusive` z NUnit se dál ztrácí, dokud nepřibude parser NUnit 3 XML.
- CTest zapisuje chybějící spustitelný soubor jako `skipped`, i když ho v konzoli počítá jako
  selhání. CTA se drží reportu, takže takový test zůstane Skipped.
- Nový nástroj s jiným dialektem může potřebovat další pravidlo a vlastní vzorek.
#
- dotnet test umí sám jen TRX, proto JUnit dostal přednost před 
 NUnit 3 XML.
- JUnit XML nemá standard, řídíme se skutečnými vzorky z pytestu, CTestu a .NET loggeru.
- classname stejné jako jméno znamená, že test nemá třídu.
- status disabled nebo notrun bez elementu znamená NotRun.
- Čas bez zóny se nebere.
- Počet testů se kontroluje proti atributu tests na nejvyšší úrovni sad.
- Nevyřešené: chybějící spustitelný soubor v CTestu zůstává Skipped.

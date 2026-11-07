- dotnet test umí sám jen TRX, proto JUnit dostal přednost před 
 NUnit 3 XML.
- JUnit XML nemá standard, řídíme se skutečnými vzorky z pytestu, CTestu a .NET loggeru.
- classname stejné jako jméno znamená, že test nemá třídu.
- status disabled nebo notrun bez elementu znamená NotRun.
- Čas bez zóny se nebere.
- Počet testů se kontroluje proti atributu tests na nejvyšší úrovni sad.
- Nevyřešené: chybějící spustitelný soubor v CTestu zůstává Skipped.

# COI_Bridge

Eigenstaendiger Mod **Bridge 0.9.0** (Mod-ID `Bridge`) fuer Captain of Industry. Die lokale
Windows-Named-Pipe-Schnittstelle bietet Live-Abfragen, explizite Spielbefehle
und `camera.center`. Der zugehoerige Kommandozeilen-Client liegt ebenfalls hier.

Die Bridge leitet Auftraege an das Spiel weiter. Sie verwaltet keinen eigenen
Soll-Spielzustand, Gebiete, Planungen oder Kamerapresets. `camera.center`
veraendert nur die Ansicht und funktioniert auch mit `write_enabled=false`.

## Bauen und installieren

Windows, .NET Framework 4.8 und das lokal installierte Spiel (API 0.8.7d):

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/build.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/install.ps1
```

Bei anderer Installation `-GameRoot` an den Build uebergeben oder `COI_ROOT`
setzen. Ergebnis: `artifacts/CoIBridge` und `artifacts/CoIBridgeClient`.
Warnungen werden als Fehler behandelt. Spielassemblies werden nur lokal
referenziert und nicht mitgeliefert. Der Build installiert nichts automatisch.
Installation kopiert nur Bridge und Client; anschliessend Spiel neu starten
und **Bridge** aktivieren. Installationsordner: `Mods/Bridge`. Bei einem Upgrade
eine alte Installation mit Mod-ID `CoIBridge` deaktivieren, damit nicht beide
Varianten gleichzeitig geladen werden.

Die bestehenden Broker-/Pipe-Tests lassen sich optional mit `-RunTests`
ausfuehren.

## Unterlagen

- [Protokoll, Operationen und Client](docs/local-bridge.md)
- [Pruefstand](STATUS.md)

`src/SharedReadModel` enthaelt die in diesem Repository gepflegten Quellen fuer
lesende Abfragen. Der Build kompiliert sie in den Namespace der Bridge.
Es gibt keine gemeinsame Laufzeit-DLL und keine Abhaengigkeit von einem
anderen Checkout. Alle Quellen werden eigenstaendig in diesem Repository gepflegt.

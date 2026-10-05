# Funktionsstand

Bridge 0.9.0, Protokollversion 1. Windows und lokale Spielinstallation erforderlich.

- Named-Pipe-Schnittstelle mit Client, Sessionpruefung, Deduplication,
  geordneter Auftragsverarbeitung und konfigurierbarer Spiel-Schreibfreigabe.
- Live-Abfragen von Entities, Prototypen, Rezepten, Lager-/Produktionsdaten,
  Ports, Zuegen und Spielstatus.
- Explizite Bau-, Abbau-, Transport-, Logistik- und Spielsteuerungsbefehle.
- Freie Baumfaell-, Bergbau-, Planierungs- und Verkippungsmarkierungen sowie
  getrennte Minen-/Forstturmgebiete. Der Zustand wird vom Spiel verwaltet.
- `camera.center` mit optionalem Abstand und Winkeln; auch bei deaktivierter
  Spiel-Schreibfreigabe. Sonderkameras werden abgelehnt.

Bridge und Client bauen mit Warnungen als Fehler. Flaechenwerkzeuge,
Turmgebiete und Kamerasteuerung wurden mit kurzen Ingame-Tests geprueft.
Nicht alle Operationen, Argumentvarianten und Spielzustaende sind praktisch
geprueft. Insbesondere Sonderkameras, Entladen mit wartendem Kameraauftrag
und extreme native Kameragrenzen sind offen. `runtime_verified:false` in
den Capabilities bleibt eine konservative Kennzeichnung.

Die Bridge exportiert keine Snapshots und speichert keine eigene Planung,
Gebietsverwaltung oder Kamerapresets. Protokoll und Parameter:
[Lokale Bridge](docs/local-bridge.md).


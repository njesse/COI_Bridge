# Lokale Spiel-API (Bridge 0.9.0)

Bridge ist ein eigenstaendiger Mod fuer Live-Abfragen und explizite
Spielbefehle ueber Windows Named Pipes. Er exportiert keine Dateien mit
Spielzustand und bietet keine Snapshot-Operation an. Discovery und lokale
Konfiguration sind reine Verbindungs-/Betriebsdateien; `game.save` bleibt ein
expliziter Auftrag an die regulaere Speicherfunktion des Spiels.

Keine direkte ChatGPT-Anbindung, kein HTTP-/LAN-Server, keine
automatische Routenfindung und kein beliebiger .NET-Methodenaufruf.

## Build und Installation

Windows, .NET Framework 4.8 und die lokal installierte Spielversion 0.8.7d.
Kein zusaetzliches SDK und keine NuGet-Pakete. Aus diesem Repository:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/build.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/install.ps1
```

Optional `-RunTests` am Build fuer die vorhandenen Broker-/Pipe-Tests.
Die Installation kopiert ausschliesslich Bridge und Client und startet/beendet
kein Spiel. Danach selbst neu starten und **Bridge** aktivieren. Der Mod wird
unter `Mods/Bridge` installiert. Eine alte Installation mit ID `CoIBridge`
deaktivieren, damit keine zweite Bridge geladen wird.

Pakete: `artifacts/CoIBridge/CoIBridge.dll` (Mod-ID `Bridge`, `IsUiOnly=false`)
und `artifacts/CoIBridgeClient/CoIBridgeClient.exe` (ohne Spielassemblies).
Der Client und diese Anleitung liegen nach Installation ausserdem unter
`%APPDATA%\Captain of Industry\StateReporter\client`. Discovery und
Konfigurationspfade bleiben erhalten; `StateReporter` im Pfad ist historische
Kompatibilitaet und keine Abhaengigkeit vom Reporter-Mod.

`src/SharedReadModel` ist lokal in diesem Repository enthalten und wird direkt
in die Bridge kompiliert. Kein anderer Checkout und keine gemeinsame DLL.
Alle Quellen werden eigenstaendig in diesem Repository gepflegt.
Bei einem Upgrade vom alten kombinierten Mod 0.6 dessen alte DLL deaktivieren,
damit keine zweite Bridge geladen wird.

Protokollversion 1 bleibt erhalten. `snapshot.create` ist seit 0.7.0 entfernt;
Clients muessen `bridge.capabilities` abfragen. Discovery fuehrt
`bridge_version`; `reporter_version` bleibt der veraltete Alias fuer dieselbe
Bridge-Version. Laufzeitnachweise: [STATUS](../STATUS.md).

## Kamera (Bridge 0.9.0)

`camera.center` veraendert die normale Spielansicht. In `bridge.capabilities`
steht dafuer `writes_game:false` und `changes_view:true`; bei allen anderen
Operationen ist `changes_view:false`. Die Kamera funktioniert auch mit
`write_enabled=false`. Sessionpruefung, Reihenfolge und Deduplication gelten
wie fuer andere Auftraege.

```json
{"operation":"camera.center","args":{"position":{"x":500,"y":600},"distance_tiles":80,"yaw_degrees":180,"pitch_degrees":45}}
```

`position` ist erforderlich: ganzzahlige X/Y innerhalb der Inselkarte, ohne Z.
Die drei anderen Parameter sind optional. `distance_tiles` ist der absolute
Orbitabstand in Tiles (endlich, groesser null und im nativen Zahlenformat
darstellbar); `yaw_degrees` erlaubt 0 bis 360 Grad, `pitch_degrees` 0 bis 90 Grad.
Yaw 0 blickt in Kartenrichtung +Y und steigt gegen den Uhrzeigersinn.
Pitch 0 blickt parallel zum Boden, Pitch 90 senkrecht nach unten.
Fehlende Parameter behalten ihre aktuellen Werte. Explizites `null`, unbekannte
Argumente und ungueltige Kartenpositionen werden vor Ausfuehrung abgelehnt.
Die nativen Zoom-/Winkelgrenzen gelten zusaetzlich; Grenzwerte koennen daher
vom Spiel begrenzt werden.

Die Bridge uebergibt einen kurzlebigen Auftrag vom Simulationsthread an
`IGameLoopEvents.RenderUpdate`. Nur dort werden Kameraobjekte aufgeloest und
bedient: optionale Werte ueber `SetOrbitRadius`, `SetYaw`, `SetPitch`, dann
`CameraController.PanTo` und `ReleaseDamping` fuer einen direkten Sprung.
Der Auftrag wird beim folgenden Renderupdate abgeschlossen. Erst danach wird
sein Job als `processed` gemeldet; die Simulation muss dafuer nicht laufen.

Das Ergebnis enthaelt `target_position` (angeforderte X/Y), `previous_view`
und `effective_view`. Beide Ansichten enthalten `position` (nativer Kamera-
Pivot in Tiles, gegebenenfalls mit Nachkommastellen), `distance_tiles`,
`yaw_degrees` und `pitch_degrees`. `effect_completion` ist `view_updated`.
Die wirksame Ansicht wird nach der nativen Anwendung gelesen. Das Ziel ist
kein Versprechen einer pixelgenauen Bildschirmmitte; Terrain und native
Kamerabegrenzungen beeinflussen die Darstellung.

Dolly-/Film-Kameras, freier Blick und andere besondere Kameramodi werden mit
verstaendlichem Fehler abgelehnt. Es erfolgt kein automatischer Moduswechsel.
Beim Entladen werden wartende Auftraege abgebrochen und Render-Abonnements
entfernt. Es werden keine Kamerapositionen, Presets oder Zielverfolgungen
persistiert. Die bisherigen kurzlebigen Jobantworten koennen die zuvor
gelesene Ansicht fuer den Aufrufer enthalten.

Nur die Bridge referenziert zusaetzlich die lokalen Assemblies `Mafi.Unity`,
`Mafi.UnityCore` und `UnityEngine.CoreModule`; Spielassemblies werden nicht
mitgeliefert. Der Client benoetigt diese Referenzen nicht.

## Verbindung und erster Aufruf

Discovery-Dateien: `%APPDATA%\Captain of Industry\StateReporter\bridge-<pid>-<session>.json`.
Sie enthalten `version`, `pid`, `pipe`, `session`, `bridge_version`, `reporter_version` (Alias), `game_name`.
Die Datei wird erst nach Erstellung der Pipe atomar veroeffentlicht und beim
Entladen entfernt. Nach einem Prozessabsturz kann eine veraltete Datei bleiben.
Bei mehreren Instanzen anhand PID und Spielname bewusst die richtige auswaehlen.

```powershell
$client = '.\artifacts\CoIBridgeClient\CoIBridgeClient.exe'
& $client discover
# Einen der ausgegebenen Dateipfade bewusst auswaehlen:
$discovery = 'C:\Users\NAME\AppData\Roaming\Captain of Industry\StateReporter\bridge-PID-SESSION.json'
$request = @{ id = [guid]::NewGuid().ToString('N'); operation = 'game.status'; args = @{} }
$request | ConvertTo-Json -Depth 12 | Set-Content -Encoding UTF8 request.json
& $client request $discovery request.json 60
# Bei ungewissem Ausgang dieselbe ID nachschlagen, NICHT erneut ausfuehren:
& $client status $discovery $request.id 60
```

`request` ergaenzt Protokollversion und Sitzung aus Discovery. Es sendet den
Auftrag genau einmal und fragt anschliessend `job.status` ab. Exitcodes:
0 verarbeitet, 1 Kommunikations-/Clientfehler (Ausgang eventuell unbekannt),
2 fehlgeschlagen/Sitzung beendet, 3 weiterhin angenommen. Timeout 1..600 Sekunden,
Standard 60. IDs vor dem Senden persistieren. Ein Timeout storniert keinen Auftrag.

Die Pipe erlaubt nur die Windows-SID des Spielprozesses. Die geschuetzte DACL
verweigert Netzwerk-Anmeldungen; zusaetzlich setzt die native Erstellung
`PIPE_REJECT_REMOTE_CLIENTS`. Grundlage:
[CreateNamedPipeW](https://learn.microsoft.com/en-us/windows/win32/api/namedpipeapi/nf-namedpipeapi-createnamedpipew)
und [Windows-Sicherheitsdeskriptoren](https://learn.microsoft.com/en-us/windows/win32/api/sddl/nf-sddl-convertstringsecuritydescriptortosecuritydescriptorw).
Unity-Mono implementiert `WindowsIdentity.User` nicht; daher wird die SID aus
dem Windows-Prozesstoken gelesen. Fehler fuehren zum Abbruch des Bridge-Starts,
nicht zu einer weniger geschuetzten Pipe. Der Sitzungsschluessel verhindert
veraltete IDs, ersetzt jedoch keine Benutzer-Authentifizierung. Andere Programme
desselben Windows-Benutzers sind innerhalb dieser Vertrauensgrenze.

`StateReporter/bridge-config.json` enthaelt anfangs `{"write_enabled":true}`.
`false` sperrt alle Spiel-Schreiboperationen. Die Datei wird etwa jede Sekunde
neu gelesen; ungueltige Konfiguration sperrt Schreiben. Bereits eingereihte
Scheduler-Befehle werden nicht zurueckgenommen. Lesen bleibt verfuegbar. Der Schalter wird bei Annahme und unmittelbar vor Ausfuehrung
eines Auftrags geprueft.

## Protokollversion 1

Jede Verbindung uebertraegt genau eine Anfrage und eine Antwort. Vier Bytes
unsigned Little Endian geben die Laenge des folgenden UTF-8-JSON in Bytes an.
Zulaessig sind 1..1.048.576 Bytes. Kein BOM und kein Zeilentrenner auf dem Draht.
Unvollstaendige Rahmen, ungueltiges UTF-8 und ungueltige Laengen schliessen die
Verbindung. JSON-Tiefe maximal 32, doppelte JSON-Schluessel werden abgelehnt.

```json
{"version":1,"session":"aus-discovery","id":"dauerhaft-eindeutig","operation":"game.pause","args":{"paused":true}}
```

Antwortfelder: `version`, `session`, `id`, `state`, `result`, `error`.
Zustaende: `accepted` (angenommen, noch kein Erfolg), `processed` (verarbeitet),
`failed` (Fehlertext), `session_ended` (kein weiterer Zugriff auf alte Sitzung).
`job.status` liefert die ID und Antwort des urspruenglichen Auftrags.

Gleiche ID plus gleicher kanonischer Inhalt liefert innerhalb einer Sitzung
denselben Auftrag/Endzustand. Andere Argumente oder Operation bei gleicher ID
werden abgelehnt. JSON-Objektreihenfolge ist dabei unerheblich. Nach Abbruch oder
Timeout erfolgt KEINE automatische erneute Einreichung. Sitzungswechsel erzeugt
neuen Schluessel und neue Auftragsliste. Entity-/Port-/Zug-IDs neu abfragen.

Maximal 2048 angenommene Auftraege pro Sitzung, 128 wartende Auftraege und 8 MiB
wartende Argumente. Es gibt keine heimliche Verdraengung alter IDs. Bei erreichtem
Limit wird vor Annahme abgelehnt. Ergebnisse im Sitzungsjournal sind auf 32 KiB
begrenzt: groessere Ergebnisse liefern `result_omitted:true` mit Erklaerung.
Kleinere Seiten verwenden; die Bridge erzeugt keinen Vollsnapshot. Control-Abfragen `bridge.status`,
`bridge.capabilities`, `job.status` verbrauchen keinen Journaleintrag und sind
aktuelle Abfragen, keine idempotent gespeicherten Spielauftraege.

Der Kommunikationsthread liest keine Spielobjekte. `UpdateBeforeCmdProc` des
Simulationsthreads arbeitet die Queue geordnet ab. Schreibbefehle laufen ueber
`IInputScheduler.ScheduleInputCmd`. Der naechste Auftrag beginnt erst nach
Verarbeitung des vorherigen. Kein Rollback und keine behauptete Gruppenatomizitaet.
Client-Trennung storniert angenommene Befehle nicht.

Scheduler-Antworten enthalten `command` (voller API-Typ), `result`,
`processed_at_step` und `effect_completion:command_processed_not_world_task_finished`.
Bool-/Entity-ID-/Tilelisten-Ergebnisse bleiben typisiert. Erfolgreiche Verarbeitung
bedeutet weder Fahrzeugankunft noch fertig gebautes Gebaeude. `game.save` wartet
zusaetzlich auf das Save-Ergebnis. Bei Sitzungsende werden offene Anfragen beendet,
Events abgemeldet und Verbindungen geschlossen. Laufzeitobjekte werden nicht
serialisiert; Event-Abonnements verwenden ausschliesslich `AddNonSaveable`.

## Argumentkonventionen

Unbekannte Felder werden abgelehnt, Zahlen werden nicht aus Strings konvertiert.
IDs sind Integer >=0, Prototyp-/Rezept-IDs sind exakte Strings aus dem Spiel.
Strings maximal 256 Zeichen. Positionen: `{x,y,z}`, XY-Positionen `{x,y}`;
x/y Integer 0..65535, z/Designation-Hoehen -1024..1024. Das sind Eingabegrenzen,
keine Garantie fuer Gueltigkeit auf der aktuellen Karte. Spielvalidierung gilt.
X/Y werden zusaetzlich auf die tatsaechlichen Kartenabmessungen begrenzt.
Transformation: `position`, optional `rotation` 0/90/180/270 (Standard 0),
`reflected` Boolean (Standard false). Prozentwerte sind Zahlen 0..1.
Seiten: optional `offset` ab0 und `limit` (Standard25, maximal100); Ergebnis
`items`, `total`, `next_offset` (null am Ende). Seiten sind Live-Abfragen und
keine ueber mehrere Aufrufe eingefrorene Datenbankansicht.

## Leseoperationen und Befehlsweg

- `bridge.status {}`: Schreibfreigabe, Queue, aktiver Auftrag, Journalgroesse.
- `bridge.capabilities {}`: explizite Operationen mit `writes_game` und konservativer
  Kennzeichnung `runtime_verified:false`. Pruefstand siehe [STATUS](../STATUS.md);
  die Kennzeichnung verspricht keine vollstaendige Validierung aller Spielzustaende.
- `job.status {request_id}`: gespeicherten Auftragszustand abfragen.
- `game.status {}`: Spielname, Pause, Geschwindigkeit, Simulationsschritt.
- `entities.list {offset?,limit?,prototype_id?}`: Entity-Inventar, Typ und Position.
- `entity.get {entity_id}`: Entity plus Maschinen-, Lager-, Status- und Portdaten
  mit gemeinsamer Capture-Zeit; nicht anwendbare Datenteile sind null.
- `prototypes.list {kind,offset?,limit?}`: IDs, Runtime-Typ und Freischaltung.
  kind: `building`, `transport`, `product`, `recipe`, `vehicle`, `designation`,
  `surface`, `surface_designation`.
- `recipes.list {machine_prototype_id,offset?,limit?}`: Rezept-Bindungen mit
  Inputs/Outputs, Grundmengen, Multiplikator, effektiver Dauer in Ticks;
  limit maximal50, Standard10.
- `trains.list {offset?,limit?}`: Zug-ID, Pause und Fahrmodus.
- `train_lines.list {offset?,limit?}`: Linien-ID, Name und Stationsanzahl.
## 1. Bauen und Abreissen

- `build.validate {prototype_id,position,rotation?,reflected?}`: Bauvorpruefung,
  Ergebnis `valid,error`; nur freigeschaltete, vom Spieler baubare LayoutEntityProto; Phantom-, obsolete, Sandbox- und automatisch vom Elternelement erzeugte Prototypen sind ausgeschlossen.
- `build.create`: gleiche Argumente, regulaerer Baugeist via `CreateStaticEntityCmd`.
- `build.batch {items:[{prototype_id,position,rotation?,reflected?,recipe_ids?}]}`:
  1..64 konfigurierte Gebaeude, maximal64 Rezepte je Maschine, unterstuetzte und
  freigeschaltete Rezepte. Einzelvorpruefung und `BatchCreateStaticEntitiesCmd`.
  Gegenseitige Kollisionen bleiben Aufgabe des Spielbefehls; Teilresultate moeglich.
- `deconstruct.validate {entity_id}`: `CanRemoveEntity` ohne Fehlerunterdrueckung.
- `deconstruct.start {entity_id}`: regulaerer `StartDeconstructionOfStaticEntityCmd`.
- `ghost.transform {entity_id,rotate?,flip?}`: `TryTransformEntityCmd`, nur ungebaut.
  Der Spielbefehl kann die Entity ersetzen; danach IDs neu abfragen.

## 2. Transport

- `transport.validate {prototype_id,pivots:[{x,y,z},...],start_direction?,end_direction?}`:
  2..256 vorgegebene Punkte, Richtungen `+x`,`-x`,`+y`,`-y`; Ergebnis
  `valid,error,blocking_entity_id`. `TransportsManager.CanBuildOrJoinTransport`.
- `transport.build`: gleiche Argumente, `BuildTransportCmd` mit regulaeren Kosten.
- `ports.compatible {port_id,other_port_id}`: `IoPort.CanConnectTo`, keine Routenpruefung.
- `transport.reverse {entity_id}`: `ReverseTransportCmd`.
- `transport.deconstruct {entity_id,start:{x,y,z},end:{x,y,z}}`:
  regulaerer `DeconstructTransportSegmentCmd`, kein Sofortabbau.

## 3. Rezepte

- `recipe.set {entity_id,recipe_id,enabled}`: `MachineSetRecipeActiveCmd`.
- `recipe.reorder {entity_id,old_index,new_index_after_remove}`: `ReorderRecipeCmd`,
  nullbasierte Indizes in zugewiesenen Rezepten.
- `boost.set {entity_id,enabled}`: prueft `IsBoostRequested` vor `MachineBoostToggleCmd`;
  bereits passender Zustand liefert `unchanged:true`.

## 4. Prioritaeten

- `priority.general {entity_id,priority}`: sichtbare allgemeine Prioritaet.
- `priority.construction {entity_id,priority}`: Bauprioritaet.
- `priority.custom {entity_id,priority_id,priority}`: sichtbare benannte Prioritaet.
- `priority.zipper {entity_id,port_name,prioritized}`: einstelliger Portname,
  `ZipperSetPriorityPortsCmd`.

Numerische Prioritaeten sind in Version0.6 auf1..15 begrenzt. Bedeutung folgt der
jeweiligen Spielprioritaet; unterschiedliche Domaenen nicht gleichsetzen.

## 5. Fahrzeuge und Zuege

- `vehicle.navigate {vehicle_id,position:{x,y}}`: `NavigateVehicleToPositionCmd`.
- `vehicle.assign {vehicle_id,entity_id}`: `AssignVehicleToEntityCmd`.
- `vehicle.unassign {vehicle_id}`: `UnassignVehicleCmd`.
- `vehicle.build {prototype_id,depot_id,count}`: `AddVehicleToBuildQueueCmd`, count1..100.
- `train.navigate {train_id,target_entity_id}`: `NavigateTrainToCmd`.
- `train.line {train_id,line_id}`: `AssignTrainLineToTrainCmd`, -1 hebt Zuweisung auf.

## 6. Designations

- `designation.add {prototype_id,items:[{origin:{x,y},height_origin,height_x,height_xy,height_y}]}`:
  1..256 Eintraege, `AddTerrainDesignationsCmd`, `IsDesignationAllowed`-Vorpruefung.
- `designation.remove {origins:[{x,y},...]}`: maximal256, `RemoveDesignationsCmd`.
- `surface.add {prototype_id,items:[{origin:{x,y},tiles_bitmap,surface_prototype_id}]}`:
  maximal256, `AddSurfaceDesignationsCmd`; prototype_id ist SurfaceDesignationProto,
  Oberflaeche ist TerrainTileSurfaceProto; 16-Bit-Tilemaske1..65535.
- `mine.area {entity_id,vertices:[{x,y},...]}`: 3..64 Eckpunkte,
  `MineTowerAreaChangeCmd` und `PolygonTerrainArea2i.TryCreate`. Bestehende Eingaben
  bleiben kompatibel; ungueltige Polygone werden vor dem Scheduling abgelehnt.

### Flaechenwerkzeuge ab 0.8.0 (Issue #10)

Freie **Baumfaell-, Bergbau-, Planierungs- und Verkippungsmarkierungen** sind
von den **Zustaendigkeitsgebieten der Forst-/Minentuerme** getrennt.
Die Bridge speichert keine Gebiete, Namen oder eigenen IDs; sie leitet die
Auftraege an das Spiel weiter. Native Markierungen koennen einander ueberschreiben.

Gemeinsame Auswahl fuer freie Markierungen:

```json
{"kind":"mining","shape":{"type":"rectangle","origin":{"x":100,"y":100},"size":{"x":8,"y":8}}}
```

Alternativ `shape: {type:"polygon", vertices:[{x,y},...]}` mit 3..64 Punkten
ohne wiederholten Schlusspunkt. Rechteckgroessen sind in Tiles; die obere Kante
ist `origin+size`. Koordinaten muessen innerhalb der Karte liegen.
Das native 4x4-Raster wird automatisch berechnet. Jede Zelle mit positiver
Flaechenueberschneidung wird einbezogen; reine Randberuehrung nicht. Daraus kann
eine Vergroesserung gegenueber der eingegebenen Flaeche entstehen.
Maximal 256 Rasterzellen pro Anfrage; groessere Flaechen explizit aufteilen.
Rastererweiterungen ueber die Kartengrenze werden abgelehnt.

- `designation.area.get {kind,shape}`: die ausgewaehlten Rasterzellen mit
  aktuellen Markierungen aller Typen; `designation:null` bedeutet keine Markierung.
- `designation.area.validate {kind,shape,target_height?,profile?,direction?}`:
  `valid`, `planned` mit Rasterurspruengen und vier Zielhoehen, `errors` und
  aktuelle `cells`. Eingabefehler schlagen wie bei anderen Operationen fehl.
- `designation.area.set` hat dieselben Argumente wie `.validate`. Erstellt oder
  ersetzt die ausgewaehlten Markierungen mittels `AddTerrainDesignationsCmd`.
- `designation.area.remove {kind,shape}`: entfernt ausschliesslich Markierungen
  des angegebenen Typs mittels `RemoveDesignationsCmd`. Andere Typen bleiben.

`kind` ist `forestry`, `mining`, `level` oder `dumping` und verwendet die offiziellen
`IdsCore.TerrainDesignators`-IDs. Bei Baumfaellmarkierungen (`forestry`) keine
Hoehenargumente angeben. Terrainauftraege benoetigen `target_height` (-1024..1024),
optional `profile` (`flat` als Standard, `ramp_up`, `ramp_down`). Rampen benoetigen
`direction` (`+x`, `-x`, `+y`, `-y`); bei ebenen Flaechen ist es nicht erlaubt.
Rampenhoehen berechnet `DesignationDataFactory.CreateArea` mit nativer Steigung.
`target_height` ist die Starthoehe an der in Rampenrichtung ersten Kante der
Raster-Bounding-Box; auch ausgesparte Polygonzellen behalten diesen gemeinsamen
Hoehenbezug. Die Vorpruefung liefert die vier konkreten Hoehen jeder Zelle.

Beispiel einer ebenen Planierungsmarkierung:

```json
{"kind":"level","shape":{"type":"rectangle","origin":{"x":100,"y":100},"size":{"x":8,"y":8}},"target_height":3,"profile":"flat"}
```

Bearbeiten bedeutet erneutes Setzen auf der ausgewaehlten Flaeche. Beim
Verschieben/Verkleinern muss die nicht mehr gewuenschte alte Flaeche explizit
entfernt werden. Es gibt keine automatisch verfolgte Gebietsidentitaet.

Turmgebiete separat:

- `mine.area {entity_id,vertices}` oder `{entity_id,shape}`: Minenturmgebiet ersetzen.
- `forestry.area {entity_id,vertices}` oder `{entity_id,shape}`: Forstturmgebiet ersetzen.
- `mine.area.get {entity_id}` / `forestry.area.get {entity_id}`: native Polygonpunkte
  und `is_empty` lesen.
- `mine.area.clear {entity_id}` / `forestry.area.clear {entity_id}`: leeres Gebiet
  ueber den jeweiligen AreaChange-Command setzen. Turm und freie Auftraege bleiben.

Schreibantworten enthalten `native` mit Command-Ergebnis und `observed` mit
anschliessend ausgelesenem Zustand. Freie Markierungen melden `requested_count`,
`matched_count`, `partial` und `cells`; Turmgebiete `area`, `matches_requested` und
`partial`. Teilweise uebernommene Aenderungen sind damit sichtbar. Ein Entfernen
ohne passende Markierungen liefert direkt Zaehler 0 und den Ist-Zustand.
`effect_completion` bleibt `command_processed_not_world_task_finished`:
Ein verarbeiteter Auftrag bestaetigt keine abgeschlossene Baumfaellung oder
Terrainarbeit. Schreibsperre, Sessionpruefung und Deduplication gelten weiterhin.

## 7. Lager und Logistik

- `storage.product {entity_id,product_id}`: `StorageSetProductCmd`.
- `storage.limits {entity_id,import_until?,export_from?,transport_from?,transport_until?}`:
  mindestens ein Verhaeltnis0..1, `StorageSetSliderStepCmd`; fehlende Werte bleiben.
- `storage.alert_threshold {entity_id,ratio,above}`: `StorageAlertSetThresholdCmd`.
- `storage.alert_enabled {entity_id,enabled,above}`: `StorageAlertSetEnabledCmd`.
- `logistics.assign {output_entity_id,input_entity_id}`: `AssignStaticEntityCmd`;
  Rollen entsprechen den API-Interfaces `IEntityAssignedAsOutput` und
  `IEntityAssignedAsInput`, nicht einer geratenen Transport-/Portorientierung.

## 8. Spielsteuerung

- `game.pause {paused}`: `SetSimPauseStateCmd`.
- `game.speed {multiplier}`: 1..3, `GameSpeedChangeCmd`.
- `entity.enabled {entity_id,enabled}`: `SetEntityEnabledCmd`, nur manuell pausierbare Entities.
- `game.save {name}`: `ISaveManager.RequestGameSave`, Abschluss ueber `OnSaveDone` /
  `SaveResult`; Ergebnis `file_path`, Fehler werden gemeldet. Gueltiger einfacher
  Dateiname ohne Verzeichnisse. Fuer Tests immer neuen Namen verwenden. Ein
  bestehender Name kann entsprechend Spielverhalten ueberschrieben werden.

## Entwicklerhinweise

Die Adapter verwenden die oeffentlichen APIs der lokal installierten
Spielassemblies. Eingaben, Prototypfreischaltungen und native Zulaessigkeit
werden vor dem Scheduling geprueft. Keine Cheats, kostenlose Erstellung,
Validierungsunterdrueckung oder direkten Command-Processor-Aufrufe.

Der Pipe-Thread verarbeitet nur Protokolldaten. Spielobjekte werden im
Simulationsthread ueber `UpdateBeforeCmdProc` aufgeloest; Spiel-Schreibbefehle
werden mit `IInputScheduler.ScheduleInputCmd` eingereiht. Ergebnisse erst
nach `IsProcessedAndSynced` lesen und `HasError`, `ErrorMessage` sowie
`GetResultObject()` auswerten. Verarbeitung und Abschluss der eigentlichen
Weltarbeit bleiben getrennte Zustaende.

Kameraobjekte ausschliesslich in `IGameLoopEvents.RenderUpdate` aufloesen und
bedienen. Der kurzlebige Kameraauftrag wird beim folgenden Renderupdate
abgeschlossen. Beim Entladen wartende Auftraege abbrechen und Event-Abonnements
entfernen; diese verwenden `AddNonSaveable` und werden nicht im Spielstand gespeichert.

Neue Operationen muessen Eingaben einschliesslich unbekannter Felder pruefen,
ihren Schreib-/Ansichtseffekt in den Capabilities ausweisen und die bestehende
Sessionpruefung, Deduplication und Reihenfolge weiterverwenden. Native Grenzen
und regulaere Spielkosten gelten weiterhin. Die Bridge pflegt keine eigene
Sollzustands- oder Planungsschicht.

## Pruefstand

Build und optionale Broker-/Pipe-Tests werden mitgeliefert. Eine erfolgreiche
Kompilierung allein bestaetigt keine Operation im Spiel. Bekannte Grenzen
und der kompakte Funktionsstand stehen in [STATUS](../STATUS.md).

# RechnungsTool

Standalone-Rechnungstool für Kleinunternehmer (§ 19 UStG) — .NET 10 + Avalonia, Daten als XML im Dateisystem, PDF-Export über ein HTML-Template (Headless-Chromium, komplett kostenlos/lokal, keine Cloud).

## Features

- Moderne UI auf Basis von **FluentAvalonia** (MIT): Karten-Layout, Icons, Akzent-Buttons
- **Rechnungsübersicht** links (Übernehmen-Button erscheint beim Hovern direkt am Eintrag), Editor mit **Live-DIN-A4-Vorschau** (side-by-side, abschaltbar) rechts; beim Start öffnet sich die neueste Rechnung
- **Stammdatenpflege** (Unternehmen, Steuer, Bank, Nummernkreis) → `stammdaten.xml`
- Zeilenbasierte **Leistungspositionen** (Text, Menge, Einheit, Nettopreis)
- **Kleinunternehmerregelung** fest verdrahtet: keine USt, Pflichthinweis nach § 19 UStG auf jeder Rechnung
- **Fortlaufende Rechnungsnummern** im Format `JJJJ-NN`; Startwert (letzte vor Einführung des Tools vergebene Nummer) in den Stammdaten konfigurierbar; Nummer in der Maske nachträglich editierbar
- **Validierung**: Pflichtangaben nach § 14 UStG blockieren den PDF-Export (✖), Nicht-Eindeutigkeit blockiert auch das Speichern; „nicht fortlaufend“ und Formatabweichungen sind Warnungen (⚠)
- **Rechnung übernehmen**: vorhandene Rechnung als Vorlage für eine neue kopieren
- **PDF-Export** nach `<Datenordner>/pdf/`, anschließend Anzeige im Finder

## Datenablage (Konvention)

Alle Daten liegen als XML in einem konfigurierbaren Ordner (lokal oder Share):

```
<Datenordner>/
├── stammdaten.xml          # genau eine
├── rechnung-2026-19.xml    # eine Datei pro Rechnung
├── rechnung-2026-20.xml
└── pdf/                    # exportierte PDFs
```

Andere XML-Dateien sind dort **nicht erlaubt** — die App zeigt beim Start ein Warnbanner.
Nicht parsebare `rechnung-*.xml` erscheinen trotzdem in der Übersicht (rot markiert) mit Fehlerdetails.

## Konfiguration

`~/Library/Application Support/RechnungsTool/config.json`

```json
{
  "DatenOrdner": "~/Documents/Rechnungen"
}
```

Die Datei wird beim ersten Start automatisch angelegt; `~` wird expandiert, Netzwerk-Shares (z. B. `/Volumes/...`) funktionieren genauso. Der Datenordner lässt sich auch direkt in der App ändern: Zahnrad-Icon oben rechts → Einstellungen (schreibt sofort in die Config und liest den Ordner neu ein).

## PDF-Erzeugung

Das Layout liegt in [src/RechnungsTool/Assets/RechnungTemplate.html](src/RechnungsTool/Assets/RechnungTemplate.html) (One-Pager, DIN A4, alle Styles inline) und ist in die App **eingebettet**. Zum Anpassen ohne Rebuild eine Kopie nach `~/Library/Application Support/RechnungsTool/RechnungTemplate.html` legen — die hat Vorrang. Gerendert wird mit **PuppeteerSharp** (MIT-Lizenz): Beim ersten Export/der ersten Vorschau wird einmalig ein Headless-Chromium (~170 MB) nach `~/Library/Application Support/RechnungsTool/chromium` geladen — danach läuft alles offline.

Pflichtangaben nach § 14 Abs. 4 UStG sind im Template abgedeckt: Name/Anschrift beider Parteien, Steuernummer bzw. USt-IdNr., Ausstellungsdatum, fortlaufende Rechnungsnummer, Menge/Art der Leistung, Leistungszeitraum, Entgelt sowie der Hinweis auf § 19 UStG (keine E-Rechnung nötig, PDF genügt für Kleinunternehmer an inländische Empfänger).

## Entwicklung & Build

```bash
dotnet run --project src/RechnungsTool          # App starten
dotnet build RechnungsTool.slnx                 # bauen

build/publish-macos.sh                          # self-contained .app für Apple Silicon
build/publish-macos.sh osx-x64                  # … für Intel
```

In VSCode: „Tasks: Run Task“ → **publish-macos** (ruft dasselbe Skript auf).

## Release

Tag pushen → GitHub Action baut arm64- und Intel-Bundles und veröffentlicht sie als GitHub-Release:

```bash
git tag v1.0.0 && git push origin v1.0.0
```

Installation auf einem anderen Mac: Zip aus dem Release laden, `RechnungsTool.app` nach Programme ziehen und einmalig `xattr -cr /Applications/RechnungsTool.app` ausführen (die App ist ad-hoc-signiert, nicht notarisiert — sonst blockt Gatekeeper).

Output: `dist/<rid>/RechnungsTool.app` (ad-hoc signiert, per `open` startbar).

# Lieblingstypen OBS Remote

Eine native Windows-Anwendung zur **Fernsteuerung von OBS Studio** — auch über das Internet (z. B. via Tailscale zu einem OBS auf einem anderen PC). Im Lieblingstypen-Style.

![Screenshot Placeholder](docs/screenshot.png)

## Features

- **Multi-OBS-Verwaltung** — beliebig viele OBS-Verbindungen mit Name, Host, Port, Passwort speichern; per Klick wechseln
- **Szenen-Wechsel** mit Live-Anzeige der aktuell aktiven Szene
- **4 Favoriten-Slots** für Schnellzugriff direkt im Hauptbereich (per ⭐-Klick zu Favoriten machen)
- **Aufnahme-Steuerung** — Start / Stop, Pause / Fortsetzen mit Live-Counter
- **Stream-Steuerung** — Start / Stop mit Live-Counter
- **Audio-Pegel-Meter** pro Audio-Quelle mit Peak-Hold, Mute, +/− 3 dB, OBS-Style Lautstärke-Fader
- **Mein-Mikro-Schnellzugriff** — eine Audioquelle als "Mein Mikro" markieren, dicker Mute-Button oben rechts
- **Live-Vorschau** der aktiven Szene (2 FPS Screenshot-Polling, klick zum Vergrößern)
- **Globale Hotkeys** — funktionieren auch wenn ein anderes Programm (z. B. Vollbild-Spiel) den Fokus hat. Pro Verbindung konfigurierbar
- **Performance-Stats** — RAM, CPU, Netzwerk-Bandbreite der App selbst sichtbar
- **JSON-Export / Import** — alle Verbindungen, Favoriten, Hotkeys etc. in einer Datei sichern und auf einen anderen PC mitnehmen
- **Neueste YouTube-Videos** vom Channel als klickbare Thumbnail-Reihe

## Installation

### Variante A: Fertige .exe (empfohlen)

1. Auf der [Releases-Seite](#releases) die aktuelle `LieblingstypenRemote.exe` herunterladen
2. Doppelklick → läuft

Die .exe ist self-contained, braucht **kein .NET Install** und **kein WebView2-Install** (WebView2 ist auf Windows 11 bereits da; auf älteren Windows lädt der WebView2-Installer beim ersten Start nach).

### Variante B: Aus Source bauen

Voraussetzung: [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

```bash
git clone https://github.com/USER/lieblingstypen-obs-remote.git
cd lieblingstypen-obs-remote/obs-remote-exe
dotnet publish -c Release -r win-x64 --self-contained=true -p:PublishSingleFile=true
```

Die fertige `.exe` liegt dann in `obs-remote-exe/bin/Release/net8.0-windows/win-x64/publish/`.

## Verwendung

### OBS-WebSocket aktivieren

In OBS Studio:
1. Werkzeuge → **WebSocket-Servereinstellungen**
2. Häkchen bei "WebSocket-Server aktivieren"
3. Port merken (Standard 4455) und Passwort setzen

### Erstmals verbinden

1. App starten
2. "+ NEUE VERBINDUNG" → Name (z. B. "Mein OBS"), Host (`localhost` oder Tailscale-IP des Remote-PCs), Port, Passwort
3. SPEICHERN & VERBINDEN → fertig

### Hotkeys einrichten

1. Verbunden? Dann oben auf **⌨ HOTKEYS**
2. **+ HOTKEY HINZUFÜGEN** → Aktion wählen (Szenenwechsel / Aufnahme / Stream / Mute / Lautstärke / Favorit 1-4) → bei Szenenwechsel Szene aus Dropdown wählen
3. **AUFNEHMEN** klicken → gewünschte Taste(n) drücken (F13-F24 empfohlen, kollidieren nicht mit Spielen)
4. **FERTIG** → Hotkey ist sofort global aktiv

## Remote-Steuerung (z. B. zum Kumpel)

1. [Tailscale](https://tailscale.com) auf beiden PCs installieren (gratis, ~5 Minuten)
2. OBS beim anderen PC starten → WebSocket aktivieren, "An allen Interfaces lauschen" einstellen
3. In dieser App: neue Verbindung mit Tailscale-IP des anderen PCs (z. B. `100.x.x.x`)
4. Connect — fertig

## Tech-Stack

- **C# + WPF + WebView2** (.NET 8) — natives Windows-Fenster mit eingebetteter Webview
- **HTML / CSS / Vanilla JS** — UI, kein Framework
- **[obs-websocket-js](https://github.com/obs-websocket-community-projects/obs-websocket-js)** — OBS WebSocket v5 Client
- **Win32 RegisterHotKey** via P/Invoke — globale Hotkeys

## Architektur

```
┌─────────────────────────────────────┐
│  LieblingstypenRemote.exe (C# WPF)  │
│                                     │
│  ┌──────────────────────────────┐   │
│  │  WebView2 (Chromium)         │   │
│  │  ┌─────────────────────────┐ │   │
│  │  │  index.html (Webapp)    │ │   │
│  │  │  obs-websocket-js       │ │   │
│  │  └──────┬──────────────────┘ │   │
│  └─────────┼────────────────────┘   │
│            │ JS↔C# Bridge           │
│            ↓                        │
│  ┌──────────────────────────────┐   │
│  │  GlobalHotkeyManager (Win32) │   │
│  │  YouTubeService (HTTP)       │   │
│  │  Process Stats               │   │
│  └──────────────────────────────┘   │
└──────────┬──────────────────────────┘
           │ WebSocket
           ↓
   ┌─────────────────┐
   │  OBS Studio     │
   │  (lokal oder    │
   │   via Tailscale)│
   └─────────────────┘
```

## Konfiguration

Alle Einstellungen werden im WebView2-User-Data-Folder gespeichert:
```
<exe-Folder>/userdata/WebView2/Default/Local Storage/leveldb/
```

Für Backup oder Übertragung auf einen anderen PC: in der App **JSON EXPORT** → ergibt eine `obs-connections.json` mit allen Verbindungen + deren Favoriten, Hotkeys, Mic-Auswahl etc.

## Lizenz

[MIT](LICENSE)

## Credits

Built for [Lieblingstypen](https://www.youtube.com/@lieblingstypen).

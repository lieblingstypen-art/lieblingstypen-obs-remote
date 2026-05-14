@echo off
REM Startet die Lieblingstypen OBS Remote als eigenstaendige App (ohne Browser-Leiste).
REM Nutzt Edge im --app= Modus: chromeless Window, eigenes Icon in der Taskleiste,
REM keine bestehende Browser-Instanz noetig.

set "HTML=%~dp0index.html"
set "URL=file:///%HTML:\=/%"

REM Eigenes User-Data-Verzeichnis, damit die App ihre eigene Session/LocalStorage hat
set "PROFILE=%~dp0.edge-profile"

start "" "msedge.exe" ^
  --app="%URL%" ^
  --user-data-dir="%PROFILE%" ^
  --window-size=1920,1080 ^
  --window-position=1920,0 ^
  --no-first-run ^
  --no-default-browser-check ^
  --disable-features=msEdgeSidebar

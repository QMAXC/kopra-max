# Anleitung: Kobra Max Steuerung

Mit diesem Programm steuerst du den **Anycubic Kobra Max** über das USB-Kabel:
heizen, fahren und G-Code-Dateien drucken. Du musst nichts installieren und brauchst keine Admin-Rechte.

## 1. Einmalig: Programm installieren

**Am einfachsten:** Windows-Taste drücken, `cmd` eingeben, Enter. Dann diese Zeile einfügen
(Rechtsklick fügt ein) und Enter drücken:

```bat
mkdir "H:\3d druk" 2>nul & curl -fL -o "%TEMP%\KobraMax.zip" https://github.com/QMAXC/kopra-max/releases/latest/download/KobraMax-win-x64.zip && tar -xf "%TEMP%\KobraMax.zip" -C "H:\3d druk" && start "" "H:\3d druk\KobraMax\KobraMax.exe"
```

Du siehst kurz einen Download-Balken, dann startet die App und legt **Kobra Max Steuerung** auf den
Desktop. Für ein Update: App schließen und denselben Befehl noch einmal ausführen.

> Kommt bei `curl` oder `tar` „Zugriff verweigert" oder „nicht gefunden", nimm den Weg von Hand.

**Oder von Hand:**

1. Lade **`KobraMax-win-x64.zip`** herunter
   (GitHub → *Releases* → neueste Version → `KobraMax-win-x64.zip`).
2. Rechtsklick auf die ZIP-Datei → **Alle extrahieren …**
3. Als Ziel **`H:\3d druk\`** eingeben → **Extrahieren**.
   Danach gibt es den Ordner `H:\3d druk\KobraMax\`.
4. Im Ordner `KobraMax` Rechtsklick auf **`KobraMax.exe`** → **Weitere Optionen anzeigen** →
   **Senden an** → **Desktop (Verknüpfung erstellen)**.

> Wichtig: Die `.exe` braucht die anderen Dateien im Ordner. Nicht nur die `.exe` allein kopieren!

## 2. Drucken

1. Drucker einschalten und mit dem USB-Kabel an den Laptop anschließen.
2. **KobraMax** auf dem Desktop doppelklicken.
3. Bei *Verbindung* sollte schon **COM… – USB-SERIAL CH340** ausgewählt sein.
   Steht da nichts: Kabel prüfen und auf **Aktualisieren** klicken.
4. **Verbinden** klicken. Der Drucker startet kurz neu (3 Sekunden), dann wird der Punkt **grün**
   und die Temperaturen erscheinen.
5. **Datei öffnen …** → deine `.gcode`-Datei wählen (das Fenster öffnet in `H:\3D`).
6. **Start** klicken. Der Balken zeigt den Fortschritt.

Während des Drucks:

- **Pause** hält an, **Weiter** macht weiter.
- **Stopp** bricht ab, schaltet die Heizung aus und fährt die Düse 10 mm hoch.
- Der Laptop geht während des Drucks nicht in den Ruhezustand. **Deckel trotzdem offen lassen**
  und das Kabel nicht abziehen!

## 3. Eigene Temperaturen

- Zahl eingeben und **Heizen** klicken: Der Drucker heizt sofort, und **beim Druck wird dieser Wert
  statt dem aus der Datei benutzt** („Beim Druck: 200 °C").
- **Aus** schaltet die Heizung aus. Beim Druck gelten dann wieder die Werte aus der Datei.
- Grenzen: Düse 1–260 °C, Bett 1–110 °C. Für PLA: Düse 190–220 °C, Bett 50–60 °C.
- **Temperaturturm** drucken? Vorher bei Düse **und** Bett auf **Aus** klicken, sonst werden die
  Temperaturstufen überschrieben.

## 4. Konsole

Unten kannst du G-Code-Befehle direkt senden (Enter drückt „Senden"). Kleinbuchstaben werden
automatisch groß geschrieben. Beispiele:

| Befehl | Bedeutung |
|---|---|
| `M105` | Temperatur abfragen |
| `G28` | Alle Achsen nach Hause fahren |
| `M84` | Motoren ausschalten (Achsen von Hand bewegen) |
| `M106 S255` / `M107` | Lüfter an / aus |

## 5. Probleme?

| Meldung | Lösung |
|---|---|
| *Port belegt – ist das Programm noch woanders offen?* | Anderes KobraMax-Fenster (oder Cura/Python) schließen, dann **Verbinden**. |
| *Drucker antwortet nicht …* | Drucker an? USB-Kabel ab- und wieder anstecken, **Aktualisieren**, **Verbinden**. |
| *Kein Port gefunden* | Kabel steckt nicht richtig oder Drucker ist aus. |
| *Das ist eine .3mf-Projektdatei …* | Im Slicer **G-Code exportieren** und die `.gcode`-Datei öffnen. |
| Windows sagt „Der Computer wurde durch Windows geschützt" | **Weitere Informationen** → **Trotzdem ausführen**. Geht das nicht, die Lehrkraft fragen. |

Ohne Drucker ausprobieren: In der Eingabeaufforderung `KobraMax.exe --demo` starten – dann läuft ein
simulierter Drucker.

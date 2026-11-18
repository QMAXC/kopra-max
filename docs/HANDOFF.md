# Handoff: Kobra Max Steuerung als native WinUI-3-App

> Dieses Dokument ist für **Claude Code**. Es enthält alles, was nötig ist, um die bestehende
> Python-App „Kobra Max Steuerung" als native Windows-App (WinUI 3, C#) neu zu bauen.
> Die vollständige Referenz-Implementierung in Python steht am Ende (Anhang A).

---

## 1. Ziel

Eine **native Windows-Desktop-App** zum Steuern eines **Anycubic Kobra Max** über USB (serieller Port),
die die bestehende Python/Tkinter-App 1:1 ersetzt, ohne Python.

**Harte Anforderungen des Nutzers**
- **WinUI 3** (Windows App SDK), Sprache **C#**, **typsicher** (Nullable aktiviert, keine `dynamic`, Warnungen als Fehler).
- Ergebnis ist eine **.exe, die man doppelklickt**.
- Läuft auf **Schul-Laptops ohne Admin-Rechte und ohne Installation** → siehe Abschnitt 3.
- Oberfläche und Meldungen auf **Deutsch**.

---

## 2. Hardware & Umgebung (verifiziert)

| Punkt | Wert |
|---|---|
| Drucker | Anycubic Kobra Max, Marlin-Firmware, Bowden, 1,75 mm Filament, 0,4-mm-Düse |
| USB-Chip | **CH340**, erscheint als `USB-SERIAL CH340 (COM3)` (Nummer kann wechseln!) |
| Baudrate | **115200** funktioniert (Fallback 250000 anbieten) |
| Zeilenende | `\n` |
| Befehle | **Nur Großbuchstaben**: `m105` → `echo:Unknown command: "m105"` |
| Temperatur-Antwort | `ok T:230.16 /230.00 (203.50) B:60.01 /60.00 (3427.88) @:42 B@:23` |
| Reset beim Öffnen | Drucker startet beim Öffnen des Ports neu → **3 s warten**, dann Eingangspuffer leeren |
| Schul-PCs | Windows 11 (Build 26100), keine Admin-Rechte, persönliches Netzlaufwerk `H:\` |
| Ablageort | `H:\3d druk\` (App) und `H:\3D\` (Testdateien, im Schnellzugriff) |

**Fehler, die real aufgetreten sind und sauber behandelt werden müssen**
- `PermissionError(13, 'Zugriff verweigert')` → Port noch von einem anderen Prozess belegt
  (z. B. altes Fenster). Meldung: „Port belegt – ist das Programm noch woanders offen?"
- `OSError(22, 'Das Zeitlimit für die Semaphore wurde erreicht.', 121)` → Drucker aus oder USB hängt.
  Meldung: „Drucker antwortet nicht – eingeschaltet? Kabel ab- und wieder anstecken, dann Aktualisieren."
- Beim Schließen des Fensters **muss der Port freigegeben werden** (sonst Fehler 13 beim Neustart).

---

## 3. Build & Auslieferung (wichtig!)

- WinUI 3 lässt sich **nur unter Windows** bauen (.NET 8 SDK + Windows App SDK; Visual Studio optional,
  `dotnet build` reicht mit den Projekt-Templates).
- Auf den Schul-PCs darf nichts installiert werden → **unpackaged + self-contained**:
  ```xml
  <OutputType>WinExe</OutputType>
  <TargetFramework>net8.0-windows10.0.19041.0</TargetFramework>
  <TargetPlatformMinVersion>10.0.17763.0</TargetPlatformMinVersion>
  <UseWinUI>true</UseWinUI>
  <WindowsPackageType>None</WindowsPackageType>
  <WindowsAppSDKSelfContained>true</WindowsAppSDKSelfContained>
  <SelfContained>true</SelfContained>
  <RuntimeIdentifier>win-x64</RuntimeIdentifier>
  <Nullable>enable</Nullable>
  <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  ```
- Ausgabe: Ordner mit `KobraMax.exe` + DLLs. Diesen Ordner als ZIP ausliefern; der Nutzer entpackt ihn
  nach `H:\3d druk\KobraMax\` und legt eine Desktop-Verknüpfung auf die .exe.
  (Single-File-Publish ist bei WinUI 3 heikel – nur nutzen, wenn es nachweislich startet.)
- Serieller Port: NuGet `System.IO.Ports`.
- Risiko: Schul-PCs könnten unsignierte .exe-Dateien blockieren (AppLocker/SmartScreen).
  Python lief dort; ob eine .exe läuft, muss getestet werden. Fallback: die Python-Version (Anhang A) bleibt nutzbar.

---

## 4. Funktionen (alle aus der Python-Version übernehmen)

### 4.1 Verbindung
- Dropdown mit allen COM-Ports inkl. Beschreibung (z. B. „COM3 – USB-SERIAL CH340"),
  Port mit „CH340" automatisch vorauswählen. Beschreibung per WMI (`Win32_PnPEntity`) oder Registry.
- Knöpfe **Aktualisieren**, **Verbinden/Trennen**, Status-Anzeige (rot „getrennt" / grün „verbunden").
- Nach dem Öffnen: 3 s warten („Verbinde … Drucker startet neu"), Puffer leeren.

### 4.2 Temperatur
- Anzeige **Düse ist/soll** und **Bett ist/soll**, alle 3 s per `M105` aktualisiert (nur wenn Warteschlange leer).
- Eingabefelder (Vorgabe Düse 200, Bett 60) + **Heizen** (`M104 S<n>` / `M140 S<n>`) + **Aus** (`S0`).
- Grenzen: Düse 1–260 °C, Bett 1–110 °C; nur ganze Zahlen. Fehleingaben ablehnen.
- **Temperatur-Override (wichtig, vom Nutzer gewünscht):**
  - **Heizen** merkt sich den Wert als „eigene Temperatur". Label daneben: „Druck: 200 °C".
  - **Aus** löscht den eigenen Wert. Label: „Druck: Wert aus Datei".
  - Beim Druck werden Heizbefehle aus der Datei umgeschrieben:
    - Düse: `M104`, `M109`, `G10`, `M568`; Bett: `M140`, `M190`
    - ersetzt wird der erste `S`- **oder** `R`-Parameter (z. B. `M109 R170` kommt real vor!)
    - **nicht** ersetzen, wenn der Wert 0 ist (Heizung aus am Druckende muss bleiben)
    - Groß/Kleinschreibung egal, optionale Zeilennummer `N123` davor erlauben
    - Konsole loggt: „Düse: Datei wollte 170 °C, nutze 200 °C"
  - **Wächter:** Während des Drucks bei jeder Temperaturmeldung prüfen: Ist das gemeldete **Soll** ≠ eigener Wert
    (Toleranz 1 °C) und Soll > 0, dann `M104/M140 S<eigener Wert>` in die Warteschlange.
    Pro Heizer höchstens alle 5 s. Log: „Wächter: Ziel war 170 °C, stelle zurück auf 200 °C".

### 4.3 Bewegung
- Knöpfe X−, X+, Y−, Y+, Z−, Z+, **Home** (`G28`), Schrittweite 1 / 10 / 50 mm.
- Fahren relativ: `G91`, `G1 X10 F3000` (Z: `F600`), `G90`.
- **Während eines Drucks gesperrt** (Log-Meldung).

### 4.4 Druck
- **Datei öffnen**: Dateidialog, Filter `*.gcode;*.gco;*.g`. Startordner bevorzugt `H:\3D`, sonst Ordner der .exe.
  (Der Nutzer fand die Dateien sonst nicht.)
- Laden: Kommentare ab `;` entfernen, Leerzeilen weg, `N123` vorne und `*45` hinten entfernen.
- Warnung, wenn Datei auf `.3mf` endet (das ist kein reiner G-Code).
- **Start**, **Pause/Weiter**, **Stopp**; Fortschrittsbalken + „123 / 4567 Zeilen (2 %)".
- Stopp: Druck abbrechen, dann `M104 S0`, `M140 S0`, `G91`, `G1 Z10 F600`, `G90`.
- Druck-Zeilen nicht einzeln loggen (zu viele), manuelle Befehle schon.
- Beim Schließen während eines Drucks: Rückfrage.
- Rechner darf während des Drucks nicht schlafen: `SetThreadExecutionState(ES_CONTINUOUS | ES_SYSTEM_REQUIRED)` während Druck.

### 4.5 Konsole
- Log-Fenster (max. ~500 Zeilen), Eingabefeld + **Senden** (Enter sendet), Eingabe automatisch in Großbuchstaben.
- Antworten des Druckers mit `< ` anzeigen, eigene Befehle mit `> `. Reine `ok` und Temperaturzeilen nicht loggen.

---

## 5. Protokoll (exakt so funktioniert es)

- **Ein Befehl, dann auf `ok` warten** (Ping-Pong). Erst nach `ok` den nächsten senden.
- Zeilen, die mit `Error` beginnen: Befehl gilt als erledigt; läuft ein Druck → Druck abbrechen + loggen.
- `echo:busy: processing` / Temperaturzeilen während `M109`/`M190` sind normal – weiter auf `ok` warten.
- Priorität: manuelle Befehle (Warteschlange) **vor** der nächsten Druckzeile.
- Leerlauf: weiter lesen, damit Druckermeldungen erscheinen.
- Temperatur-Regex: `T:\s*([\d.]+)\s*/\s*([\d.]+).*?B:\s*([\d.]+)\s*/\s*([\d.]+)`

---

## 6. Architektur-Vorschlag (typsicher, testbar)

```
KobraMax.sln
├─ KobraMax.Core/            (net8.0, KEINE UI-Abhängigkeit → per dotnet test testbar)
│   ├─ ISerialTransport.cs        // WriteLine, ReadLine(timeout), Open/Close
│   ├─ SerialPortTransport.cs     // System.IO.Ports-Implementierung
│   ├─ GcodeFile.cs               // Laden & Säubern
│   ├─ TemperatureOverride.cs     // Umschreiben von Heizbefehlen (reine Funktion)
│   ├─ TemperatureReport.cs       // record struct (NozzleIst, NozzleSoll, BedIst, BedSoll) + Parser
│   ├─ PrinterSession.cs          // Hintergrund-Thread/Task: Warteschlange, Ping-Pong, Druck, Wächter
│   └─ PrinterEvent.cs            // abstrakte record-Hierarchie: Log, Temperature, Status, Progress, PrintEnded
├─ KobraMax.App/             (WinUI 3, MVVM mit CommunityToolkit.Mvvm)
│   ├─ MainWindow.xaml(.cs)
│   └─ MainViewModel.cs           // Events per DispatcherQueue auf den UI-Thread
└─ KobraMax.Tests/           (xUnit) mit FakeSerialTransport (simulierter Marlin)
```

- Events als `abstract record PrinterEvent` mit Unterklassen statt Strings + object (Python-Version ist da nicht typsicher).
- `CancellationToken` für Trennen/Schließen.

### Pflicht-Tests (alle in der Python-Version real geprüft)
1. `M140 S80` → `M140 S50`, `M104 S170 T0` → `M104 S200 T0`, `M109 R170` → `M109 R200` (bei Override 200/50).
2. `M104 S0` bleibt unverändert.
3. `m104 s170` (klein) und `N5 M109 S170*12` werden erkannt (nach Säubern).
4. Ohne Override bleibt alles unverändert.
5. Wächter: Fake meldet Soll 170 bei Override 200 → genau ein `M104 S200` innerhalb 5 s.
6. Ping-Pong: nächste Zeile erst nach `ok`; `Error:` bricht Druck ab.
7. Manueller Befehl wird vor der nächsten Druckzeile gesendet.
8. Unbekannter Befehl → `echo:Unknown command` wird geloggt.

---

## 7. Beispiel-G-Code des Nutzers (aus seinem Slicer, `ggggg.gcode`)

```
1154: M104 S170 ; set extruder temp for bed leveling
1156: M109 R170 ; wait for bed leveling temp
1162: M104 S240 ; set extruder temp
1167: M109 S240 ; wait for extruder temp
1647: M104 S245 ; set temperature
49962: M104 S0 ; turn off temperature
```
Hinweis: 170 °C ist absichtlich (Bett-Leveln). Der Nutzer druckt PLA (190–220 °C, Bett 50 °C).

---

## 8. Akzeptanzkriterien

- [ ] `dotnet build` ohne Warnungen; `dotnet test` grün.
- [ ] Ausgelieferter Ordner startet per Doppelklick auf einem Windows-PC **ohne** installiertes .NET/Windows App SDK.
- [ ] Verbinden mit COM3 → nach ~3 s grün, Temperaturen erscheinen.
- [ ] Override + Wächter verhalten sich wie in 4.2.
- [ ] Fenster schließen → Port sofort wieder frei (zweiter Start verbindet ohne Fehler 13).
- [ ] Kurze Anleitung (`ANLEITUNG.md`, Deutsch, für einen Schüler) zum Entpacken und Starten.

---

## Anhang A: Referenz-Implementierung (Python, funktioniert real am Drucker)

Die Datei `drucker_gui.py` liegt diesem Handoff bei. Sie ist die Wahrheit für das Verhalten.

```python
import queue, re, threading, time
import tkinter as tk
from tkinter import ttk, filedialog, scrolledtext
import serial
from serial.tools import list_ports

BAUD = 115200
TEMP_RE = re.compile(r"T:\s*([\d.]+)\s*/\s*([\d.]+).*?B:\s*([\d.]+)\s*/\s*([\d.]+)")
WERT_RE = re.compile(r"(?<![A-Za-z])([SR])([\d.]+)", re.I)
BEFEHL_RE = re.compile(r"\s*(?:N\d+\s*)?([GMT]\d+)", re.I)
MAX_DUESE, MAX_BETT = 260, 110


class Drucker:
    """Spricht in einem eigenen Thread mit dem Drucker."""

    def __init__(self, melde):
        self.melde = melde            # melde(typ, wert) -> Nachricht an die Oberflaeche
        self.ser = None
        self.befehle = queue.Queue()  # Befehle von Hand / von den Knoepfen
        self.zeilen = []              # geladene G-Code-Datei
        self.pos = 0
        self.druckt = False
        self.pause = False
        self.aktiv = False
        self.ziel_duese = None        # eigene Temperaturen, ersetzen die Werte aus der Datei
        self.ziel_bett = None
        self.letzte_korrektur = {"duese": 0.0, "bett": 0.0}

    def anpassen(self, zeile):
        """Ersetzt Heiz-Befehle aus der Datei durch die eigenen Werte."""
        m = BEFEHL_RE.match(zeile)
        befehl = m.group(1).upper() if m else ""
        if befehl in ("M104", "M109", "G10", "M568"):
            wert, name = self.ziel_duese, "Düse"
        elif befehl in ("M140", "M190"):
            wert, name = self.ziel_bett, "Bett"
        else:
            return zeile
        m = WERT_RE.search(zeile)
        if wert is None or not m or float(m.group(2)) == 0:
            return zeile          # kein eigener Wert gesetzt oder "Heizung aus"
        neu = zeile[:m.start(2)] + str(wert) + zeile[m.end(2):]
        self.melde("log", f"{name}: Datei wollte {m.group(2)} °C, nutze {wert} °C")
        return neu

    def waechter(self, werte):
        """Stellt beim Druck die eigenen Temperaturen wieder her, falls die Datei sie geaendert hat."""
        if not self.druckt:
            return
        jetzt = time.monotonic()
        for name, ziel, ist_ziel, code in (("duese", self.ziel_duese, werte[1], "M104"),
                                           ("bett", self.ziel_bett, werte[3], "M140")):
            if ziel and ist_ziel > 0 and abs(ist_ziel - ziel) > 1 \
                    and jetzt - self.letzte_korrektur[name] > 5:
                self.letzte_korrektur[name] = jetzt
                self.melde("log", f"Wächter: Ziel war {ist_ziel:.0f} °C, stelle zurück auf {ziel} °C")
                self.befehle.put(f"{code} S{ziel}")

    def verbinde(self, port):
        self.aktiv = True
        threading.Thread(target=self._lauf, args=(port,), daemon=True).start()

    def trenne(self):
        self.aktiv = False

    def starte_druck(self):
        self.pos = 0
        self.pause = False
        self.druckt = True

    def stopp(self):
        self.druckt = False
        self.pause = False
        for b in ("M104 S0", "M140 S0", "G91", "G1 Z10 F600", "G90"):
            self.befehle.put(b)

    def _lauf(self, port):
        try:
            self.ser = serial.Serial(port, BAUD, timeout=0.2)
        except Exception as e:
            self.aktiv = False
            self.melde("log", f"Verbindung fehlgeschlagen: {e}")
            self.melde("status", False)
            return
        self.melde("log", "Verbinde ... (Drucker startet neu, 3 Sekunden)")
        time.sleep(3)
        try:
            self.ser.reset_input_buffer()
            self.melde("status", True)
            while self.aktiv:
                befehl = None
                try:
                    befehl = self.befehle.get_nowait()
                except queue.Empty:
                    if self.druckt and not self.pause:
                        if self.pos < len(self.zeilen):
                            befehl = self.anpassen(self.zeilen[self.pos])
                            self.pos += 1
                            self.melde("fortschritt", (self.pos, len(self.zeilen)))
                        else:
                            self.druckt = False
                            self.melde("log", "Druck fertig!")
                            self.melde("druck_ende", None)
                if befehl is None:
                    self._lies_zeile()   # Leerlauf: Meldungen des Druckers anzeigen
                else:
                    self._sende(befehl)
        except Exception as e:
            self.melde("log", f"Verbindung verloren: {e}")
        finally:
            try:
                self.ser.close()
            except Exception:
                pass
            was_printing = self.druckt
            self.druckt = False
            self.aktiv = False
            if was_printing:
                self.melde("druck_ende", None)
            self.melde("status", False)

    def _sende(self, befehl):
        aus_datei = self.druckt and not self.pause and self.befehle.empty()
        if befehl != "M105" and not aus_datei:
            self.melde("log", "> " + befehl)
        self.ser.write((befehl + "\n").encode())
        while self.aktiv:
            if self._lies_zeile():
                return

    def _lies_zeile(self):
        """Liest eine Zeile. Gibt True zurueck, wenn der Befehl erledigt ist."""
        z = self.ser.readline().decode(errors="ignore").strip()
        if not z:
            return False
        m = TEMP_RE.search(z)
        if m:
            werte = tuple(float(x) for x in m.groups())
            self.melde("temp", werte)
            self.waechter(werte)
        elif z != "ok":
            self.melde("log", "< " + z)
        if z.startswith("Error"):
            if self.druckt:
                self.druckt = False
                self.melde("log", "Druck wegen Fehler abgebrochen.")
                self.melde("druck_ende", None)
            return True
        return z.startswith("ok")


class App:
    def __init__(self, root):
        self.root = root
        root.title("Kobra Max Steuerung")
        self.q = queue.Queue()
        self.drucker = Drucker(lambda typ, wert: self.q.put((typ, wert)))
        self.verbunden = False

        # --- Verbindung ---
        f = ttk.LabelFrame(root, text="Verbindung")
        f.grid(row=0, column=0, columnspan=2, sticky="ew", padx=8, pady=4)
        self.port = ttk.Combobox(f, width=40, state="readonly")
        self.port.grid(row=0, column=0, padx=4, pady=4)
        ttk.Button(f, text="Aktualisieren", command=self.suche_ports).grid(row=0, column=1, padx=4)
        self.btn_verb = ttk.Button(f, text="Verbinden", command=self.verbinden)
        self.btn_verb.grid(row=0, column=2, padx=4)
        self.lbl_status = ttk.Label(f, text="getrennt", foreground="red")
        self.lbl_status.grid(row=0, column=3, padx=8)

        # --- Temperatur ---
        f = ttk.LabelFrame(root, text="Temperatur")
        f.grid(row=1, column=0, sticky="nsew", padx=8, pady=4)
        self.lbl_duese = ttk.Label(f, text="Düse: -- / -- °C", width=24, font=("Segoe UI", 11, "bold"))
        self.lbl_duese.grid(row=0, column=0, padx=4, pady=4)
        self.e_duese = ttk.Entry(f, width=5)
        self.e_duese.insert(0, "200")
        self.e_duese.grid(row=0, column=1)
        ttk.Button(f, text="Heizen", command=lambda: self.heize("M104", self.e_duese)).grid(row=0, column=2, padx=2)
        ttk.Button(f, text="Aus", command=lambda: self.aus("M104")).grid(row=0, column=3, padx=2)
        self.lbl_ziel_duese = ttk.Label(f, text="Druck: Wert aus Datei", width=20)
        self.lbl_ziel_duese.grid(row=0, column=4, padx=4)
        self.lbl_bett = ttk.Label(f, text="Bett: -- / -- °C", width=24, font=("Segoe UI", 11, "bold"))
        self.lbl_bett.grid(row=1, column=0, padx=4, pady=4)
        self.e_bett = ttk.Entry(f, width=5)
        self.e_bett.insert(0, "60")
        self.e_bett.grid(row=1, column=1)
        ttk.Button(f, text="Heizen", command=lambda: self.heize("M140", self.e_bett)).grid(row=1, column=2, padx=2)
        ttk.Button(f, text="Aus", command=lambda: self.aus("M140")).grid(row=1, column=3, padx=2)
        self.lbl_ziel_bett = ttk.Label(f, text="Druck: Wert aus Datei", width=20)
        self.lbl_ziel_bett.grid(row=1, column=4, padx=4)

        # --- Bewegung ---
        f = ttk.LabelFrame(root, text="Bewegung")
        f.grid(row=1, column=1, sticky="nsew", padx=8, pady=4)
        ttk.Button(f, text="Y+", width=5, command=lambda: self.fahre("Y", 1)).grid(row=0, column=1)
        ttk.Button(f, text="X-", width=5, command=lambda: self.fahre("X", -1)).grid(row=1, column=0)
        ttk.Button(f, text="Home", width=5, command=lambda: self.bewegung("G28")).grid(row=1, column=1)
        ttk.Button(f, text="X+", width=5, command=lambda: self.fahre("X", 1)).grid(row=1, column=2)
        ttk.Button(f, text="Y-", width=5, command=lambda: self.fahre("Y", -1)).grid(row=2, column=1)
        ttk.Button(f, text="Z+", width=5, command=lambda: self.fahre("Z", 1)).grid(row=0, column=3, padx=(12, 0))
        ttk.Button(f, text="Z-", width=5, command=lambda: self.fahre("Z", -1)).grid(row=2, column=3, padx=(12, 0))
        ttk.Label(f, text="Schritt (mm):").grid(row=3, column=0, columnspan=2, pady=4)
        self.schritt = ttk.Combobox(f, values=["1", "10", "50"], width=4, state="readonly")
        self.schritt.set("10")
        self.schritt.grid(row=3, column=2)

        # --- Druck ---
        f = ttk.LabelFrame(root, text="Druck")
        f.grid(row=2, column=0, columnspan=2, sticky="ew", padx=8, pady=4)
        ttk.Button(f, text="Datei öffnen", command=self.oeffne).grid(row=0, column=0, padx=4, pady=4)
        self.lbl_datei = ttk.Label(f, text="keine Datei geladen", width=40)
        self.lbl_datei.grid(row=0, column=1, columnspan=3, sticky="w")
        ttk.Button(f, text="Start", command=self.start).grid(row=1, column=0, padx=4, pady=4)
        self.btn_pause = ttk.Button(f, text="Pause", command=self.pause)
        self.btn_pause.grid(row=1, column=1, padx=4)
        ttk.Button(f, text="Stopp", command=self.stopp).grid(row=1, column=2, padx=4)
        self.lbl_fort = ttk.Label(f, text="")
        self.lbl_fort.grid(row=1, column=3, padx=8, sticky="w")
        self.balken = ttk.Progressbar(f, length=560)
        self.balken.grid(row=2, column=0, columnspan=4, padx=4, pady=4)

        # --- Konsole ---
        f = ttk.LabelFrame(root, text="Konsole")
        f.grid(row=3, column=0, columnspan=2, sticky="nsew", padx=8, pady=4)
        self.log_feld = scrolledtext.ScrolledText(f, height=10, width=70, state="disabled")
        self.log_feld.grid(row=0, column=0, columnspan=2, padx=4, pady=4)
        self.e_befehl = ttk.Entry(f, width=58)
        self.e_befehl.grid(row=1, column=0, padx=4, pady=4)
        self.e_befehl.bind("<Return>", lambda e: self.sende_eingabe())
        ttk.Button(f, text="Senden", command=self.sende_eingabe).grid(row=1, column=1)

        root.protocol("WM_DELETE_WINDOW", self.schliessen)
        self.suche_ports()
        self.hole_meldungen()
        self.frage_temperatur()

    # ---------- Verbindung ----------
    def suche_ports(self):
        self.ports = list(list_ports.comports())
        self.port["values"] = [f"{p.device} - {p.description}" for p in self.ports]
        if self.ports:
            self.port.current(0)

    def verbinden(self):
        if self.drucker.aktiv:
            self.drucker.trenne()
        elif self.ports and self.port.current() >= 0:
            self.btn_verb["state"] = "disabled"
            self.drucker.verbinde(self.ports[self.port.current()].device)
        else:
            self.log("Kein Port gefunden. Kabel eingesteckt? Drucker an?")

    # ---------- Befehle ----------
    def sende(self, befehl):
        if self.verbunden:
            self.drucker.befehle.put(befehl)
        else:
            self.log("Nicht verbunden.")

    def sende_eingabe(self):
        b = self.e_befehl.get().strip().upper()
        if b:
            self.sende(b)
            self.e_befehl.delete(0, "end")

    def heize(self, code, feld):
        try:
            wert = int(feld.get().strip())
        except ValueError:
            self.log("Bitte eine ganze Zahl als Temperatur eingeben, z. B. 200.")
            return
        grenze = MAX_DUESE if code == "M104" else MAX_BETT
        if not 0 < wert <= grenze:
            self.log(f"Bitte einen Wert zwischen 1 und {grenze} °C eingeben.")
            return
        if not self.verbunden:
            self.log("Nicht verbunden.")
            return
        if code == "M104":
            self.drucker.ziel_duese = wert
            self.lbl_ziel_duese["text"] = f"Druck: {wert} °C"
        else:
            self.drucker.ziel_bett = wert
            self.lbl_ziel_bett["text"] = f"Druck: {wert} °C"
        self.sende(f"{code} S{wert}")

    def aus(self, code):
        """Heizung aus. Danach gelten beim Druck wieder die Werte aus der Datei."""
        if code == "M104":
            self.drucker.ziel_duese = None
            self.lbl_ziel_duese["text"] = "Druck: Wert aus Datei"
        else:
            self.drucker.ziel_bett = None
            self.lbl_ziel_bett["text"] = "Druck: Wert aus Datei"
        self.sende(f"{code} S0")

    def bewegung(self, *befehle):
        if self.drucker.druckt:
            self.log("Während des Drucks ist Bewegen gesperrt.")
            return
        for b in befehle:
            self.sende(b)

    def fahre(self, achse, richtung):
        weg = richtung * int(self.schritt.get())
        tempo = 600 if achse == "Z" else 3000
        self.bewegung("G91", f"G1 {achse}{weg} F{tempo}", "G90")

    # ---------- Druck ----------
    def oeffne(self):
        pfad = filedialog.askopenfilename(filetypes=[("G-Code", "*.gcode *.gco *.g"), ("Alle Dateien", "*.*")])
        if not pfad:
            return
        if self.drucker.druckt:
            self.log("Es läuft gerade ein Druck.")
            return
        with open(pfad, encoding="utf-8", errors="ignore") as f:
            zeilen = [z.split(";")[0].strip() for z in f]
        # Zeilennummern (N123) und Pruefsummen (*45) entfernen, der Drucker braucht sie nicht
        zeilen = [re.sub(r"^N\d+\s*|\*\d+$", "", z).strip() for z in zeilen]
        self.drucker.zeilen = [z for z in zeilen if z]
        self.lbl_datei["text"] = pfad.split("/")[-1]
        self.lbl_fort["text"] = f"{len(self.drucker.zeilen)} Zeilen"
        self.balken["value"] = 0

    def start(self):
        if not self.verbunden:
            self.log("Nicht verbunden.")
        elif not self.drucker.zeilen:
            self.log("Erst eine Datei öffnen.")
        elif self.drucker.druckt:
            self.log("Es läuft schon ein Druck.")
        else:
            self.log("Druck gestartet.")
            self.drucker.starte_druck()

    def pause(self):
        if self.drucker.druckt:
            self.drucker.pause = not self.drucker.pause
            self.btn_pause["text"] = "Weiter" if self.drucker.pause else "Pause"
            self.log("Pausiert." if self.drucker.pause else "Weiter geht's.")

    def stopp(self):
        if self.drucker.druckt:
            self.drucker.stopp()
            self.btn_pause["text"] = "Pause"
            self.log("Druck gestoppt, Heizung aus.")

    # ---------- Anzeige ----------
    def log(self, text):
        self.log_feld["state"] = "normal"
        self.log_feld.insert("end", text + "\n")
        if int(self.log_feld.index("end-1c").split(".")[0]) > 500:
            self.log_feld.delete("1.0", "100.0")
        self.log_feld.see("end")
        self.log_feld["state"] = "disabled"

    def hole_meldungen(self):
        try:
            while True:
                typ, wert = self.q.get_nowait()
                if typ == "log":
                    self.log(wert)
                elif typ == "temp":
                    self.lbl_duese["text"] = f"Düse: {wert[0]:.0f} / {wert[1]:.0f} °C"
                    self.lbl_bett["text"] = f"Bett: {wert[2]:.0f} / {wert[3]:.0f} °C"
                elif typ == "status":
                    self.verbunden = wert
                    self.btn_verb["state"] = "normal"
                    self.btn_verb["text"] = "Trennen" if wert else "Verbinden"
                    self.lbl_status.config(text="verbunden" if wert else "getrennt",
                                           foreground="green" if wert else "red")
                    self.log("Verbunden." if wert else "Getrennt.")
                elif typ == "fortschritt":
                    self.balken["value"] = 100 * wert[0] / wert[1]
                    self.lbl_fort["text"] = f"{wert[0]} / {wert[1]} Zeilen ({100 * wert[0] // wert[1]} %)"
                elif typ == "druck_ende":
                    self.btn_pause["text"] = "Pause"
        except queue.Empty:
            pass
        self.root.after(50, self.hole_meldungen)

    def frage_temperatur(self):
        if self.verbunden and self.drucker.befehle.empty():
            self.drucker.befehle.put("M105")
        self.root.after(3000, self.frage_temperatur)

    def schliessen(self):
        self.drucker.trenne()   # gibt den Port sauber frei
        self.root.after(400, self.root.destroy)


if __name__ == "__main__":
    root = tk.Tk()
    App(root)
    root.mainloop()
```

## Anhang B: Testdateien

`testdatei_generator.py` erzeugt `temperaturturm_220-190.gcode` und `testhaus_205.gcode`
(Aufruf: `python testdatei_generator.py <ausgabeordner>`). Gut für einen ersten echten Test der neuen App.
**Vor dem Temperaturturm in der App bei Düse und Bett „Aus" drücken**, sonst überschreibt der Override die Stufen.

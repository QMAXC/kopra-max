"""Erzeugt Test-G-Code fuer Anycubic Kobra Max (Marlin, 1,75 mm, 0,4-mm-Duese, Bowden)."""
import math

FIL_D = 1.75
LH = 0.2              # Schichthoehe
LW = 0.45             # Linienbreite
RETRACT, RETRACT_F = 5.0, 2400
TRAVEL_F, WALL_F, FIRST_F = 7200, 2400, 1200
CX, CY = 200, 200     # Bettmitte
BED = 50

def e_pro_mm(w, h):
    return (w * h) / (math.pi * (FIL_D / 2) ** 2)

class G:
    def __init__(self, titel):
        self.z = 0.0
        self.x = self.y = None
        self.retracted = False
        self.out = [f"; {titel}", "; erzeugt fuer Anycubic Kobra Max, PLA, 1.75 mm, Duese 0.4 mm"]
        self.layer_nr = 0

    def c(self, s):
        self.out.append(s)

    def start(self, temp):
        self.c(f"M140 S{BED}")
        self.c("M104 S150")           # vorwaermen ohne Tropfen
        self.c("G90")
        self.c("M83")                 # relative Extrusion
        self.c("G28")
        self.c(f"M190 S{BED}")
        self.c(f"M109 S{temp}")
        self.c("G92 E0")
        # Reinigungslinie vorne links
        self.c("G1 Z5 F600")
        self.c(f"G0 X30 Y20 F{TRAVEL_F}")
        self.c("G1 Z0.3 F600")
        self.x, self.y = 30, 20
        self.line_to(150, 20, w=0.6, h=0.3, f=1500)
        self.travel(150, 20.8)
        self.line_to(30, 20.8, w=0.6, h=0.3, f=1500)
        self.retract()
        self.c("G1 Z2 F600")

    def ende(self):
        self.retract()
        self.c("M104 S0")
        self.c("M140 S0")
        self.c("M106 S0")
        self.c("G91")
        self.c("G1 Z10 F600")
        self.c("G90")
        self.c(f"G1 X10 Y380 F{TRAVEL_F}")
        self.c("M84")
        self.c("; Ende")

    def retract(self):
        if not self.retracted:
            self.c(f"G1 E-{RETRACT} F{RETRACT_F}")
            self.retracted = True

    def unretract(self):
        if self.retracted:
            self.c(f"G1 E{RETRACT} F{RETRACT_F}")
            self.retracted = False

    def layer(self, z):
        self.layer_nr += 1
        self.z = round(z, 3)
        self.c(f";LAYER:{self.layer_nr} Z={self.z}")
        self.c(f"G1 Z{self.z:.3f} F600")
        if self.layer_nr == 2:
            self.c("M106 S255")       # Luefter ab Schicht 2

    def travel(self, x, y):
        if self.x is not None and math.hypot(x - self.x, y - self.y) < 1e-6:
            return
        if self.x is not None and math.hypot(x - self.x, y - self.y) > 2:
            self.retract()
        self.c(f"G0 X{x:.3f} Y{y:.3f} F{TRAVEL_F}")
        self.x, self.y = x, y

    def line_to(self, x, y, w=LW, h=LH, f=None):
        self.unretract()
        d = math.hypot(x - self.x, y - self.y)
        if d < 1e-6:
            return
        f = f or (FIRST_F if self.layer_nr <= 1 else WALL_F)
        self.c(f"G1 X{x:.3f} Y{y:.3f} E{d * e_pro_mm(w, h):.5f} F{f}")
        self.x, self.y = x, y

    def polyline(self, pts, closed=False):
        self.travel(*pts[0])
        for p in pts[1:]:
            self.line_to(*p)
        if closed:
            self.line_to(*pts[0])

    def rect(self, x0, y0, x1, y1):
        self.polyline([(x0, y0), (x1, y0), (x1, y1), (x0, y1)], closed=True)

    def walls(self, x0, y0, x1, y1, n=2):
        """n Umfaenge von aussen nach innen... gedruckt innen zuerst (sauberere Aussenseite)."""
        for i in reversed(range(n)):
            o = LW / 2 + i * LW
            if x1 - x0 > 2 * o and y1 - y0 > 2 * o:
                self.rect(x0 + o, y0 + o, x1 - o, y1 - o)

    def fill(self, x0, y0, x1, y1, richtung):
        """Volle Flaeche mit Zickzack-Linien."""
        if richtung == 0:   # Linien entlang X
            y, i = y0 + LW / 2, 0
            while y <= y1 - LW / 2 + 1e-9:
                a, b = (x0 + LW / 2, x1 - LW / 2) if i % 2 == 0 else (x1 - LW / 2, x0 + LW / 2)
                if i == 0:
                    self.travel(a, y)
                else:
                    self.line_to(a, y)
                self.line_to(b, y)
                y += LW; i += 1
        else:               # Linien entlang Y
            x, i = x0 + LW / 2, 0
            while x <= x1 - LW / 2 + 1e-9:
                a, b = (y0 + LW / 2, y1 - LW / 2) if i % 2 == 0 else (y1 - LW / 2, y0 + LW / 2)
                if i == 0:
                    self.travel(x, a)
                else:
                    self.line_to(x, a)
                self.line_to(x, b)
                x += LW; i += 1

    def text(self):
        return "\n".join(self.out) + "\n"


def temperaturturm(temps=(220, 215, 210, 205, 200, 195, 190), seg_h=5.0):
    g = G("Temperaturturm " + "-".join(map(str, temps)) + " C (unten heiss, oben kalt)")
    g.c("; Jede Stufe 5 mm hoch. Rille = Grenze zwischen zwei Stufen.")
    g.c("; Daneben steht ein duenner Stab: Faeden dazwischen = Stringing.")
    g.start(temps[0])
    # Turm 20 x 15 mm, Stab 4 x 4 mm, 12 mm daneben
    tx0, ty0 = CX - 18, CY - 7.5
    tx1, ty1 = tx0 + 20, ty0 + 15
    px0, py0 = tx1 + 12, CY - 2
    px1, py1 = px0 + 4, py0 + 4
    bx0, by0, bx1, by1 = tx0 - 3, ty0 - 3, px1 + 3, ty1 + 3   # Bodenplatte
    # Skirt
    z = LH
    g.layer(z)
    g.rect(bx0 - 4, by0 - 4, bx1 + 4, by1 + 4)
    # Bodenplatte 3 Schichten
    for k in range(3):
        if k:
            z += LH; g.layer(z)
        g.walls(bx0, by0, bx1, by1, n=1)
        g.fill(bx0 + LW, by0 + LW, bx1 - LW, by1 - LW, k % 2)
    basis = z
    n_seg_layers = int(round(seg_h / LH))
    for s, t in enumerate(temps):
        g.c(f";STUFE {s + 1}: {t} C")
        if s:
            g.c(f"M104 S{t}")
        for k in range(n_seg_layers):
            z += LH; g.layer(z)
            rille = s > 0 and k < 2       # zwei Schichten nur innerer Umfang
            if rille:
                o = LW / 2 + LW
                g.rect(tx0 + o, ty0 + o, tx1 - o, ty1 - o)
            else:
                g.walls(tx0, ty0, tx1, ty1, n=2)
            g.walls(px0, py0, px1, py1, n=2)
    g.ende()
    return g.text(), (basis, z)


def haus(temp=205):
    g = G(f"Kleines Testhaus bei {temp} C: Tuer = Bruecke 8 mm, Dach = 45-Grad-Ueberhang")
    g.start(temp)
    L, B = 30.0, 22.0                     # Laenge (X), Breite (Y)
    x0, y0 = CX - L / 2, CY - B / 2
    x1, y1 = x0 + L, y0 + B
    wand_h = 15.0
    tuer_b, tuer_h = 8.0, 10.0
    tx0, tx1 = CX - tuer_b / 2, CX + tuer_b / 2
    z = LH
    g.layer(z)
    g.rect(x0 - 5, y0 - 5, x1 + 5, y1 + 5)   # Skirt
    # Boden: 2 Schichten voll
    for k in range(2):
        if k:
            z += LH; g.layer(z)
        g.walls(x0, y0, x1, y1, n=2)
        g.fill(x0 + 2 * LW, y0 + 2 * LW, x1 - 2 * LW, y1 - 2 * LW, k % 2)
    # Waende mit Tuer (vorne, y0)
    while z + LH <= wand_h + 1e-9:
        z += LH; g.layer(z)
        if z <= tuer_h + 1e-9:
            # offener Umfang: von rechter Tuerkante einmal herum zur linken Tuerkante
            for i in reversed(range(2)):
                o = LW / 2 + i * LW
                g.polyline([(tx1, y0 + o), (x1 - o, y0 + o), (x1 - o, y1 - o),
                            (x0 + o, y1 - o), (x0 + o, y0 + o), (tx0, y0 + o)])
        else:
            g.walls(x0, y0, x1, y1, n=2)      # geschlossen -> Bruecke ueber der Tuer
    # Satteldach: Laengsseiten wandern pro Schicht 0,2 mm nach innen (45 Grad)
    d = 0.0
    while True:
        z += LH; d += LH
        ya, yb = y0 + d, y1 - d
        if yb - ya < 4 * LW:
            break
        g.layer(z)
        g.walls(x0, ya, x1, yb, n=2)
    # First: schmale Restflaeche schliessen
    g.layer(z)
    ya, yb = y0 + d, y1 - d
    if yb - ya > LW:
        g.fill(x0 + LW / 2, ya, x1 - LW / 2, yb, 0)
    g.ende()
    return g.text(), z


if __name__ == "__main__":
    import sys
    out = sys.argv[1]
    t, (b, h) = temperaturturm()
    open(f"{out}/temperaturturm_220-190.gcode", "w", newline="\r\n").write(t)
    print("Turm Hoehe", h)
    t, h = haus()
    open(f"{out}/testhaus_205.gcode", "w", newline="\r\n").write(t)
    print("Haus Hoehe", h)

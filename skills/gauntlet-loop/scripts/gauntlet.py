import argparse
import json
import random
import re
import shutil
import subprocess
import sys
from datetime import date
from pathlib import Path

KINDS = {
    "game": ["play", "feel", "ux", "audio", "visual"],
    "mechanic": ["feel", "readability", "response", "fairness"],
    "image": ["silhouette", "fidelity", "density", "craft", "cohesion"],
    "ui": ["hierarchy", "fidelity", "states", "motion", "cohesion"],
    "gamefeel": ["response", "weight", "layering", "restraint"],
    "text": ["clarity", "density", "structure", "voice"],
    "code": ["correctness", "speed", "simplicity"],
}
LENS_HINTS = {
    "play": "что за 30 секунд происходит у эталона и не происходит у нас; решения игрока",
    "feel": "кадров от нажатия до реакции, разгон, вес; отклик «аккордом» или «арпеджио»",
    "ux": "следующее действие — самое заметное; нажатий до геймплея; тупики",
    "audio": "немые у нас действия, слышные у эталона; разных SFX за 30 с",
    "visual": "силуэт на 128 px, тона без цвета, свет и плотность слоёв",
    "readability": "читается ли состояние и угроза за долю секунды",
    "response": "сколько слоёв отклика и в каком кадре приходит первый",
    "fairness": "понятна ли причина провала с экрана",
    "silhouette": "на 128 px и в прищуре — тот же ли это предмет",
    "fidelity": "поэлементно против эталона: чего нет, что не той формы",
    "density": "столько же информации или пустее",
    "craft": "материал и свет, ритм, точность",
    "cohesion": "одна авторская вещь или собранные куски",
    "hierarchy": "главное заметнее второстепенного",
    "states": "загрузка, пусто, данные, ошибка, нажатие",
    "motion": "сглаживание и длительности переходов",
    "weight": "масса и инерция читаются",
    "layering": "мгновенный, короткий и долгий слои отклика",
    "restraint": "всё есть и всё тише, чем ожидаешь",
    "clarity": "понятно с первого прочтения",
    "structure": "порядок и связность разделов",
    "voice": "единый тон",
    "correctness": "проходит ли эталонные сценарии",
    "speed": "число против эталонного",
    "simplicity": "меньше движущихся частей при том же поведении",
}
PRODUCT_KINDS = {"game"}
IMAGE_EXT = {".png", ".jpg", ".jpeg", ".webp", ".bmp", ".tga"}
PLACEHOLDER = re.compile(r"<[^<>\n]{2,}>")


def fail(msg):
    print(msg)
    sys.exit(1)


def read(p):
    return Path(p).read_text(encoding="utf-8") if Path(p).exists() else ""


def write(p, text):
    Path(p).parent.mkdir(parents=True, exist_ok=True)
    Path(p).write_text(text, encoding="utf-8", newline="\n")


def bullets(text):
    out = []
    for line in text.splitlines():
        m = re.match(r"\s*-\s+\*\*(.+?)\*\*\s+[—-]\s+(.*)", line)
        if m:
            out.append((m.group(1).strip(), m.group(2).strip()))
    return out


def bar_field(run, key):
    m = re.search(rf"^{key}:\s*(.*)$", read(run / "BAR.md"), re.M)
    return m.group(1).strip() if m else ""


def rounds(run):
    rows = []
    for d in sorted((run / "rounds").glob("[0-9][0-9]*")):
        o = d / "outcome.json"
        if o.exists():
            rows.append(json.loads(o.read_text(encoding="utf-8")))
    return rows


def cmd_init(a):
    run = Path(a.run)
    if (run / "STATE.md").exists():
        fail(f"{run} уже существует — продолжай: next {run}")
    lenses = KINDS[a.kind]
    (run / "ref").mkdir(parents=True, exist_ok=True)
    (run / "rounds").mkdir(exist_ok=True)
    ref_line = "<положи эталон в ref/ и впиши путь>"
    if a.ref:
        src = Path(a.ref)
        if not src.exists():
            fail(f"нет файла эталона: {src}")
        shutil.copy2(src, run / "ref" / src.name)
        ref_line = f"ref/{src.name}"
    write(run / "BAR.md",
          f"DELIVERABLE: {a.deliverable}\nGOAL: {a.goal}\nKIND: {a.kind}\n"
          f"BAR: <эталон одной фразой и почему именно он>\nREFERENCE: {ref_line}\n")
    write(run / "RUBRIC.md",
          "# Рубрика — только ужесточать, каждую правку — строкой в STATE.md\n\n"
          + "".join(f"- **{l}** — {LENS_HINTS[l]}\n" for l in lenses))
    piece_line = ("- **core-loop** — facet: play — capture: <команда захвата>\n"
                  if a.kind in PRODUCT_KINDS else
                  "- **main** — capture: <команда захвата>\n")
    write(run / "PIECES.md", "# Куски — по одной строке, у каждого своя команда захвата\n\n" + piece_line)
    write(run / "open_gaps.md", "# Очередь разрывов\n\n")
    if a.kind in PRODUCT_KINDS:
        write(run / "DELIVERABLE.md",
              "# Что человек держит в руках — ворота до первого раунда\n\n"
              "- [ ] запускается одной командой с чистого сейва\n"
              "- [ ] полная сессия идёт от старта до итога\n"
              "- [ ] можно проиграть или ошибиться, и игра говорит, что случилось\n"
              "- [ ] ни один экран не тупиковый\n")
    write(run / "STATE.md",
          f"# Gauntlet — {run.name}\n\nСтарт: {date.today()} · бюджет раундов: {a.budget}\n\n"
          "## Журнал\n\n| # | кусок | линза | победитель | разрыв |\n| --- | --- | --- | --- | --- |\n")
    print(f"создан {run}. Заполни BAR (строка BAR), PIECES (команды захвата), проверь RUBRIC.")


def current(run):
    lenses = [n for n, _ in bullets(read(run / "RUBRIC.md"))]
    pieces = bullets(read(run / "PIECES.md"))
    done = rounds(run)
    return lenses, pieces, done


def pick(lenses, pieces, done, rotate):
    if rotate == "piece":
        piece = pieces[len(done) % len(pieces)]
        used = [r["lens"] for r in done if r["piece"] == piece[0]]
    else:
        idx, start = 0, 0
        while True:
            name = pieces[idx % len(pieces)][0]
            block = [r for r in done[start:] if r["piece"] == name]
            if len({r["lens"] for r in block}) >= len(lenses) and block:
                start += len(block)
                idx += 1
                continue
            break
        piece = pieces[idx % len(pieces)]
        used = [r["lens"] for r in done[start:] if r["piece"] == piece[0]]
    order = {l: -1 for l in lenses}
    for i, l in enumerate(used):
        if l in order:
            order[l] = i
    lens = min(lenses, key=lambda l: order[l])
    return piece, lens


def norm(s):
    return re.sub(r"[^\w]+", " ", s.lower()).strip()


def cmd_next(a):
    run = Path(a.run)
    if not (run / "STATE.md").exists():
        fail(f"нет прогона {run} — сначала init")
    lenses, pieces, done = current(run)
    problems = []
    for f in ("BAR.md", "PIECES.md", "RUBRIC.md"):
        if PLACEHOLDER.search(read(run / f)):
            problems.append(f"{f}: остался шаблон <...>")
    if len(lenses) < 3:
        problems.append("RUBRIC.md: меньше трёх линз в формате «- **имя** — вопрос»")
    if not pieces:
        problems.append("PIECES.md: нет кусков в формате «- **имя** — capture: команда»")
    kind = bar_field(run, "KIND")
    deliverable = bar_field(run, "DELIVERABLE")
    print(f"deliverable: {deliverable}\nkind: {kind}")
    if problems:
        print("SETUP INCOMPLETE:\n  " + "\n  ".join(problems))
        sys.exit(2)
    if kind in PRODUCT_KINDS:
        facets = [re.search(r"facet:\s*(\w+)", d) for _, d in pieces]
        tags = [m.group(1) for m in facets if m]
        if "play" not in tags:
            print("SETUP INCOMPLETE: в продуктовом прогоне нужен хотя бы один кусок facet: play")
            sys.exit(2)
        surface = sum(t in ("visual", "ux") for t in tags)
        if surface * 2 > len(pieces):
            print("SETUP INCOMPLETE: куски поверхности (visual, ux) — больше половины; кадр не продукт")
            sys.exit(2)
        open_gates = [l for l in read(run / "DELIVERABLE.md").splitlines() if l.strip().startswith("- [ ]") and "BLOCKED" not in l]
        if open_gates:
            print("GATE ZERO: срез не собран — раундов полировки не будет, пока не закрыто:")
            print("\n".join("  " + g.strip() for g in open_gates))
            sys.exit(3)
    m = re.search(r"бюджет раундов:\s*(\d+)", read(run / "STATE.md"))
    budget = int(m.group(1)) if m else 40
    if len(done) >= budget:
        print(f"STOP: бюджет {budget} раундов исчерпан")
        sys.exit(4)
    tail = done[-len(lenses):]
    if len(tail) == len(lenses) and all(r["winner"] == "ours" for r in tail) and {r["lens"] for r in tail} == set(lenses):
        print("STOP: наш вариант выиграл вслепую по всем линзам полного круга — подними эталон или заканчивай")
        sys.exit(4)
    if kind in PRODUCT_KINDS and len(done) >= 4:
        last = [r["piece"] for r in done[-4:]]
        facet_of = {n: (re.search(r"facet:\s*(\w+)", d) or [None, ""])[1] for n, d in pieces}
        if all(facet_of.get(p) in ("visual", "ux") for p in last):
            print("SURFACE DRIFT: четыре раунда подряд на поверхности — следующим бери кусок play/meta/content")
    if len(done) >= 3 and len({norm(r["gap"]) for r in done[-3:]}) == 1:
        print("STALL: один и тот же разрыв три раунда — переформулируй «у эталона X, у нас Y» или BLOCKED")
    piece, lens = pick(lenses, pieces, done, a.rotate)
    n = len(done) + 1
    cap = re.search(r"capture:\s*(.*)$", piece[1])
    rd = (run / "rounds" / f"{n:02d}").as_posix()
    run_s = run.as_posix()
    hint = dict(bullets(read(run / "RUBRIC.md"))).get(lens, "")
    print(f"round: {n:02d} / {budget}\npiece: {piece[0]}\nlens: {lens} — {hint}")
    print(f"capture: {cap.group(1).strip() if cap else '(нет команды в PIECES.md)'}")
    print(f"artifact → {rd}/artifact.<ext>")
    ref = bar_field(run, "REFERENCE")
    print(f"blind pair: python ~/.claude/skills/gauntlet-loop/scripts/gauntlet.py ab {rd} --ours {rd}/artifact.png --bar {run_s}/{ref}")
    print(f"verdict → {rd}/verdict.md, затем: python ~/.claude/skills/gauntlet-loop/scripts/gauntlet.py reveal {rd}")


def ffmpeg_normalise(src, dst, width):
    if not shutil.which("ffmpeg"):
        return False
    r = subprocess.run(["ffmpeg", "-y", "-loglevel", "error", "-i", str(src),
                        "-vf", f"scale={width}:-2:flags=lanczos", "-frames:v", "1", str(dst)],
                       capture_output=True)
    return r.returncode == 0


def text_normalise(src, dst):
    t = Path(src).read_text(encoding="utf-8", errors="replace")
    t = re.sub(r"[ \t]+\n", "\n", t)
    t = re.sub(r"\n{3,}", "\n\n", t)
    t = re.sub(r"^#{1,6}\s*", "## ", t, flags=re.M)
    write(dst, t.strip() + "\n")


def cmd_ab(a):
    rd = Path(a.round)
    ours, bar = Path(a.ours), Path(a.bar)
    for p in (ours, bar):
        if not p.exists():
            fail(f"нет файла: {p}")
    ab = rd / "ab"
    if (ab / "key.json").exists():
        fail(f"пара уже создана в {ab} — не пересоздавать, иначе ключ раскроется")
    ab.mkdir(parents=True, exist_ok=True)
    sides = [("ours", ours), ("bar", bar)]
    random.shuffle(sides)
    is_image = ours.suffix.lower() in IMAGE_EXT and bar.suffix.lower() in IMAGE_EXT
    key = {}
    for letter, (who, src) in zip("AB", sides):
        if is_image:
            dst = ab / f"{letter}.png"
            if not ffmpeg_normalise(src, dst, a.shrink):
                fail("для картинок нужен ffmpeg в PATH (winget install Gyan.FFmpeg)")
        else:
            dst = ab / f"{letter}{ours.suffix or '.txt'}"
            text_normalise(src, dst)
        key[letter] = who
    write(ab / "key.json", json.dumps(key))
    print(f"пара готова: {ab}/A и {ab}/B. key.json не открывать до reveal.")


def cmd_reveal(a):
    rd = Path(a.round)
    run = rd.parent.parent
    v = read(rd / "verdict.md")
    win = re.search(r"^WINNER:\s*([AB])\s*$", v, re.M)
    gaps = re.findall(r"^GAP:\s*(.+)$", v, re.M)
    head = re.search(r"^#\s*Round\s+\d+\s+[—-]\s+(.+?)\s+[—-]\s+lens:\s*(\S+)", v, re.M)
    if not win or len(gaps) != 1 or not head:
        fail("verdict.md неполный: нужен заголовок «# Round NN — <кусок> — lens: <линза>», "
             "строка WINNER: A|B и ровно одна строка GAP:")
    if (rd / "outcome.json").exists():
        fail("раунд уже раскрыт")
    key = json.loads(read(rd / "ab" / "key.json") or "{}")
    if not key:
        fail("нет ab/key.json — сначала ab")
    winner = key[win.group(1)]
    out = {"round": rd.name, "piece": head.group(1).strip(), "lens": head.group(2).strip(),
           "winner": winner, "gap": gaps[0].strip(), "date": str(date.today())}
    write(rd / "outcome.json", json.dumps(out, ensure_ascii=False, indent=1))
    with open(rd / "verdict.md", "a", encoding="utf-8") as f:
        f.write(f"\nREVEAL: {win.group(1)} = {winner}\n")
    with open(run / "STATE.md", "a", encoding="utf-8") as f:
        f.write(f"| {rd.name} | {out['piece']} | {out['lens']} | {winner} | {out['gap']} |\n")
    print(f"{win.group(1)} = {winner}. " + ("OURS WINS — перечитай строку рубрики: если причина не выдерживает, считай BAR WINS."
                                           if winner == "ours" else "BAR WINS — закрывай этот разрыв, один."))


def cmd_status(a):
    run = Path(a.run)
    lenses, pieces, done = current(run)
    ours = sum(r["winner"] == "ours" for r in done)
    print(f"{run.name}: раундов {len(done)}, наших побед {ours}, кусков {len(pieces)}, линз {len(lenses)}")
    for r in done[-5:]:
        print(f"  {r['round']} {r['piece']}/{r['lens']}: {r['winner']} — {r['gap']}")
    gaps = [l for l in read(run / "open_gaps.md").splitlines() if l.startswith("- ")]
    print(f"в очереди разрывов: {len(gaps)}")


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    p = argparse.ArgumentParser(prog="gauntlet")
    s = p.add_subparsers(dest="cmd", required=True)
    i = s.add_parser("init")
    i.add_argument("run")
    i.add_argument("--deliverable", required=True)
    i.add_argument("--goal", required=True)
    i.add_argument("--kind", required=True, choices=sorted(KINDS))
    i.add_argument("--ref")
    i.add_argument("--budget", type=int, default=40)
    n = s.add_parser("next")
    n.add_argument("run")
    n.add_argument("--rotate", choices=["lens", "piece"], default="lens")
    b = s.add_parser("ab")
    b.add_argument("round")
    b.add_argument("--ours", required=True)
    b.add_argument("--bar", required=True)
    b.add_argument("--shrink", type=int, default=1000)
    r = s.add_parser("reveal")
    r.add_argument("round")
    st = s.add_parser("status")
    st.add_argument("run")
    a = p.parse_args()
    {"init": cmd_init, "next": cmd_next, "ab": cmd_ab, "reveal": cmd_reveal, "status": cmd_status}[a.cmd](a)


if __name__ == "__main__":
    main()

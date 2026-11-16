import argparse
import csv
import json
import sys
from pathlib import Path

DEFAULT_MODEL = {
    "name": "untitled",
    "days": 30,
    "upgrade": {
        "base_cost": 10.0,
        "cost_growth": 1.12,
        "base_income_per_minute": 6.0,
        "income_per_level": 3.0,
    },
    "offline": {"cap_hours": 4.0, "rate_multiplier": 0.4},
    "ad": {"reward_in_minutes_of_income": 1.0},
    "daily_bonus_in_minutes_of_income": 2.0,
    "gates": [],
    "personas": [
        {"name": "casual_f2p", "sessions_per_day": 1.5, "minutes_per_session": 4.0,
         "ads_per_day": 1.0, "usd_per_day": 0.0},
        {"name": "engaged_f2p", "sessions_per_day": 4.0, "minutes_per_session": 10.0,
         "ads_per_day": 8.0, "usd_per_day": 0.0},
        {"name": "dolphin", "sessions_per_day": 4.0, "minutes_per_session": 12.0,
         "ads_per_day": 3.0, "usd_per_day": 0.5},
        {"name": "whale", "sessions_per_day": 6.0, "minutes_per_session": 20.0,
         "ads_per_day": 0.0, "usd_per_day": 5.0},
    ],
    "usd_to_soft_at_level": {"base": 500.0, "growth": 1.12},
}


def cost_of_level(model, level):
    up = model["upgrade"]
    return up["base_cost"] * (up["cost_growth"] ** level)


def income_per_minute(model, level):
    up = model["upgrade"]
    return up["base_income_per_minute"] + up["income_per_level"] * level


def usd_value_in_soft(model, level):
    conv = model["usd_to_soft_at_level"]
    return conv["base"] * (conv["growth"] ** level)


def gate_limit(model, day):
    limit = None
    for gate in model.get("gates", []):
        if day < gate["unlock_day"]:
            limit = gate["max_level"] if limit is None else min(limit, gate["max_level"])
    return limit


def simulate_persona(model, persona):
    level = 0
    balance = 0.0
    rows = []
    for day in range(1, model["days"] + 1):
        rate = income_per_minute(model, level)
        active_minutes = persona["sessions_per_day"] * persona["minutes_per_session"]
        offline_minutes = (
            model["offline"]["cap_hours"] * 60.0
            * model["offline"]["rate_multiplier"]
            * max(persona["sessions_per_day"] - 1.0, 1.0)
        )
        earned_active = rate * active_minutes
        earned_offline = rate * offline_minutes
        earned_ads = rate * model["ad"]["reward_in_minutes_of_income"] * persona["ads_per_day"]
        earned_daily = rate * model["daily_bonus_in_minutes_of_income"]
        earned_usd = persona["usd_per_day"] * usd_value_in_soft(model, level)
        earned = earned_active + earned_offline + earned_ads + earned_daily + earned_usd

        balance += earned
        spent = 0.0
        bought = 0
        cap = gate_limit(model, day)
        while True:
            if cap is not None and level >= cap:
                break
            price = cost_of_level(model, level)
            if balance < price:
                break
            balance -= price
            spent += price
            level += 1
            bought += 1
            if bought > 100000:
                break

        next_price = cost_of_level(model, level)
        rate_after = income_per_minute(model, level)
        minutes_to_next = (next_price - balance) / rate_after if rate_after > 0 else float("inf")
        rows.append({
            "day": day,
            "level": level,
            "bought": bought,
            "income_per_minute": round(rate_after, 2),
            "earned": round(earned, 2),
            "spent": round(spent, 2),
            "balance": round(balance, 2),
            "minutes_to_next": round(max(minutes_to_next, 0.0), 1),
            "gated": int(cap is not None and level >= cap),
        })
    return rows


def use_utf8_stdout():
    try:
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
        return True
    except (AttributeError, ValueError):
        return False


def sparkline(values, unicode_ok=True):
    glyphs = "▁▂▃▄▅▆▇█" if unicode_ok else ".:-=+*#@"
    lo, hi = min(values), max(values)
    if hi - lo < 1e-9:
        return glyphs[0] * len(values)
    return "".join(glyphs[int((v - lo) / (hi - lo) * (len(glyphs) - 1))] for v in values)


def diagnose(model, rows):
    notes = []
    stalls = [r["day"] for r in rows if r["bought"] == 0 and not r["gated"]]
    if stalls:
        first = stalls[0]
        streak = 1
        for a, b in zip(stalls, stalls[1:]):
            if b == a + 1:
                streak += 1
            else:
                break
        notes.append(f"стена: с дня {first}, без покупок подряд дней: {streak}")
    gated = [r["day"] for r in rows if r["gated"]]
    if gated:
        notes.append(f"упирается в гейт в дни: {gated[0]}–{gated[-1]}")
    tail = rows[-1]
    if tail["balance"] > cost_of_level(model, tail["level"]) * 5:
        notes.append("инфляция: к концу прогона баланс кратно превышает следующую покупку")
    waits = [r["minutes_to_next"] for r in rows if r["minutes_to_next"] != float("inf")]
    if waits:
        jumps = [
            (rows[i]["day"], round(waits[i] / max(waits[i - 1], 0.01), 1))
            for i in range(1, len(waits))
            if waits[i] > waits[i - 1] * 3 and waits[i] > 5
        ]
        if jumps:
            day, factor = jumps[0]
            notes.append(f"скачок ожидания в день {day}: время до покупки выросло в {factor}x")
    if not notes:
        notes.append("явных стен, гейтов и инфляции не найдено")
    return notes


def main():
    parser = argparse.ArgumentParser(
        description="Прогон модели прогрессии по персонам. Без модели считает демо-конфиг.")
    parser.add_argument("--model", type=Path,
                        help="JSON с моделью; --dump-model покажет структуру по умолчанию")
    parser.add_argument("--out", type=Path, default=Path("sim_out"),
                        help="папка для CSV по каждой персоне")
    parser.add_argument("--dump-model", action="store_true",
                        help="напечатать модель по умолчанию и выйти")
    args = parser.parse_args()
    unicode_ok = use_utf8_stdout()

    if args.dump_model:
        json.dump(DEFAULT_MODEL, sys.stdout, ensure_ascii=False, indent=2)
        print()
        return

    model = DEFAULT_MODEL
    if args.model:
        model = json.loads(args.model.read_text(encoding="utf-8"))
        for key, value in DEFAULT_MODEL.items():
            model.setdefault(key, value)

    args.out.mkdir(parents=True, exist_ok=True)
    print(f"модель: {model['name']}, дней: {model['days']}\n")

    for persona in model["personas"]:
        rows = simulate_persona(model, persona)
        path = args.out / f"{persona['name']}.csv"
        with path.open("w", newline="", encoding="utf-8") as handle:
            writer = csv.DictWriter(handle, fieldnames=list(rows[0].keys()))
            writer.writeheader()
            writer.writerows(rows)

        levels = [r["level"] for r in rows]
        balances = [r["balance"] for r in rows]
        print(f"{persona['name']}: уровень к дню {model['days']} = {levels[-1]}, "
              f"баланс = {balances[-1]:.0f}")
        print(f"  прогресс {sparkline(levels, unicode_ok)}")
        print(f"  баланс   {sparkline(balances, unicode_ok)}")
        for note in diagnose(model, rows):
            print(f"  - {note}")
        print(f"  csv: {path}")
        print()

    spread = {p["name"]: simulate_persona(model, p)[-1]["level"] for p in model["personas"]}
    lowest = min(spread.values())
    highest = max(spread.values())
    if lowest > 0:
        print(f"разрыв между крайними персонами: {highest / lowest:.1f}x")


if __name__ == "__main__":
    main()

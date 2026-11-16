import argparse
import csv
import json
import random
import sys
from pathlib import Path

DEFAULT_CONFIG = {
    "obstacles": [
        {"name": "Obstacle 1", "weight": 10, "min_level": 1},
        {"name": "Obstacle 2", "weight": 10, "min_level": 1},
        {"name": "Obstacle 3", "weight": 8, "min_level": 5},
        {"name": "Obstacle 4", "weight": 6, "min_level": 15},
        {"name": "Obstacle 5", "weight": 4, "min_level": 30},
    ],
    "difficulty_distribution": {"e": 30, "m": 50, "h": 10, "sh": 10},
    "goals_per_level": {"min": 2, "max": 4, "typical": 3},
    "special_every": 10,
    "special_difficulty": "e",
    "avoid_repeat_window": 3,
}

DIFFICULTY_NAMES = {"e": "Easy", "m": "Medium", "h": "Hard", "sh": "Super Hard"}


def block_difficulties(config, block_size, rng):
    dist = config["difficulty_distribution"]
    total_weight = sum(dist.values())
    quota = []
    for key, weight in dist.items():
        quota.extend([key] * round(block_size * weight / total_weight))
    while len(quota) < block_size:
        quota.append(max(dist, key=dist.get))
    quota = quota[:block_size]
    rng.shuffle(quota)
    return quota


def exact_difficulties(config, count, rng):
    block_size = config["special_every"] or 10
    result = []
    while len(result) < count:
        result.extend(block_difficulties(config, block_size, rng))
    return result[:count]


def pick_obstacles(config, level, rng):
    available = [o for o in config["obstacles"] if level >= o.get("min_level", 1)]
    if not available:
        available = config["obstacles"][:1]
    goals = config["goals_per_level"]
    count = min(rng.randint(goals["min"], goals["max"]), len(available))
    chosen = []
    pool = list(available)
    for _ in range(count):
        weights = [o["weight"] for o in pool]
        picked = rng.choices(pool, weights=weights, k=1)[0]
        chosen.append(picked["name"])
        pool.remove(picked)
    return sorted(chosen)


def generate(config, start, count, seed):
    rng = random.Random(seed)
    difficulties = exact_difficulties(config, count, rng)
    recent = []
    plan = []
    for index in range(count):
        level = start + index
        is_special = config["special_every"] and level % config["special_every"] == 0
        if is_special and difficulties[index] != config["special_difficulty"]:
            swap_with = next(
                (i for i in range(index + 1, len(difficulties))
                 if difficulties[i] == config["special_difficulty"]), None)
            if swap_with is not None:
                difficulties[index], difficulties[swap_with] = (
                    difficulties[swap_with], difficulties[index])
        difficulty = config["special_difficulty"] if is_special else difficulties[index]

        combo = None
        for _ in range(20):
            candidate = pick_obstacles(config, level, rng)
            if tuple(candidate) not in recent:
                combo = candidate
                break
        if combo is None:
            combo = pick_obstacles(config, level, rng)

        recent.append(tuple(combo))
        if len(recent) > config["avoid_repeat_window"]:
            recent.pop(0)

        plan.append({
            "level": level,
            "goals": " - ".join(combo),
            "difficulty": difficulty,
            "difficulty_name": DIFFICULTY_NAMES.get(difficulty, difficulty),
            "notes": "Special Level" if is_special else "Regular",
        })
    return plan


def print_markdown(plan):
    print("| Level | Goals | Difficulty | Notes |")
    print("|---|---|---|---|")
    for row in plan:
        print(f"| {row['level']} | {row['goals']} | {row['difficulty']} | {row['notes']} |")


def print_summary(plan):
    counts = {}
    for row in plan:
        counts[row["difficulty"]] = counts.get(row["difficulty"], 0) + 1
    total = len(plan)
    print()
    print("распределение сложности:")
    for key, name in DIFFICULTY_NAMES.items():
        got = counts.get(key, 0)
        print(f"  {name:<11} {got:>3}  ({got / total * 100:.0f}%)")


def main():
    parser = argparse.ArgumentParser(
        description="План пака уровней: случайные комбинации препятствий и разметка сложности. "
                    "Нужен, чтобы дизайнер не собирал раз за разом привычные комбинации.")
    parser.add_argument("--config", type=Path, help="JSON с препятствиями и весами")
    parser.add_argument("--start", type=int, default=1, help="номер первого уровня пака")
    parser.add_argument("--count", type=int, default=100, help="сколько уровней спланировать")
    parser.add_argument("--seed", type=int, help="фиксированный seed для повторяемости")
    parser.add_argument("--csv", type=Path, help="сохранить план в CSV")
    parser.add_argument("--dump-config", action="store_true",
                        help="напечатать конфиг по умолчанию и выйти")
    args = parser.parse_args()

    try:
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    except (AttributeError, ValueError):
        pass

    if args.dump_config:
        json.dump(DEFAULT_CONFIG, sys.stdout, ensure_ascii=False, indent=2)
        print()
        return

    config = DEFAULT_CONFIG
    if args.config:
        config = json.loads(args.config.read_text(encoding="utf-8"))
        for key, value in DEFAULT_CONFIG.items():
            config.setdefault(key, value)

    plan = generate(config, args.start, args.count, args.seed)
    print_markdown(plan)
    print_summary(plan)

    if args.csv:
        with args.csv.open("w", newline="", encoding="utf-8") as handle:
            writer = csv.DictWriter(handle, fieldnames=list(plan[0].keys()))
            writer.writeheader()
            writer.writerows(plan)
        print(f"\ncsv: {args.csv}")


if __name__ == "__main__":
    main()

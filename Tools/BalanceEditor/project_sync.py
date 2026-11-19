"""Read and safely update an existing Rubezh Unity balance configuration.

The compiled C# literals are authoritative; JSON weapon-config is display metadata.
All writes are guarded by a SHA-256 revision and keep a dated backup.
"""

from pathlib import Path
import hashlib
import json
import re
import tempfile
import os
import datetime
import math
from apply_balance import FIELDS, LINKS, current_link, validate
from source_match import field_matches, literal_matches, replace_literal

WEAPON_FIELDS = (
    "range",
    "accuracy",
    "damage",
    "interval",
    "magazine",
    "suppression",
    "morale",
    "falloff",
)
# Capture only the numeric text; replacing groups preserves comments and layout.
CS_FLOAT = r"((?:\d+(?:\.\d*)?|\.\d+)(?:[eE][+-]?\d+)?)[fF]?"
SPEC_RE = re.compile(
    r"new\s+Spec\s*\(\s*"
    + r"\s*,\s*".join(
        r"(\d+)" if key == "magazine" else CS_FLOAT for key in WEAPON_FIELDS
    )
    + r"\s*\)"
)


def validate_project(directory):
    p = Path(directory).expanduser().resolve()
    for rel in (
        "Assets/Rubezh/Core/BattleSimulation.cs",
        "Assets/Rubezh/Core/WeaponBalance.cs",
        "Packages/manifest.json",
        "ProjectSettings/ProjectVersion.txt",
    ):
        if not (p / rel).is_file():
            raise ValueError("Указан не корень совместимого Unity-проекта. Нет " + rel)
    return p


def watched_paths(p):
    rel = {
        "Assets/Rubezh/Core/WeaponBalance.cs",
        "Tools/BalanceEditor/weapon-config.json",
        "Tools/BalanceEditor/balance-profile.json",
    }
    rel.update(field["source"] for field in FIELDS.values())
    return sorted(rel)


def revision(p):
    h = hashlib.sha256()
    for rel in watched_paths(p):
        f = p / rel
        h.update(rel.encode("utf-8"))
        h.update(b"\0")
        h.update(f.read_bytes() if f.is_file() else b"NOT_PRESENT")
        h.update(b"\0")
    return h.hexdigest()


def existing_literals(p):
    result = {}
    missing = []
    for field_id, field in FIELDS.items():
        source = p / field["source"]
        if not source.is_file():
            raise ValueError("Не найден исходник " + str(source))
        matches = field_matches(field, source.read_text(encoding="utf-8-sig"))
        if len(matches) != 1:
            missing.append(field_id)
            continue
        x = float(matches[0].group("num"))
        result[field_id] = int(x) if field["is_int"] else x
    return result, missing


def existing_weapons(p, default):
    source = p / "Assets/Rubezh/Core/WeaponBalance.cs"
    text = source.read_text(encoding="utf-8-sig")
    rows = SPEC_RE.findall(text)
    if len(rows) != 12:
        raise ValueError(
            f"В WeaponBalance.cs ожидалось 12 оружейных профилей, найдено {len(rows)}; загрузка отменена"
        )
    out = json.loads(json.dumps(default))
    if (
        out.get("format") != "rubezh-weapon-config-v2"
        or len(out.get("weapons", [])) != 12
    ):
        raise ValueError("Повреждена схема редактора оружия")
    for i, row in enumerate(rows):
        for key, val in zip(WEAPON_FIELDS, row):
            out["weapons"][i][key] = int(val) if key == "magazine" else float(val)
    return out


def read_current(p, default_weapons):
    p = validate_project(p)
    weapons = existing_weapons(p, default_weapons)
    actual, missing = existing_literals(p)
    oldprofile = p / "Tools/BalanceEditor/balance-profile.json"
    if oldprofile.is_file():
        try:
            x = json.loads(oldprofile.read_text(encoding="utf-8-sig"))
            if x.get("format") == "rubezh-balance-profile-v1" and isinstance(
                x.get("values"), dict
            ):
                validate(x)
        except (OSError, ValueError, KeyError, json.JSONDecodeError) as exc:
            raise ValueError("Повреждён профиль в Unity-проекте: " + str(exc)) from exc
    values = dict(actual)  # Unavailable fields have no fabricated numeric value.
    warnings = []
    if missing:
        warnings.append(
            f"Для {len(missing)} старых параметров в реальном C# больше нет прямых числовых полей: "
            + ", ".join(missing)
            + ". Они отображаются как справочные, не применяются без отдельного адаптера."
        )
    return {
        "ok": True,
        "project": str(p),
        "revision": revision(p),
        "weapons": weapons,
        "balance": {
            "format": "rubezh-balance-profile-v1",
            "game_version": "0.11.29",
            "title": "Текущий баланс из Unity",
            "values": values,
            "unavailable": missing,
        },
        "missing": missing,
        "warnings": warnings,
    }


def float_cs(value, integer):
    if integer:
        return str(int(value))
    token = f"{float(value):.8g}"
    if "." not in token and "e" not in token:
        token += ".0"
    return token


def plan_balance_updates(p, data, current):
    validate(data)
    requested = data.get("values", {})
    old = current["balance"]["values"]
    missing = set(current["missing"])
    invalid = [k for k in missing if k in requested]
    if invalid:
        raise ValueError(
            "В актуальном игровом коде эти поля уже не используются напрямую: "
            + ", ".join(invalid)
            + ". Изменения не применены."
        )
    replacements = {}
    edits = {}
    originals = {}
    changed = []
    for k, field in FIELDS.items():
        if k in missing:
            continue
        prior = old[k]
        new = requested.get(k, prior)
        if abs(new - prior) <= 1e-8:
            continue
        path = p / field["source"]
        text = originals.setdefault(path, path.read_text(encoding="utf-8-sig"))
        hits = field_matches(field, text)
        if len(hits) != 1:
            raise ValueError(f"{k}: нет однозначного параметра в {path.name}")
        m = hits[0]
        if abs(float(m.group("num")) - prior) > 1e-6:
            raise ValueError(f"{k}: исходный параметр изменён вне редактора")
        token = float_cs(new, field["is_int"])
        edits.setdefault(path, []).append((m.start("num"), m.end("num"), token))
        changed.append(k)
    for path, changes in edits.items():
        text = originals[path]
        last = len(text) + 1
        for start, end, token in sorted(changes, reverse=True):
            if end > last:
                raise ValueError("Перекрывающиеся числовые параметры: " + str(path))
            text = text[:start] + token + text[end:]
            last = start
        replacements[path] = text
    for keys, relative, old_formula, new_formula in LINKS:
        if not any(k in changed for k in keys):
            continue
        path = p / relative
        text = replacements.get(path, path.read_text(encoding="utf-8-sig"))
        old_formula, new_formula = current_link(old_formula, new_formula, text)
        if "__HP_CAP__" in new_formula:
            # Can update an already linked HP cap without recreating stale source.
            hp = requested.get("hp", FIELDS["hp"]["default"])
            before = re.compile(r"(?P<n>\d+(?:\.\d+)?)f\s*-\s*patient\s*\.\s*Health")
            match = before.search(text)
            if not match:
                raise ValueError("Не удалось найти предел лечения, изменено вручную")
            newer = float_cs(hp, False) + "f-patient.Health"
            replacements[path] = text[: match.start()] + newer + text[match.end() :]
            continue
        # Original linker may use rshg_radius substitution. Link it once; later
        # edits change the authoritative numeric field rather than the expression.
        if "rshg_radius" in changed and "return (s.LauncherKind" in old_formula:
            # old_formula may have already been replaced by the expression.
            if literal_matches(new_formula, text):
                continue
            val = requested.get("rshg_radius", FIELDS["rshg_radius"]["default"])
            replacement = float_cs(val, False) + "f"
            old_formula = old_formula.replace("4.3f", replacement)
            new_formula = new_formula.replace("4.3f", replacement)
        if literal_matches(new_formula, text):
            continue
        if len(literal_matches(old_formula, text)) != 1:
            raise ValueError(
                "Расчётная формула изменилась, автоматическая запись опасна: "
                + relative
                + " / "
                + old_formula[:75]
            )
        replacements[path] = replace_literal(text, old_formula, new_formula)
    return replacements


def prepare_weapon_code(data, templates, source):
    if not isinstance(data, dict) or data.get("format") != "rubezh-weapon-config-v2":
        raise ValueError("Неверный формат оружейной конфигурации")
    ws = data.get("weapons")
    if not isinstance(ws, list) or len(ws) != 12:
        raise ValueError("Нужны 12 профилей оружия")
    rows = list(SPEC_RE.finditer(source))
    if len(rows) != len(ws):
        raise ValueError("Ожидались 12 профилей в исходнике WeaponBalance")
    edits = []
    for idx, (weapon, row) in enumerate(zip(ws, rows)):
        if not isinstance(weapon, dict) or weapon.get("id") != idx:
            raise ValueError("Неверный порядок оружия")
        for column, name in enumerate(WEAPON_FIELDS, start=1):
            v = weapon.get(name)
            if (
                isinstance(v, bool)
                or not isinstance(v, (int, float))
                or not math.isfinite(v)
                or not (1e-5 <= v <= 10000)
            ):
                raise ValueError(f"Некорректное значение оружия {idx} / {name}")
            if name == "magazine" and (int(v) != v or v > 1000):
                raise ValueError("Магазин должен быть целым от 1 до 1000")
            if float(row.group(column)) != v:
                start, end = row.span(column)
                # Only numeric spans change. Keep the existing suffix, layout and comments.
                token = str(int(v)) if name == "magazine" else format(v, ".9g")
                if name != "magazine" and source[end : end + 1].lower() != "f":
                    token += "f"
                edits.append((start, end, token))
    for start, end, token in reversed(edits):
        source = source[:start] + token + source[end:]
    return source


def write_atomic(path, data):
    path.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.NamedTemporaryFile("wb", dir=path.parent, delete=False) as f:
        f.write(data)
        temp = Path(f.name)
    try:
        os.replace(temp, path)
    finally:
        temp.unlink(missing_ok=True)


def commit(p, expected_revision, weapons, balance, templates):
    p = validate_project(p)
    if not isinstance(expected_revision, str) or revision(p) != expected_revision:
        raise ValueError(
            "Конфликт: файлы Unity изменились после загрузки. Нажмите «Загрузить из Unity» и сравните значения."
        )
    current = read_current(p, templates)
    patches = plan_balance_updates(p, balance, current)
    patches[p / "Assets/Rubezh/Core/WeaponBalance.cs"] = prepare_weapon_code(
        weapons,
        templates,
        (p / "Assets/Rubezh/Core/WeaponBalance.cs").read_text(encoding="utf-8"),
    )
    patches[p / "Tools/BalanceEditor/weapon-config.json"] = (
        json.dumps(weapons, indent=2, ensure_ascii=False) + "\n"
    )
    patches[p / "Tools/BalanceEditor/balance-profile.json"] = (
        json.dumps(balance, indent=2, ensure_ascii=False) + "\n"
    )
    # Back up all existing destinations before doing any write.
    stamp = datetime.datetime.now().strftime("%Y%m%d-%H%M%S-%f")
    backup = p / "BalanceEditorBackups" / stamp
    data = {
        dest: (content.encode("utf-8") if isinstance(content, str) else content)
        for dest, content in patches.items()
    }
    original = {dest: (dest.read_bytes() if dest.exists() else None) for dest in data}
    for dest, src in original.items():
        if src is None:
            continue
        dest_backup = backup / dest.relative_to(p)
        dest_backup.parent.mkdir(parents=True, exist_ok=True)
        dest_backup.write_bytes(src)
    modified = []
    try:
        for dest, content in data.items():
            if original[dest] != content:
                write_atomic(dest, content)
                modified.append(dest)
    except Exception:
        for dest in reversed(modified):
            old = original[dest]
            if old is None:
                dest.unlink(missing_ok=True)
            else:
                write_atomic(dest, old)
        raise
    return {
        "ok": True,
        "changed": [str(x.relative_to(p)) for x in modified],
        "backup": str(backup),
        "revision": revision(p),
        "message": "Конфигурация сохранена. Unity обнаружит изменения в исходниках.",
    }


def save_named(p, name, weapons, balance, templates):
    p = validate_project(p)
    if not isinstance(name, str) or not 1 <= len(name.strip()) <= 70:
        raise ValueError("Введите название профиля (1–70 символов)")
    safe = re.sub(r"[^\w\-]+", "-", name.strip(), flags=re.UNICODE).strip("-_")[:64]
    if safe in ("", ".", ".."):
        raise ValueError("Недопустимое название профиля")
    validate(balance)
    prepare_weapon_code(
        weapons,
        templates,
        (p / "Assets/Rubezh/Core/WeaponBalance.cs").read_text(encoding="utf-8"),
    )
    folder = p / "BalanceEditorProfiles"
    folder.mkdir(exist_ok=True)
    target = folder / (safe + ".json")
    i = 2
    while target.exists():
        target = folder / (safe + f"-{i}.json")
        i += 1
    content = {
        "format": "rubezh-balance-snapshot-v2",
        "game_version": "0.11.29",
        "title": name.strip(),
        "saved_at": datetime.datetime.now(datetime.timezone.utc).isoformat(),
        "weapons": weapons,
        "balance": balance,
    }
    write_atomic(
        target,
        (json.dumps(content, ensure_ascii=False, indent=2) + "\n").encode("utf-8"),
    )
    return {"ok": True, "path": str(target), "name": target.name}

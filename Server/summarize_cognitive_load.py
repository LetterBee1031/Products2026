"""ユーザ・ex_statusごとのL_cur_raw平均をServer/dataにCSV保存する。

実行例: python Server/summarize_cognitive_load.py
同じex_statusの記録は、block_idや記録日に関係なくまとめて集計する。
行はユーザID、列はex_status、セルは平均値。記録がない組み合わせは空欄。
"""

import csv
import json
import math
import sys
from collections import defaultdict
from pathlib import Path
from statistics import mean


DATA_DIR = Path(__file__).resolve().parent / "data"
INPUT_PREFIX = "cognitive_load_prediction_"
OUTPUT_NAME = "cognitive_load_raw_mean_by_user_status.csv"
OUTPUT_STATUSES = [
    "stroop_congruent_start",
    "stroop_neutral_start",
    "stroop_incongruent_start",
    "mental_arithmetic_low_start",
    "mental_arithmetic_medium_start",
    "mental_arithmetic_high_start",
    "visual_low_start",
    "visual_medium_start",
    "visual_high_start",
]


def summarize(data_dir: Path = DATA_DIR) -> Path:
    prediction_dir = data_dir / "experience" / "cognitive_load_prediction"
    paths = sorted(prediction_dir.glob(f"{INPUT_PREFIX}*.jsonl"))
    if not paths:
        raise FileNotFoundError(f"推論記録が見つかりません: {prediction_dir}")

    groups = defaultdict(list)
    skipped = 0
    for path in paths:
        # ファイル名のIDを使い、01などの先頭ゼロも保持する。
        user_id = path.stem[len(INPUT_PREFIX):]
        with path.open(encoding="utf-8-sig") as source:
            for line_number, line in enumerate(source, start=1):
                if not line.strip():
                    continue
                try:
                    record = json.loads(line)
                    if not isinstance(record, dict):
                        raise ValueError("JSONオブジェクトではありません")
                    status = record["ex_status"]
                    if not isinstance(status, str) or not status.strip():
                        raise ValueError("ex_statusが空または文字列ではありません")
                    value = record["L_cur_raw"]
                    if isinstance(value, bool) or not isinstance(value, (int, float)):
                        raise ValueError("L_cur_rawが数値ではありません")
                    if not math.isfinite(value):
                        raise ValueError("L_cur_rawが有限値ではありません")
                except (ValueError, KeyError) as error:
                    skipped += 1
                    print(f"スキップ: {path.name}:{line_number}: {error}", file=sys.stderr)
                    continue
                groups[(user_id, status)].append(value)

    if not groups:
        raise ValueError("平均を計算できる有効な記録がありません")

    output_path = data_dir / OUTPUT_NAME
    user_ids = sorted({user_id for user_id, _ in groups})
    statuses = OUTPUT_STATUSES
    # Excelで日本語を表示できるようUTF-8 BOM付きで保存する。
    with output_path.open("w", newline="", encoding="utf-8-sig") as output:
        writer = csv.writer(output)
        writer.writerow(["user_id", *statuses])
        for user_id in user_ids:
            writer.writerow([
                user_id,
                *(
                    mean(groups[(user_id, status)])
                    if (user_id, status) in groups else ""
                    for status in statuses
                ),
            ])

    print(f"保存先: {output_path}")
    print(f"入力: {len(paths)}ファイル / 集計: {len(groups)}組 / スキップ: {skipped}行")
    return output_path


if __name__ == "__main__":
    summarize()

import json
import pandas as pd

def read_last_n_jsonl_as_dataframe(file_path: str, n: int) -> pd.DataFrame:
    if n <= 0:
        return pd.DataFrame()

    with open(file_path, "rb") as f:
        f.seek(0, 2)  # ファイル末尾へ
        file_size = f.tell()

        if file_size == 0:
            return pd.DataFrame()

        buffer = bytearray()
        pointer = file_size - 1
        newline_count = 0

        while pointer >= 0:
            f.seek(pointer)
            byte = f.read(1)
            buffer.extend(byte)

            if byte == b"\n":
                newline_count += 1
                if newline_count > n:
                    break

            pointer -= 1

        text = buffer[::-1].decode("utf-8").strip()
        if not text:
            return pd.DataFrame()

        lines = text.splitlines()
        records = [json.loads(line) for line in lines[-n:] if line.strip()]

    return pd.DataFrame(records)
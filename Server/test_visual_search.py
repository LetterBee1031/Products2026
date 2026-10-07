"""視覚探索APIの受信・検証・JSONL保存を実際のASGIアプリで確認する。"""
import asyncio
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

from Server import server2


async def post_json(payload):
    # httpx等の追加依存なしで、FastAPIのルーティングと検証を通す。
    body = json.dumps(payload).encode("utf-8")
    scope = {
        "type": "http", "asgi": {"version": "3.0"}, "http_version": "1.1",
        "method": "POST", "scheme": "http", "path": "/api/visual_search_log",
        "raw_path": b"/api/visual_search_log", "query_string": b"",
        "root_path": "", "headers": [(b"content-type", b"application/json")],
        "client": ("127.0.0.1", 1234), "server": ("test", 80),
    }
    messages = []

    async def receive():
        return {"type": "http.request", "body": body, "more_body": False}

    async def send(message):
        messages.append(message)

    await server2.app(scope, receive, send)
    status = next(m["status"] for m in messages if m["type"] == "http.response.start")
    result = b"".join(m.get("body", b"") for m in messages if m["type"] == "http.response.body")
    return status, json.loads(result)


class VisualSearchApiTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.directory.cleanup)
        self.data_dir = Path(self.directory.name)
        self.patch = patch.object(server2, "DATA_DIR", self.data_dir)
        self.patch.start()
        self.addCleanup(self.patch.stop)
        self.payload = {
            "user_id": "test_01", "block_id": "visual_1", "difficulty": "High",
            "trial_index": 1, "is_practice": False, "target_present": True,
            "is_correct": False, "reaction_time_ms": 321.25,
            "randomSeed": -12345, "sent_at": "2026/10/06 17:42:31",
        }

    def test_complete_trials_are_appended_with_server_time(self):
        for difficulty, block_id, present in [
            ("High", "visual_1", True), ("Practice", "visual_0", False)
        ]:
            payload = dict(self.payload, difficulty=difficulty, block_id=block_id,
                           is_practice=difficulty == "Practice", target_present=present)
            status, result = asyncio.run(post_json(payload))
            self.assertEqual(200, status)
            self.assertTrue(result["ok"])
        records = [json.loads(line) for line in
                   (self.data_dir / "visual_search_log_test_01.jsonl").read_text().splitlines()]
        self.assertEqual(2, len(records))
        self.assertEqual("visual_1", records[0]["block_id"])
        self.assertEqual("visual_0", records[1]["block_id"])
        self.assertEqual(321.25, records[0]["reaction_time_ms"])
        self.assertEqual(-12345, records[0]["randomSeed"])
        self.assertEqual(self.payload["sent_at"], records[0]["sent_at"])
        self.assertTrue(records[0]["received_at"].endswith("+09:00"))
        self.assertFalse(records[0]["is_correct"])
        self.assertTrue(records[1]["is_practice"])
        self.assertFalse(records[1]["target_present"])

    def test_invalid_payloads_are_rejected_without_writing(self):
        invalid = [
            {"received_at": "client-supplied"}, {"difficulty": "Unknown"},
            {"is_practice": True}, {"trial_index": 0}, {"trial_index": 1.5},
            {"block_id": "1"}, {"block_id": "visual_low"},
            {"block_id": "visual_-1"}, {"reaction_time_ms": -0.1},
            {"reaction_time_ms": "NaN"}, {"reaction_time_ms": "Infinity"},
            {"randomSeed": 2**31}, {"user_id": " "}, {"sent_at": " "},
            {"is_correct": "true"},
        ]
        for changes in invalid:
            with self.subTest(changes=changes):
                status, _ = asyncio.run(post_json(dict(self.payload, **changes)))
                self.assertEqual(422, status)
        missing = dict(self.payload)
        del missing["target_present"]
        self.assertEqual(422, asyncio.run(post_json(missing))[0])
        self.assertEqual([], list(self.data_dir.iterdir()))

    def test_user_file_path_is_normalized(self):
        status, result = asyncio.run(post_json(dict(self.payload, user_id="../other")))
        self.assertEqual(200, status)
        self.assertEqual("___other", result["user_id"])
        self.assertEqual(["visual_search_log____other.jsonl"],
                         [p.name for p in self.data_dir.iterdir()])


if __name__ == "__main__":
    unittest.main()

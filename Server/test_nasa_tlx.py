"""NASA-TLX single-item and six-item API regression checks."""
import asyncio
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
from Server import server2

async def post_json(payload, mode="raw_tlx"):
    # httpx等の追加依存なしで、FastAPIのルーティングと検証を通す。
    body = json.dumps(payload).encode("utf-8")
    scope = {
        "type": "http", "asgi": {"version": "3.0"}, "http_version": "1.1",
        "method": "POST", "scheme": "http", "path": "/api/nasa_tlx",
        "raw_path": b"/api/nasa_tlx", "query_string": ("mode=" + mode).encode(),
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


class NasaTlxApiTests(unittest.TestCase):
    def setUp(self):
        directory = tempfile.TemporaryDirectory()
        self.addCleanup(directory.cleanup)
        self.data_dir = Path(directory.name)
        patched = patch.object(server2, "DATA_DIR", self.data_dir)
        patched.start()
        self.addCleanup(patched.stop)
        self.payload = {"user_id": "test_tlx", "block_id": "stroop_1",
                        "n_back_correct_count": 0, "mental_demand": 14}

    def saved(self):
        path = self.data_dir / "experience" / "NASA-TLX" / "NASA-TLX_data_test_tlx.jsonl"
        return [json.loads(line) for line in path.read_text().splitlines()]

    def test_mental_only_accepts_missing_and_null_scales(self):
        for extra in ({}, {name: None for name in
                      ["physical_demand", "temporal_demand", "performance", "effort", "frustration"]}):
            status, result = asyncio.run(post_json(dict(self.payload, **extra), "mental_only"))
            self.assertEqual(status, 200)
            self.assertEqual(result["RawTLX"], 14)
        for record in self.saved():
            self.assertEqual(record["subjective_mode"], "mental_only")
            self.assertEqual(record["block_id"], "stroop_1")
            self.assertEqual(record["mental_demand"], 14)
            for name in ["physical_demand", "temporal_demand", "performance", "effort", "frustration"]:
                self.assertIsNone(record[name])

    def test_full_mode_preserves_mean(self):
        payload = dict(self.payload, physical_demand=2, temporal_demand=4,
                       performance=6, effort=8, frustration=8)
        status, result = asyncio.run(post_json(payload))
        self.assertEqual(status, 200)
        self.assertEqual(result["RawTLX"], 7)
        self.assertEqual(self.saved()[0]["subjective_mode"], "raw_tlx")
        # Legacy mental_only callers may still supply all six scales.
        status, result = asyncio.run(post_json(payload, "mental_only"))
        self.assertEqual(status, 200)
        self.assertEqual(result["RawTLX"], 14)

    def test_full_mode_rejects_incomplete_answers(self):
        status, _ = asyncio.run(post_json(self.payload))
        self.assertEqual(status, 422)
        self.assertFalse(list(self.data_dir.rglob("*.jsonl")))

    def test_mental_only_requires_mental_demand(self):
        payload = dict(self.payload)
        del payload["mental_demand"]
        status, _ = asyncio.run(post_json(payload, "mental_only"))
        self.assertEqual(status, 422)

    def test_unknown_mode_rejected(self):
        status, _ = asyncio.run(post_json(self.payload, "unknown"))
        self.assertEqual(status, 400)

if __name__ == "__main__":
    unittest.main()

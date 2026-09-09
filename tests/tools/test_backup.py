import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location("backup", ROOT / "tools/verify-json-backup.py")
BACKUP = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(BACKUP)


class BackupTests(unittest.TestCase):
    def test_current_and_legacy_fixtures_round_trip_without_modifying_source(self):
        for name in ("store-v5.json", "store-v7.json"):
            with self.subTest(name=name), tempfile.TemporaryDirectory() as directory:
                source = ROOT / "tests/fixtures" / name
                before = source.read_bytes()
                destination = Path(directory) / "restored.json"
                report = BACKUP.verify(source, destination)
                self.assertTrue(report["restoreCopyVerified"])
                self.assertEqual(before, destination.read_bytes())
                self.assertEqual(before, source.read_bytes())

    def test_refuses_overwrite(self):
        with tempfile.TemporaryDirectory() as directory:
            destination = Path(directory) / "existing.json"
            destination.write_bytes(b"DO NOT CHANGE")
            with self.assertRaises(FileExistsError):
                BACKUP.verify(ROOT / "tests/fixtures/store-v7.json", destination)
            self.assertEqual(b"DO NOT CHANGE", destination.read_bytes())

    def test_orphans_duplicates_and_invalid_input_fail_before_copy(self):
        original = json.loads((ROOT / "tests/fixtures/store-v7.json").read_bytes())
        mutations = [
            lambda s: s["selections"][0].update(performanceId="absent"),
            lambda s: s["groups"][0].update(createdBy="absent"),
            lambda s: s["pushDevices"][0].update(username="absent"),
            lambda s: s["reminderDeliveries"][0].update(pushDeviceId="absent"),
            lambda s: s["users"].append(s["users"][0]),
            lambda s: s.update(dataVersion=99)
        ]
        for mutate in mutations:
            state = json.loads(json.dumps(original))
            mutate(state)
            with self.assertRaises(ValueError):
                BACKUP.validate(json.dumps(state).encode())
        with tempfile.TemporaryDirectory() as directory:
            source = Path(directory) / "bad.json"
            source.write_bytes(b"{invalid")
            destination = Path(directory) / "copy.json"
            with self.assertRaises(ValueError):
                BACKUP.verify(source, destination)
            self.assertFalse(destination.exists())


if __name__ == "__main__":
    unittest.main()

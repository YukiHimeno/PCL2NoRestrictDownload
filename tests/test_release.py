import importlib.util
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch


SCRIPT = Path(__file__).resolve().parents[1] / ".github/scripts/release.py"
spec = importlib.util.spec_from_file_location("release_automation", SCRIPT)
release_automation = importlib.util.module_from_spec(spec)
spec.loader.exec_module(release_automation)


class ReleaseTests(unittest.TestCase):
    def test_patch_changes_only_declaration(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            target = root / release_automation.SOURCE
            target.parent.mkdir(parents=True)
            source = b"' PCL\r\n    " + release_automation.OLD + b"\r\nOther = True\r\n"
            target.write_bytes(source)
            release_automation.patch(root)
            self.assertEqual(target.read_bytes(), source.replace(release_automation.OLD, release_automation.NEW))
            with self.assertRaises(ValueError):
                release_automation.patch(root)

    def test_patch_rejects_changed_or_duplicate_declarations(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            target = root / release_automation.SOURCE
            target.parent.mkdir(parents=True)
            for source in (b"No restriction declaration", release_automation.OLD + b"\n" + release_automation.OLD):
                target.write_bytes(source)
                with self.assertRaises(ValueError):
                    release_automation.patch(root)

    def test_selects_oldest_new_unpublished_release(self):
        first = {"id": 418095202, "tag_name": "2.99.0.0", "published_at": "2026-10-20T07:01:34Z", "draft": False}
        next_one = {"id": 418095203, "tag_name": "3.0.0.0", "published_at": "2026-10-21T07:01:34Z", "draft": False}
        with patch.object(release_automation, "api", side_effect=[[next_one, first], {"already": True}, None]) as api:
            self.assertEqual(release_automation.choose_release(), "3.0.0.0")
            self.assertIn("no-restrict-3.0.0.0", api.call_args.args[0])

    def test_rejects_unsafe_tag(self):
        candidate = {"id": 418095203, "tag_name": "../bad", "published_at": "2026-10-21T07:01:34Z", "draft": False}
        with patch.object(release_automation, "api", return_value=[candidate]):
            with self.assertRaises(ValueError):
                release_automation.choose_release()


if __name__ == "__main__":
    unittest.main()

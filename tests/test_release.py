import importlib.util
from pathlib import Path
import unittest
from unittest.mock import patch


SCRIPTS = Path(__file__).resolve().parents[1] / ".github/scripts"


def load(name):
    spec = importlib.util.spec_from_file_location(name, SCRIPTS / f"{name}.py")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


release = load("release")
validator = load("validate_patch")
ORIGINAL = b"' PCL\r\n    Public Const RestrictMcDownload As Boolean = True\r\nOther = True\r\n"


class ReleaseTests(unittest.TestCase):
    def test_candidate_must_disable_restriction(self):
        validator.validate(ORIGINAL, ORIGINAL.replace(b"Boolean = True", b"Boolean = False", 1))
        with self.assertRaises(ValueError):
            validator.validate(ORIGINAL, ORIGINAL)
        with self.assertRaises(ValueError):
            validator.validate(ORIGINAL, ORIGINAL.replace(b"Other = True", b"Other = False"))

    def test_candidate_rejects_duplicates_and_unrelated_edits(self):
        target = b"Public Const RestrictMcDownload As Boolean = False"
        changed = ORIGINAL.replace(b"Boolean = True", b"Boolean = False", 1)
        for modified in (target + b"\n" + target, changed.replace(b"Other = True", b"Other = False")):
            with self.assertRaises(ValueError):
                validator.validate(ORIGINAL, modified)

    def test_candidate_can_handle_changed_upstream_declaration(self):
        original = ORIGINAL.replace(b"Boolean = True", b"Boolean=If(IsChina, True, False)")
        modified = original.replace(b"Boolean=If(IsChina, True, False)", b"Boolean = False")
        validator.validate(original, modified)

    def test_windows_line_endings_do_not_look_like_extra_edits(self):
        original = ORIGINAL.replace(b"\r\n", b"\n")
        modified = ORIGINAL.replace(b"Boolean = True", b"Boolean = False")
        validator.validate(original, modified)
        with self.assertRaises(ValueError):
            validator.validate(original, ORIGINAL)

    def test_selects_oldest_new_unpublished_release_with_previous_tag(self):
        first = {"id": 418095202, "tag_name": "2.99.0.0", "published_at": "2026-10-20T07:01:34Z", "draft": False}
        next_one = {"id": 418095203, "tag_name": "3.0.0.0", "published_at": "2026-10-21T07:01:34Z", "draft": False}
        with patch.object(release, "api", side_effect=[[next_one, first], {"already": True}, None]) as api:
            self.assertEqual(release.choose_release(), ("3.0.0.0", "2.99.0.0"))
            self.assertIn("patch-3.0.0.0", api.call_args.args[0])

    def test_rejects_unsafe_tag(self):
        candidate = {"id": 418095203, "tag_name": "../bad", "published_at": "2026-10-21T07:01:34Z", "draft": False}
        with patch.object(release, "api", return_value=[candidate]):
            with self.assertRaises(ValueError):
                release.choose_release()


if __name__ == "__main__":
    unittest.main()

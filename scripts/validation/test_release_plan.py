import importlib.util
from pathlib import Path
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location("release_plan", ROOT / "scripts/release/shared/release_plan.py")
plan = importlib.util.module_from_spec(spec)
spec.loader.exec_module(plan)


class ReleasePlanTests(unittest.TestCase):
    def make(self, **changes):
        args = dict(scope="all", desktop="auto", android="auto", mode="approval", store_action="prepare",
                    source="a" * 40, ref="refs/heads/master", occupied=["v3.9.9", "v3.10.0", "android-v3.0.6"],
                    run_id="123", attempt="1")
        return plan.make_plan(**(args | changes))

    def test_scopes_and_independent_versions(self):
        for scope, platforms, desktop, android in (
            ("all", ["windows", "linux", "android"], "3.10.1", "3.0.7"),
            ("desktop", ["windows", "linux"], "3.10.1", None),
            ("mobile", ["android"], None, "3.0.7"),
        ):
            with self.subTest(scope=scope):
                result = self.make(scope=scope)
                self.assertEqual(platforms, result["platforms"])
                self.assertEqual(desktop, result["desktopVersion"])
                self.assertEqual(android, result["androidVersion"])
                self.assertEqual("a" * 40, result["sourceCommit"])
                self.assertEqual(["macos", "ios"], result["disabledPlatforms"])

    def test_explicit_major_and_minor(self):
        result = self.make(desktop="4.0.0", android="3.1.0")
        self.assertEqual("4.0.0", result["desktopVersion"])
        self.assertEqual("3.1.0", result["androidVersion"])

    def test_future_platform_requires_explicit_implementation(self):
        for scope in ("macos", "ios", "windows", "", "ALL"):
            with self.subTest(scope=scope), self.assertRaises(ValueError):
                self.make(scope=scope)

    def test_future_activation_joins_only_its_reviewed_group(self):
        with patch.dict(plan.PLATFORMS['macos'], production=True):
            self.assertIn('macos', self.make(scope='desktop')['platforms'])
            self.assertNotIn('macos', self.make(scope='mobile')['platforms'])

    def test_invalid_or_conflicting_policy(self):
        for changes in (
            {"source": "master"}, {"ref": "refs/heads/untrusted"}, {"mode": "automatic"},
            {"mode": "validate", "store_action": "submit"}, {"scope": "mobile", "store_action": "submit"},
            {"scope": "desktop", "android": "4.0.0"}, {"scope": "mobile", "desktop": "4.0.0"},
            {"desktop": "3.10.0"}, {"desktop": "2.0.0"}, {"android": "3.0.6"},
            {"desktop": "v4.0.0"}, {"android": "4.0.0-rc1"}, {"desktop": "04.0.0"},
            {"run_id": "1\nmalicious"}, {"store_action": "delete"}, {"mode": "bypass"},
        ):
            with self.subTest(changes=changes), self.assertRaises(ValueError):
                self.make(**changes)

    def test_validate_is_not_promotable_and_automatic_needs_policy(self):
        self.assertFalse(self.make(mode="validate")["productionEligible"])
        self.assertTrue(self.make(mode="automatic", automatic_enabled=True)["productionEligible"])
        self.assertFalse(self.make(mode='validate', ref='refs/heads/codex/review')['productionEligible'])

    def test_strict_tags_preserve_historical_namespaces(self):
        for tag in ("v3.0.0-rc1", "v1.0.0.0", "v03.0.0", "android-v3.0.0\n", "v３.0.0", "v3.0.0;echo"):
            with self.subTest(tag=tag), self.assertRaises(ValueError):
                plan.stable_tag(tag)

    def test_pending_tags_drafts_and_candidate_reservations(self):
        occupied = plan.occupied_versions(
            [{"ref": "refs/tags/v3.0.5"}], [{"tag_name": "android-v3.0.6", "draft": True}],
            [{"name": "release-plan-v3.0.6-android-v3.0.7-123-1"},
             {"name": "validation-plan-v9.0.0-none-124-1"}])
        self.assertEqual("3.0.7", plan.resolve_version("auto", "desktop", occupied))
        self.assertEqual("3.0.8", plan.resolve_version("auto", "android", occupied))

    def test_gate_rejects_required_skip_failure_cancellation_missing_or_extra(self):
        for state in ("failure", "skipped", "cancelled", "", "unknown"):
            with self.subTest(state=state), self.assertRaises(ValueError):
                plan.check_gate({"shared": state, "android": "skipped"}, ["shared"], ["android"])
        for results in ({}, {"shared": "success", "extra": "success"}, {"shared": "success", "android": "failure"}):
            with self.subTest(results=results), self.assertRaises(ValueError):
                plan.check_gate(results, ["shared"], ["android"])
        plan.check_gate({"shared": "success", "android": "skipped"}, ["shared"], ["android"])


if __name__ == "__main__":
    unittest.main()

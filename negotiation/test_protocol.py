from __future__ import annotations

import unittest

from negmas.sao import SAONegotiator

from Server import shared_state
from negotiation import (
    NegmasAdjustmentAgent,
    NegmasPlayerAgent,
    run_negotiation,
)
from negotiation.protocol import (
    AdjustmentAgent,
    Critique,
    PlayerAgent,
    concession_threshold,
)


class NegotiationProtocolTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        shared_state.load_user_profiles("Server/data/user_profile.csv")

    def setUp(self) -> None:
        shared_state.user_status["01"].issue_settings = dict(
            shared_state.DEFAULT_ISSUE_SETTINGS
        )

    def test_boulware_threshold_reaches_minimum(self) -> None:
        self.assertEqual(concession_threshold(0, 20, 0.9, 0.5, 0.4), 0.9)
        self.assertAlmostEqual(concession_threshold(19, 20, 0.9, 0.5, 0.4), 0.5)

    def test_load_prediction_uses_csv_coefficients(self) -> None:
        current = dict(shared_state.DEFAULT_ISSUE_SETTINGS)
        agent = AdjustmentAgent(
            current_load=0.75,
            current_settings=current,
            issue_options=shared_state.ISSUE_OPTIONS,
            coeffs=shared_state.DEFAULT_COEFFS,
            rho=shared_state.DEFAULT_RHO,
            load_low=0.3,
            load_high=0.7,
        )
        offer = dict(current)
        offer["break_policy"] = 1.0
        self.assertAlmostEqual(agent.predict_load(offer), 0.65)

    def test_critique_direction_matches_preference(self) -> None:
        pa = PlayerAgent(
            preference={"tempo": 1.0, "guidance": 0.0},
            weights={"tempo": 0.5, "guidance": 0.5},
            load_low=0.3,
            load_high=0.7,
        )
        critiques = pa.critiques({"tempo": 0.0, "guidance": 1.0})
        self.assertIn(Critique("tempo", "increase", 1.0), critiques)
        self.assertIn(Critique("guidance", "decrease", 1.0), critiques)

    def test_agreement_updates_shared_state(self) -> None:
        result = run_negotiation("01", 0.75, max_steps=30, random_seed=7)
        self.assertTrue(result.accepted)
        self.assertEqual(result.engine, "negmas")
        self.assertEqual(
            shared_state.get_user_issue_settings("01"),
            result.final_settings,
        )
        self.assertGreaterEqual(result.predicted_load, 0.3)
        self.assertLessEqual(result.predicted_load, 0.7)

    def test_negmas_agents_extend_sao_negotiator(self) -> None:
        self.assertTrue(issubclass(NegmasAdjustmentAgent, SAONegotiator))
        self.assertTrue(issubclass(NegmasPlayerAgent, SAONegotiator))

    def test_only_aa_proposes_in_negmas_session(self) -> None:
        result = run_negotiation("01", 0.75, max_steps=30, random_seed=7)
        self.assertGreater(len(result.steps), 0)
        self.assertTrue(all(step.offer for step in result.steps))

    def test_dry_run_does_not_update_shared_state(self) -> None:
        settings_before = shared_state.get_user_issue_settings("01")
        result = run_negotiation(
            "01",
            0.75,
            max_steps=30,
            random_seed=7,
            persist_agreement=False,
        )
        self.assertTrue(result.accepted)
        self.assertNotEqual(result.final_settings, settings_before)
        self.assertEqual(
            shared_state.get_user_issue_settings("01"),
            settings_before,
        )


if __name__ == "__main__":
    unittest.main()

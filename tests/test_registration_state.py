import unittest
import time
import ast
from contextlib import nullcontext
from pathlib import Path
from unittest.mock import Mock, patch

from sms_tool.registration_state import (
    RegistrationState,
    RegistrationStage,
    RegistrationStageOverrun,
    RegistrationStateMachine,
    prepare_registration_context,
)
from sms_tool.registration_handlers import (
    BoundRegistrationStage,
    RegistrationAbort,
    RegistrationEmailWorkflow,
    RegistrationStageRunner,
)


class Mailbox:
    email = "user@example.com"


class RegistrationStateTests(unittest.TestCase):
    def test_email_registration_defaults_to_at_only_and_omits_codex_oauth_stage(self):
        operations = Mock()
        operations._tl.return_value = []
        operations.runtime_config_scope.return_value = nullcontext()
        workflow = RegistrationEmailWorkflow(
            RegistrationStateMachine(lambda *_: None),
            config={"registration": {}},
            operations=operations,
        )
        workflow._bootstrap = Mock()
        workflow._resume_post_create = Mock(return_value=None)
        workflow._run_stage = Mock(return_value={"success": True})
        workflow._set_outcome = Mock()
        workflow._close_sessions = Mock()

        workflow.run()

        self.assertFalse(workflow.codex_oauth)
        states = [call.args[0] for call in workflow._run_stage.call_args_list]
        self.assertNotIn(RegistrationState.CODEX_OAUTH, states)
        self.assertIn(RegistrationState.ACCESS_TOKEN_PROBE, states)
        self.assertNotIn(RegistrationState.DETECT_OFFER, states)
        self.assertIn(RegistrationState.DETECT_PAYMENT_METHODS, states)

    def test_email_registration_ignores_legacy_codex_oauth_true_flag(self):
        operations = Mock()
        operations._tl.return_value = []
        operations.runtime_config_scope.return_value = nullcontext()
        operations.select_auth_fingerprint.return_value = None
        machine = RegistrationStateMachine(lambda *_: None)
        workflow = RegistrationEmailWorkflow(
            machine,
            codex_oauth=True,
            operations=operations,
        )
        workflow._bootstrap = Mock()
        workflow._resume_post_create = Mock(return_value=None)
        workflow._run_stage = Mock(return_value={"ok": True})
        workflow._set_outcome = Mock()

        workflow.run()

        self.assertFalse(workflow.codex_oauth)
        states = [call.args[0] for call in workflow._run_stage.call_args_list]
        self.assertNotIn(RegistrationState.CODEX_OAUTH, states)

    def test_stage_runner_shares_state_and_runs_cleanup(self):
        events = []
        machine = RegistrationStateMachine(lambda *event: events.append(event))
        context = object()
        cleaned = []
        runner = RegistrationStageRunner(context, machine, cleanup=lambda state: cleaned.append(dict(state)))
        first = BoundRegistrationStage(
            RegistrationStage(RegistrationState.AUTH_FLOW, lambda _ctx: {"auth": "ok"}),
            lambda *_: {"auth": "ok"},
        )
        second = BoundRegistrationStage(
            RegistrationStage(RegistrationState.ACCESS_TOKEN_PROBE, lambda _ctx: {"probe": 200}),
            lambda *_: {"probe": 200},
        )
        # Stage handlers are represented by the stage callback in this minimal contract.
        result = runner.run([first, second])
        self.assertEqual(result, {"auth": "ok", "probe": 200})
        self.assertEqual(cleaned, [{"auth": "ok", "probe": 200}])

    def test_stage_runner_single_stage_is_the_production_execution_seam(self):
        machine = RegistrationStateMachine(lambda *_: None)
        runner = RegistrationStageRunner(object(), machine)
        self.assertEqual(
            runner.run_stage(RegistrationState.AUTH_FLOW, lambda: "ok", timeout_seconds=1),
            "ok",
        )

    def test_handlers_do_not_reverse_import_registration_facade(self):
        path = Path(__file__).resolve().parents[1] / "sms_tool" / "registration_handlers.py"
        tree = ast.parse(path.read_text(encoding="utf-8-sig"))
        imported_modules = [
            node.module
            for node in ast.walk(tree)
            if isinstance(node, ast.ImportFrom) and node.module
        ]
        self.assertNotIn("registration", imported_modules)
    def test_stage_handler_applies_common_timeout_and_failure_transition(self):
        events = []
        machine = RegistrationStateMachine(lambda state, status, detail: events.append((state, status, detail)))
        stage = RegistrationStage(RegistrationState.AUTH_FLOW, lambda _context: time.sleep(0.05), timeout_seconds=0.001)
        with self.assertRaises(RegistrationStageOverrun):
            stage.run(object(), machine)
        self.assertEqual(machine.snapshot()["state"], "failed")
        # A budget overrun must be distinguishable from a transport timeout.
        self.assertTrue(issubclass(RegistrationStageOverrun, TimeoutError))
        failed = [event for event in events if event[1] == "failed"][-1]
        self.assertIn("stage_budget_exceeded", failed[2])

    def test_otp_poll_timeout_is_clamped_to_stage_budget(self):
        workflow = RegistrationEmailWorkflow(
            RegistrationStateMachine(lambda *_: None),
            config={"registration": {"stage_timeouts": {"email_otp_wait": 45}}},
            operations=object(),
        )
        workflow.runtime.email_cfg = {"otp_timeout": 300}
        # The blocking poll receives the smaller of its own timeout and the
        # stage budget so the configured limit is enforced while it runs.
        self.assertEqual(workflow._otp_poll_timeout(), 45)

    def test_otp_poll_timeout_falls_back_to_config_without_budget(self):
        workflow = RegistrationEmailWorkflow(
            RegistrationStateMachine(lambda *_: None),
            config={"registration": {}},
            operations=object(),
        )
        workflow.runtime.email_cfg = {"otp_timeout": 210}
        self.assertEqual(workflow._otp_poll_timeout(), 210)

    def test_password_mode_skips_legacy_register_when_auth_already_reached_email_verification(self):
        operations = Mock()
        operations._is_signup_password_step.return_value = False
        operations._is_email_verification_step.side_effect = (
            lambda value: value == "https://auth.openai.com/email-verification"
        )
        workflow = RegistrationEmailWorkflow(
            RegistrationStateMachine(lambda *_: None),
            config={"registration": {}},
            operations=operations,
        )
        workflow._issue_sentinel = Mock(return_value=Mock(token="sentinel-token"))
        workflow.runtime.registration_mode = "password"
        workflow.runtime.signup_state = {
            "url": "https://auth.openai.com/email-verification",
        }
        workflow.runtime.auth_base = "https://auth.openai.com"
        workflow.runtime.base_headers = {}
        workflow.runtime.device_id = "device-id"
        workflow.runtime.username = "user@example.com"
        workflow.runtime.password = "Generated!123"
        workflow.runtime.session = object()

        workflow.user_register()

        self.assertTrue(workflow.runtime.resume_email_verification)
        self.assertTrue(workflow.runtime.otp_pre_sent)
        self.assertTrue(workflow.runtime.password_unknown)
        self.assertEqual(workflow.runtime.reg_data["mode"], "email_verification_signup")
        operations.request_with_retry.assert_not_called()
        workflow._issue_sentinel.assert_not_called()

    def test_account_creation_failed_still_aborts_outside_email_verification_state(self):
        operations = Mock()
        operations._is_signup_password_step.return_value = True
        operations._is_email_verification_step.return_value = False
        operations._auth_request_headers.return_value = {}
        operations.auth_impersonate.return_value = "firefox144"
        operations._sanitize_text.side_effect = lambda value: str(value)
        response = Mock(status_code=400)
        response.json.return_value = {
            "error": {
                "code": "account_creation_failed",
                "message": "Failed to create account. Please try again.",
            }
        }
        operations.request_with_retry.return_value = response
        workflow = RegistrationEmailWorkflow(
            RegistrationStateMachine(lambda *_: None),
            config={"registration": {}},
            operations=operations,
        )
        workflow._issue_sentinel = Mock(return_value=Mock(token="sentinel-token"))
        workflow.runtime.registration_mode = "password"
        workflow.runtime.signup_state = {
            "url": "https://auth.openai.com/create-account/password",
        }
        workflow.runtime.auth_base = "https://auth.openai.com"
        workflow.runtime.base_headers = {}
        workflow.runtime.device_id = "device-id"
        workflow.runtime.username = "user@example.com"
        workflow.runtime.password = "Generated!123"
        workflow.runtime.session = object()

        with self.assertRaisesRegex(RegistrationAbort, "user_register:Failed to create account"):
            workflow.user_register()

    def test_pre_sent_registration_otp_is_polled_before_resend(self):
        operations = Mock()
        operations._is_signup_password_step.return_value = False
        operations.SyntheticResponse.side_effect = (
            lambda status, body, url="": Mock(
                status_code=status,
                url=url,
                json=Mock(return_value=body),
            )
        )
        operations._json_or_raw.side_effect = lambda response: response.json()
        workflow = RegistrationEmailWorkflow(
            RegistrationStateMachine(lambda *_: None),
            config={"registration": {}},
            operations=operations,
        )
        workflow.runtime.registration_mode = "password"
        workflow.runtime.signup_state = {
            "url": "https://auth.openai.com/email-verification",
        }
        workflow.runtime.auth_base = "https://auth.openai.com"
        workflow.runtime.base_headers = {}
        workflow.runtime.session = object()
        workflow.runtime.mailbox = object()
        workflow.runtime.proxy = ""
        workflow.runtime.auth_flow_started = 1_000
        workflow.runtime.otp_pre_sent = True

        workflow.send_email_otp()

        operations._follow_continue_url.assert_not_called()
        self.assertEqual(workflow.runtime.otp_issued_after, 995)

    def test_payment_detection_stage_reuses_live_registration_session(self):
        operations = Mock()
        operations.auth_impersonate.return_value = "firefox144"
        operations._sanitize_text.side_effect = lambda value: str(value)
        workflow = RegistrationEmailWorkflow(
            RegistrationStateMachine(lambda *_: None),
            config={"registration": {"payment_country": "VN"}},
            operations=operations,
        )
        live_session = object()
        workflow.runtime.success = True
        workflow.runtime.access_token = "access-token"
        workflow.runtime.session = live_session
        workflow.runtime.proxy = "http://user-region-US:pass@proxy.example:8080"
        workflow.runtime.device_id = "device-id"
        workflow.runtime.base_headers = {"User-Agent": "registration-agent"}
        workflow.runtime.auth_session = {"cookie_header": "oai-did=device-id"}
        capability = {
            "ok": True,
            "payment_method_badges": ["Trial · 0 đ", "Card"],
            "payment_method_types": ["card"],
        }

        workflow._issue_sentinel = Mock(return_value=Mock(token="checkout-sentinel"))
        with patch(
            "sms_tool.payment_capability.detect_account_payment_methods",
            return_value=capability,
        ) as detect:
            workflow.detect_payment_methods()

        self.assertEqual(workflow.runtime.payment_badges, ["Trial · 0 đ", "Card"])
        self.assertIs(detect.call_args.kwargs["registration_session"], live_session)
        self.assertEqual(detect.call_args.kwargs["billing_country"], "VN")
        self.assertEqual(detect.call_args.kwargs["auth_context"]["impersonate"], "firefox144")
        self.assertEqual(detect.call_args.kwargs["auth_context"]["sentinel_token"], "checkout-sentinel")
        workflow._issue_sentinel.assert_called_once_with("chatgpt_checkout")

    def test_offer_detection_reuses_live_session_and_supplies_real_campaign_to_checkout(self):
        operations = Mock()
        operations.auth_impersonate.return_value = "firefox144"
        operations._sanitize_text.side_effect = lambda value: str(value)
        workflow = RegistrationEmailWorkflow(
            RegistrationStateMachine(lambda *_: None),
            config={"registration": {"payment_country": "VN"}},
            operations=operations,
        )
        live_session = object()
        workflow.runtime.success = True
        workflow.runtime.username = "new@example.com"
        workflow.runtime.access_token = "access-token"
        workflow.runtime.session = live_session
        workflow.runtime.proxy = "http://signup.example:8080"
        workflow.runtime.device_id = "device-id"
        workflow.runtime.base_headers = {"User-Agent": "registration-agent"}
        workflow.runtime.auth_session = {"cookie_header": "oai-did=device-id"}

        offer = {
            "ok": True,
            "promotion_status": "Có thể dùng thử Plus·-100%·x1 tháng",
            "plus_trial_eligible": True,
            "plus_trial_campaign_id": "account-real-campaign",
            "current_plan_type": "free",
        }
        with patch("sms_tool.account_promotion.check_account_promotion", return_value=offer) as check:
            workflow.detect_offer()

        self.assertEqual(workflow.runtime.promotion_status, offer["promotion_status"])
        self.assertIs(check.call_args.kwargs["request_session"], live_session)
        self.assertEqual(check.call_args.args[0]["access_token"], "access-token")

        workflow._issue_sentinel = Mock(return_value=Mock(token="checkout-sentinel"))
        with patch(
            "sms_tool.payment_capability.detect_account_payment_methods",
            return_value={"ok": True, "payment_method_badges": ["Trial · 0 đ"]},
        ) as detect:
            workflow.detect_payment_methods()

        self.assertEqual(detect.call_args.kwargs["promo_campaign_id"], "account-real-campaign")

    def test_get_session_payment_detection_uses_default_promo_without_legacy_offer_probe(self):
        operations = Mock()
        operations.auth_impersonate.return_value = "firefox144"
        operations._sanitize_text.side_effect = lambda value: str(value)
        workflow = RegistrationEmailWorkflow(
            RegistrationStateMachine(lambda *_: None),
            config={"registration": {"payment_country": "VN"}},
            operations=operations,
        )
        workflow.runtime.success = True
        workflow.runtime.access_token = "access-token"
        workflow.runtime.session = object()
        workflow.runtime.promotion_result = {
            "ok": True,
            "plus_trial_eligible": False,
        }

        workflow._issue_sentinel = Mock(return_value=Mock(token="checkout-sentinel"))
        with patch(
            "sms_tool.payment_capability.detect_account_payment_methods",
            return_value={"ok": True, "payment_method_badges": ["Card"]},
        ) as detect:
            workflow.detect_payment_methods()

        self.assertEqual(detect.call_args.kwargs["promo_campaign_id"], "plus-1-month-free")

    def test_configured_payment_country_is_persisted_as_registration_country_without_proxy(self):
        workflow = RegistrationEmailWorkflow(
            RegistrationStateMachine(lambda *_: None),
            config={"registration": {"payment_country": "vn"}},
            operations=object(),
        )

        self.assertEqual(workflow._registration_country(), "VN")

    def test_state_machine_allows_forward_skips_and_rejects_backtracking(self):
        events = []
        machine = RegistrationStateMachine(lambda state, status, detail: events.append((state, status, detail)))
        machine.transition(RegistrationState.MAILBOX_READY)
        machine.transition(RegistrationState.AUTH_FLOW)

        with self.assertRaises(ValueError):
            machine.transition(RegistrationState.SENTINEL)

        self.assertEqual(machine.snapshot()["state"], "auth_flow")
        self.assertEqual([event[0] for event in events], ["mailbox_ready", "auth_flow"])

    def test_context_reuses_device_and_stored_password_without_exposing_them_in_repr(self):
        context = prepare_registration_context(
            proxy="http://proxy.example:8080",
            mailbox=Mailbox(),
            sentinel_data={"sentinel_token": "secret-sentinel"},
            password=None,
            registration_mode="password",
            auth_base="https://auth.example",
            chat_base="https://chat.example",
            stored_password=lambda _email: "StoredPassword!1",
            generate_password=lambda: "GeneratedPassword!1",
            random_name=lambda: ("Ada", "Lovelace"),
            random_birthdate=lambda: "1990-01-01",
            normalize_mode=lambda value: str(value),
            get_device_context=lambda _email: {
                "device_id": "existing-device",
                "auth_session_logging_id": "existing-log",
            },
            sentinel_device_id=lambda _data: "sentinel-device",
            new_uuid=lambda: "new-uuid",
        )

        self.assertEqual(context.password, "StoredPassword!1")
        self.assertEqual(context.device_id, "existing-device")
        self.assertNotIn("StoredPassword!1", repr(context))
        self.assertNotIn("secret-sentinel", repr(context))


if __name__ == "__main__":
    unittest.main()

import unittest
from types import SimpleNamespace

from sms_tool.checkout_contract import CHECKOUT_URL, STRIPE_INIT_URL, CheckoutSessionContract
from sms_tool.payment_capability import CapabilityProbeError, payment_method_capability_probe


class FakeCapabilityTransport:
    def __init__(self, init_payload):
        self.init_payload = init_payload
        self.checkout_calls = []
        self.init_calls = []

    def create_checkout(self, contract, **kwargs):
        self.checkout_calls.append((contract, kwargs))
        return CheckoutSessionContract("cs_fixture", "openai_ie", "pk_live_fixture")

    def stripe_init(self, contract, checkout, **kwargs):
        self.init_calls.append((contract, checkout, kwargs))
        return self.init_payload


class PaymentCapabilityProbeTests(unittest.TestCase):
    def test_probe_stops_after_stripe_init_and_marks_zero_due_method_eligible(self):
        transport = FakeCapabilityTransport({
            "total_summary": {"due": 0},
            "currency": "idr",
            "payment_method_types": ["card", "gopay"],
        })

        result = payment_method_capability_probe(
            "access-token",
            "gopay",
            transport=transport,
            checkout_proxy="http://checkout.test:80",
            stripe_init_proxy="http://stripe.test:80",
        )

        self.assertTrue(result["ok"])
        self.assertEqual(result["classification"], "eligible")
        self.assertTrue(result["eligible"])
        self.assertEqual(result["offer_state"], "zero_due")
        self.assertEqual(len(transport.checkout_calls), 1)
        self.assertEqual(len(transport.init_calls), 1)
        self.assertEqual(transport.checkout_calls[0][1]["proxy"], "http://checkout.test:80")
        self.assertEqual(transport.init_calls[0][2]["proxy"], "http://stripe.test:80")

    def test_nonzero_offer_is_conclusive_ineligible(self):
        transport = FakeCapabilityTransport({
            "invoice": {"amount_due": 290000},
            "currency": "idr",
            "payment_method_types": ["gopay"],
        })
        result = payment_method_capability_probe("access-token", "gopay", transport=transport)
        self.assertTrue(result["ok"])
        self.assertEqual(result["classification"], "ineligible")
        self.assertEqual(result["decision"], "nonzero_offer")
        self.assertFalse(result["eligible"])
        self.assertFalse(result["retryable"])

    def test_missing_method_is_conclusive_ineligible(self):
        transport = FakeCapabilityTransport({
            "total_summary": {"due": 0},
            "payment_method_types": ["card"],
        })
        result = payment_method_capability_probe("access-token", "gcash", transport=transport)
        self.assertTrue(result["ok"])
        self.assertEqual(result["classification"], "ineligible")
        self.assertEqual(result["decision"], "payment_method_unavailable")

    def test_transport_failure_is_unknown_and_retryable(self):
        class FailedTransport(FakeCapabilityTransport):
            def create_checkout(self, contract, **kwargs):
                raise CapabilityProbeError(
                    "checkout timed out",
                    error_code="checkout_transport_failed",
                    error_stage="checkout_create",
                    retryable=True,
                    status="unknown",
                )

        result = payment_method_capability_probe("access-token", "gopay", transport=FailedTransport({}))
        self.assertFalse(result["ok"])
        self.assertEqual(result["status"], "unknown")
        self.assertEqual(result["classification"], "unknown")
        self.assertTrue(result["retryable"])
        self.assertEqual(result["error_stage"], "checkout_create")

    def test_detect_account_payment_methods_extracts_badges(self):
        from sms_tool.payment_capability import detect_account_payment_methods, format_payment_method_badges

        transport = FakeCapabilityTransport({
            "total_summary": {"due": 0},
            "currency": "vnd",
            "payment_method_types": ["card", "link", "apple_pay", "google_pay", "momo"],
            "custom_payment_methods": [{"type": "gcash"}],
        })

        result = detect_account_payment_methods(
            "access-token",
            auth_context={"registration_country": "VN"},
            transport=transport,
        )

        self.assertTrue(result["ok"])
        self.assertEqual(result["status"], "completed")
        self.assertGreater(result["checked_at"], 0)
        self.assertTrue(result["is_zero_due"])
        self.assertEqual(result["amount_due"], 0)
        self.assertEqual(result["offer_state"], "zero_due")
        self.assertEqual(result["payment_method_badges"], result["badges"])
        self.assertIn("Trial · 0 đ", result["badges"])
        self.assertIn("Card", result["badges"])
        self.assertIn("Link", result["badges"])
        self.assertIn("Apple Pay", result["badges"])
        self.assertIn("Google Pay", result["badges"])
        self.assertIn("MoMo", result["badges"])
        self.assertIn("GCash", result["badges"])
        self.assertEqual(result["custom_payment_methods"], ["gcash"])

    def test_detect_account_payment_methods_returns_structured_failure(self):
        class FailedTransport(FakeCapabilityTransport):
            def create_checkout(self, contract, **kwargs):
                raise CapabilityProbeError(
                    "checkout blocked",
                    error_code="checkout_unauthorized",
                    error_stage="checkout_create",
                    retryable=False,
                )

        result = __import__(
            "sms_tool.payment_capability", fromlist=["detect_account_payment_methods"]
        ).detect_account_payment_methods(
            "access-token",
            auth_context={"registration_country": "US"},
            transport=FailedTransport({}),
        )

        self.assertFalse(result["ok"])
        self.assertEqual(result["status"], "failed")
        self.assertGreater(result["checked_at"], 0)
        self.assertEqual(result["error_code"], "checkout_unauthorized")
        self.assertEqual(result["error_stage"], "checkout_create")
        self.assertFalse(result["retryable"])
        self.assertEqual(result["payment_method_badges"], [])

    def test_detect_requires_real_registration_country_instead_of_defaulting_to_vn(self):
        from sms_tool.payment_capability import detect_account_payment_methods

        transport = FakeCapabilityTransport({})
        result = detect_account_payment_methods("access-token", transport=transport)

        self.assertFalse(result["ok"])
        self.assertEqual(result["error_code"], "payment_country_unknown")
        self.assertEqual(transport.checkout_calls, [])

    def test_registration_transport_reuses_one_live_session_for_checkout_and_stripe(self):
        from sms_tool.payment_capability import detect_account_payment_methods

        class LiveRegistrationSession:
            def __init__(self):
                self.calls = []

            def post(self, url, **kwargs):
                self.calls.append((url, kwargs))
                if url == CHECKOUT_URL:
                    return SimpleNamespace(
                        status_code=200,
                        json=lambda: {
                            "checkout_session_id": "cs_live_registration",
                            "processor_entity": "openai_llc",
                            "publishable_key": "pk_live_fixture",
                        },
                    )
                assert url == STRIPE_INIT_URL.format(checkout_session_id="cs_live_registration")
                return SimpleNamespace(
                    status_code=200,
                    json=lambda: {
                        "total_summary": {"due": 0},
                        "currency": "vnd",
                        "payment_method_types": ["card", "momo"],
                    },
                )

        live_session = LiveRegistrationSession()
        result = detect_account_payment_methods(
            "access-token",
            auth_context={
                "registration_country": "VN",
                "cookie_header": "oai-did=device-fixture",
                "headers": {"User-Agent": "registration-agent", "oai-device-id": "device-fixture"},
                "impersonate": "firefox144",
                "sentinel_token": "sentinel-fixture",
            },
            registration_session=live_session,
        )

        self.assertTrue(result["ok"])
        self.assertEqual(len(live_session.calls), 2)
        checkout_url, checkout_kwargs = live_session.calls[0]
        stripe_url, stripe_kwargs = live_session.calls[1]
        self.assertEqual(checkout_url, CHECKOUT_URL)
        self.assertEqual(stripe_url, STRIPE_INIT_URL.format(checkout_session_id="cs_live_registration"))
        self.assertEqual(checkout_kwargs["impersonate"], "firefox144")
        self.assertEqual(stripe_kwargs["impersonate"], "firefox144")
        self.assertEqual(checkout_kwargs["headers"]["User-Agent"], "registration-agent")
        self.assertEqual(checkout_kwargs["headers"]["Cookie"], "oai-did=device-fixture")
        self.assertEqual(checkout_kwargs["headers"]["openai-sentinel-token"], "sentinel-fixture")
        self.assertEqual(checkout_kwargs["headers"]["Origin"], "https://chatgpt.com")
        self.assertTrue(checkout_kwargs["json"]["prefetch"])
        self.assertEqual(checkout_kwargs["json"]["cancel_url"], "https://chatgpt.com/#pricing")
        self.assertTrue(
            checkout_kwargs["json"]["promo_campaign"]["is_coupon_from_query_param"]
        )
        self.assertNotIn("proxies", checkout_kwargs)
        self.assertNotIn("proxies", stripe_kwargs)

    def test_oaics_checkout_uses_get_session_fallback_without_stripe_payment_page(self):
        from sms_tool.payment_capability import detect_account_payment_methods

        class LiveRegistrationSession:
            def __init__(self):
                self.calls = []

            def post(self, url, **kwargs):
                self.calls.append((url, kwargs))
                if url == CHECKOUT_URL:
                    return SimpleNamespace(
                        status_code=200,
                        json=lambda: {
                            "checkout_session_id": "oaics_fixture",
                            "publishable_key": "pk_live_fixture",
                            "payment_method_types": ["card", "link", "momo"],
                            "custom_payment_methods": [{"type": "gcash"}],
                            "billing_details": {"currency": "VND"},
                            "checkout_state": {
                                "currency": "VND",
                                "total": {"total": {"minorUnitsAmount": 0}},
                            },
                        },
                    )
                return SimpleNamespace(
                    status_code=400,
                    json=lambda: {
                        "error": {
                            "code": "resource_missing",
                            "message": "No such payment_page",
                        }
                    },
                )

        result = detect_account_payment_methods(
            "access-token",
            auth_context={"registration_country": "VN"},
            registration_session=LiveRegistrationSession(),
        )

        self.assertTrue(result["ok"])
        self.assertEqual(result["amount_due"], 0)
        self.assertIn("Trial · 0 đ", result["payment_method_badges"])
        self.assertIn("Apple Pay", result["payment_method_badges"])
        self.assertIn("Google Pay", result["payment_method_badges"])
        self.assertIn("MoMo", result["payment_method_badges"])
        self.assertIn("GCash", result["payment_method_badges"])

    def test_format_payment_method_badges_formats_currencies(self):
        from sms_tool.payment_capability import format_payment_method_badges

        badges_vn = format_payment_method_badges(["card", "momo"], amount_minor=0, currency="VND")
        self.assertEqual(badges_vn, ["Trial · 0 đ", "Card", "MoMo"])

        badges_us = format_payment_method_badges(["card", "apple_pay"], amount_minor=0, currency="USD")
        self.assertEqual(badges_us, ["Trial · 0$", "Card", "Apple Pay"])


if __name__ == "__main__":
    unittest.main()

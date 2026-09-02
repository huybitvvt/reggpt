import json

from sms_tool.account_models import AccountSessionModel
from sms_tool.account_identity import create_registration_identity
from sms_tool import storage


def test_account_session_model_hides_credentials_and_emits_safe_snapshot():
    model = AccountSessionModel.from_value({
        "email": "typed@example.com",
        "success": True,
        "source": "register",
        "register_method": "email",
        "session_type": "oauth",
        "plan_type": "plus",
        "access_token": "eyJsecret.payload.signature",
        "oauth_refresh_token": "rt_secret",
        "totp_secret": "TOTPSECRET",
        "mailbox": {"provider": "remail", "token": "mail-secret"},
        "paypal": {"ok": True, "url": "https://pay.example/?ba_token=BA-secret", "card_last4": "4242"},
    })

    assert "rt_secret" not in repr(model)
    assert not hasattr(model, "payload")
    assert not hasattr(model, "payload")
    snapshot = json.dumps(model.safe_snapshot())
    assert "rt_secret" not in snapshot
    assert "BA-secret" not in snapshot
    assert "4242" not in snapshot
    assert model.source == "register"
    assert model.register_method == "email"
    assert model.session_type == "oauth"
    assert model.plan_type == "plus"


def test_storage_accepts_typed_model_and_raw_json_is_token_free(tmp_path, monkeypatch):
    database = tmp_path / "accounts.sqlite3"
    monkeypatch.setattr(storage, "database_path", lambda cfg=None: database)
    model = AccountSessionModel.from_value({
        "email": "typed-storage@example.com",
        "success": True,
        "source": "import",
        "register_method": "apple",
        "session_type": "at_only",
        "plan_type": "free",
        "access_token": "at-secret-value",
        "oauth_refresh_token": "rt_secret_value",
    })

    assert storage.upsert_account(model)
    with storage._connect() as connection:
        row = connection.execute(
            "SELECT access_token, oauth_refresh_token, source, register_method, session_type, plan_type, raw_json FROM accounts WHERE email=?",
            ("typed-storage@example.com",),
        ).fetchone()
    assert row["access_token"] == "at-secret-value"
    assert row["oauth_refresh_token"] == "rt_secret_value"
    assert row["source"] == "import"
    assert row["register_method"] == "apple"
    assert row["session_type"] == "at_only"
    assert row["plan_type"] == "free"
    assert "at-secret-value" not in row["raw_json"]
    assert "rt_secret_value" not in row["raw_json"]


def test_storage_persists_safe_account_identity_context(tmp_path, monkeypatch):
    database = tmp_path / "accounts.sqlite3"
    monkeypatch.setattr(storage, "database_path", lambda cfg=None: database)
    base_proxy = "http://user-region-US-sid-OLD1234-t-5:proxy-secret@proxy.example:443"
    registration_proxy = "http://user-region-US-sid-NEW5678-t-5:proxy-secret@proxy.example:443"
    identity = create_registration_identity(
        registration_proxy,
        pool_index=0,
        fingerprint_key="chrome146",
        device_id="device-123",
    )
    account = {
        "email": "identity-storage@example.com",
        "success": True,
        "access_token": "at-secret-value",
        "identity_context": identity,
    }

    assert storage.upsert_account(account)
    record = storage.get_account_record("identity-storage@example.com")
    payload = json.loads(record["raw_json"])

    assert payload["identity_context"] == identity
    assert payload["identity_context"]["proxy_affinity"]["session_id"] == "NEW5678"
    assert "proxy-secret" not in record["raw_json"]
    assert base_proxy not in record["raw_json"]


def test_payment_detection_fields_survive_safe_snapshot_and_storage(tmp_path, monkeypatch):
    database = tmp_path / "accounts.sqlite3"
    monkeypatch.setattr(storage, "database_path", lambda cfg=None: database)
    payment_capability = {
        "ok": True,
        "status": "completed",
        "checked_at": 1788240000,
        "payment_method_types": ["card", "momo"],
        "custom_payment_methods": ["gcash"],
        "amount_due": 0,
        "currency": "VND",
        "offer_state": "zero_due",
        "badges": ["Trial · 0 đ", "Card", "MoMo", "GCash"],
    }
    account = {
        "email": "payment-badges@example.com",
        "success": True,
        "access_token": "at-secret-value",
        "payment_capability": payment_capability,
        "payment_method_badges": payment_capability["badges"],
        "payment_method_types": payment_capability["payment_method_types"],
        "custom_payment_methods": payment_capability["custom_payment_methods"],
        "amount_due": 0,
        "currency": "VND",
        "offer_state": "zero_due",
    }

    assert storage.upsert_account(account)
    record = storage.get_account_record(account["email"])
    payload = json.loads(record["raw_json"])

    assert payload["payment_method_badges"] == payment_capability["badges"]
    assert payload["payment_method_types"] == ["card", "momo"]
    assert payload["custom_payment_methods"] == ["gcash"]
    assert payload["amount_due"] == 0
    assert payload["currency"] == "VND"
    assert payload["offer_state"] == "zero_due"
    assert payload["payment_check_status"] == "completed"
    assert payload["payment_check_error"] == ""
    assert payload["payment_checked_at"] == 1788240000
    assert payload["payment_capability"]["ok"] is True
    assert "at-secret-value" not in record["raw_json"]


def test_inline_offer_fields_survive_safe_snapshot_and_storage(tmp_path, monkeypatch):
    database = tmp_path / "accounts.sqlite3"
    monkeypatch.setattr(storage, "database_path", lambda cfg=None: database)
    probe = {
        "ok": True,
        "promotion_status": "Có thể dùng thử Plus·-100%·x1 tháng",
        "plus_trial_eligible": True,
        "plus_trial_campaign_id": "real-campaign",
        "current_plan_type": "free",
    }
    account = {
        "email": "inline-offer@example.com",
        "success": True,
        "access_token": "at-secret-value",
        "plan_type": "free",
        "promotion_status": probe["promotion_status"],
        "promotion_result": probe,
        "promotion": {
            "status": probe["promotion_status"],
            "updated_at": 123,
            "last_result": probe,
        },
    }

    assert storage.upsert_account(account)
    record = storage.get_account_record(account["email"])
    payload = json.loads(record["raw_json"])

    assert payload["promotion_status"] == probe["promotion_status"]
    assert payload["promotion_result"]["plus_trial_campaign_id"] == "real-campaign"
    assert payload["promotion"]["last_result"]["plus_trial_eligible"] is True
    assert record["plan_type"] == "free"
    assert "at-secret-value" not in record["raw_json"]


def test_mark_promotion_persists_payment_detection_without_copying_session_token(tmp_path, monkeypatch):
    database = tmp_path / "accounts.sqlite3"
    session_path = tmp_path / "session_payment@example.com.json"
    monkeypatch.setattr(storage, "database_path", lambda cfg=None: database)
    session_path.write_text(json.dumps({
        "email": "payment@example.com",
        "success": True,
        "access_token": "at-must-stay-out-of-raw-json",
    }), encoding="utf-8")
    assert storage.upsert_account({
        "email": "payment@example.com",
        "success": True,
        "access_token": "at-must-stay-out-of-raw-json",
    }, json_path=str(session_path))

    probe = {
        "ok": True,
        "promotion_status": "Có thể dùng thử Plus",
        "payment_method_badges": ["Trial · 0 đ", "Card", "MoMo"],
        "payment_method_types": ["card", "momo"],
        "custom_payment_methods": [],
        "amount_due": 0,
        "currency": "VND",
        "offer_state": "zero_due",
        "payment_capability": {"ok": True, "amount_minor": 0, "currency": "VND"},
    }
    assert storage.mark_promotion_status(
        "payment@example.com", probe["promotion_status"], promotion_result=probe
    )

    record = storage.get_account_record("payment@example.com")
    raw = json.loads(record["raw_json"])
    saved_session = json.loads(session_path.read_text(encoding="utf-8"))
    assert raw["payment_method_badges"] == probe["payment_method_badges"]
    assert raw["payment_method_types"] == ["card", "momo"]
    assert raw["amount_due"] == 0
    assert raw["currency"] == "VND"
    assert "at-must-stay-out-of-raw-json" not in record["raw_json"]
    assert saved_session["access_token"] == "at-must-stay-out-of-raw-json"
    assert saved_session["payment_method_badges"] == probe["payment_method_badges"]

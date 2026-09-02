<div align="center">
  <img src="./SmsWorkbench/Assets/black-kitten.png" width="140" alt="Logo GPT Register Tool" />
  <h1>GPT Register Tool</h1>
  <p><strong>Ứng dụng Windows quản lý quy trình đăng ký, email OTP, Session, ưu đãi và phương thức thanh toán ChatGPT.</strong></p>
  <p><a href="./README.md">Tiếng Việt</a> · <a href="./README_EN.md">English</a></p>
  <p>
    <img src="https://img.shields.io/badge/Windows-10%2F11-0078D4?logo=windows&logoColor=white" alt="Windows 10/11" />
    <img src="https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet&logoColor=white" alt=".NET 10" />
    <img src="https://img.shields.io/badge/Python-3.10%2B-3776AB?logo=python&logoColor=white" alt="Python 3.10+" />
  </p>
</div>

## Giới thiệu

GPT Register Tool sử dụng giao diện WPF viết bằng C# và backend Python. Ứng dụng gom các tác vụ đăng ký email, nhận OTP, lưu Session, kiểm tra Access Token, bật TOTP 2FA, phát hiện ưu đãi/phương thức thanh toán và quản lý tài khoản vào một giao diện Windows thống nhất.

Dữ liệu vận hành được lưu cục bộ trong `sessions/`, `runtime/` và SQLite. Các thư mục này cùng cấu hình chứa khóa bí mật được Git bỏ qua mặc định.

> Dự án chỉ dành cho tài khoản, hệ thống và dữ liệu mà bạn có quyền sử dụng. Công cụ không bảo đảm vượt CAPTCHA, giới hạn tốc độ, kiểm soát rủi ro hoặc chính sách của dịch vụ bên thứ ba.

## Luồng hoạt động chính

```text
Nguồn email
  -> Khởi tạo phiên đăng ký
  -> Nhận và xác minh email OTP
  -> Tạo tài khoản / lấy Session
  -> Kiểm tra Access Token HTTP 200
  -> Phát hiện ưu đãi và phương thức thanh toán
  -> Bật TOTP 2FA mặc định
  -> Lưu Session JSON + chỉ mục SQLite
  -> Hiển thị badge, lọc và xuất tài khoản trên SmsWorkbench
```

Phần phát hiện thanh toán chỉ đi tới Checkout và Stripe init để đọc khả năng thanh toán, số tiền và tiền tệ. Nó không tạo Payment Method, không Confirm/Approve và không trừ tiền. Tuy nhiên, đây vẫn là một yêu cầu khởi tạo Checkout ở phía dịch vụ; có thể bị từ chối bởi kiểm soát rủi ro hoặc giới hạn của nhà cung cấp.

## Tính năng hiện có

### Đăng ký và email OTP

- Đăng ký một hoặc nhiều tài khoản qua giao thức hoặc trình duyệt hỗ trợ.
- Nguồn email: ReMail, Smailr, CFWorker, Microsoft Graph/OAuth, Outlook/Hotmail IMAP, Gmail, iCloud URL và các định dạng mailbox lịch sử.
- Tự động nhận diện khi luồng xác thực đã chuyển thẳng tới `email-verification`; không gọi lại endpoint đăng ký mật khẩu cũ trong trạng thái này.
- OTP được lọc theo người nhận, thời gian phát hành, người gửi, tiêu đề và message ID để hạn chế dùng nhầm mã cũ.
- Smailr chỉ tái sử dụng mailbox chưa có thư và chưa gắn với tài khoản trong cơ sở dữ liệu.
- Mỗi tài khoản có phiên, fingerprint, `oai-did` và trạng thái đăng ký riêng.
- Hỗ trợ checkpoint để tiếp tục bước kiểm tra AT mà không lặp lại toàn bộ quy trình OTP.
- TOTP 2FA được bật mặc định; tùy chọn **Tắt 2FA** chỉ dùng khi người vận hành chủ động chọn.

### Session và quản lý tài khoản

- Lưu Session JSON trong `sessions/` và chỉ mục tài khoản trong `runtime/accounts.sqlite3`.
- Hiển thị trạng thái AT, RT, 2FA, loại tài khoản, quốc gia đăng ký, ưu đãi và phương thức thanh toán.
- Kiểm tra sống tài khoản và quota; HTTP 401 được phân loại riêng để phục hồi có kiểm soát.
- Xem chi tiết, mở file Session nguồn, xem hộp thư và sao chép Access Token.
- Xóa nhiều tài khoản qua một lệnh backend thay vì khởi chạy từng tiến trình riêng.
- Làm mới danh sách sau khi từng Session đăng ký được lưu, không cần chờ cả batch kết thúc.

### Phát hiện ưu đãi và phương thức thanh toán

Sau khi tài khoản có Access Token hợp lệ, backend có thể chạy bước `DETECT_PAYMENT_METHODS` bằng cùng Session và proxy của luồng đăng ký.

Kết quả chuẩn hóa gồm:

- `payment_method_badges`
- `payment_method_types`
- `custom_payment_methods`
- `amount_minor` / `amount_due`
- `currency`
- `offer_state`
- `payment_check_status`
- `payment_check_error`
- `payment_checked_at`

Giao diện hiển thị các pill badge như `Trial · 0 đ`, `Card`, `Link`, `Apple Pay`, `Google Pay`, `MoMo`, `GCash`, `GoPay`, `UPI`, `Kakao Pay`, `Naver Pay` và các phương thức được trả về theo khu vực.

Các bộ lọc AND hiện có gồm `Trial`, `No offer`, `MoMo`, `GPay`, `Apple Pay`, `Card`, `UPI`, `GoPay`, `Kakao`, `Naver`, `PTTT lỗi` và `Chưa check`. Trạng thái lỗi/chưa kiểm tra được lấy từ dữ liệu có cấu trúc, không suy luận chỉ từ chuỗi badge.

### Xuất tài khoản theo bộ lọc

- Chọn một hoặc nhiều badge lọc trên danh sách tài khoản.
- Bấm **Xuất theo lọc** để xuất toàn bộ tài khoản khớp điều kiện AND.
- Định dạng đăng nhập: `email|password|TOTP_SECRET`.
- Chỉ tài khoản có đủ mật khẩu đã xác nhận và TOTP secret mới được đưa vào file này.
- Tài khoản passwordless không được gắn mật khẩu sinh tạm vào kết quả xuất vì mật khẩu đó chưa được máy chủ xác nhận.

File xuất theo lọc nằm trong `runtime/filtered_accounts/`.

### TOTP 2FA

Cột `2FA` chỉ hiển thị trạng thái `Đã thiết lập` hoặc `Chưa thiết lập`. TOTP secret được coi là dữ liệu nhạy cảm và không xuất hiện trong API đọc công khai của bảng tài khoản.

Để lấy secret của tài khoản thuộc quyền quản lý của bạn:

1. Bấm **Xem** ở cột **Chi tiết**.
2. Chọn **Mở file nguồn**.
3. Tìm trường `totp_secret` trong Session JSON.
4. Nhập secret vào ứng dụng xác thực tương thích TOTP.

Không chia sẻ Session JSON hoặc `totp_secret`; bất kỳ ai có secret đều có thể tạo mã 2FA.

### Thanh toán giao thức

Các adapter hiện có bao gồm PayPal, MoMo, GoPay, GCash, GrabPay, UPI, iDEAL, PIX, Kakao Pay, BLIK, TWINT, Direct Card Checkout, QRIS, Bizum và Naver Pay.

- Batch thanh toán có JIT AT, Canary, retry theo loại lỗi và checkpoint nguyên tử.
- Checkout proxy và Approve proxy được cấu hình độc lập theo phương thức.
- Chế độ **chỉ thăm dò** dừng trước khi tạo Payment Method, Confirm, Approve hoặc chuyển hướng nhà cung cấp.
- Kết quả chuẩn hóa thành `completed`, `failed`, `cancelled`, `unknown` hoặc `timed_out` cùng `retryable` và `error_stage`.
- MoMo chỉ được coi là thành công khi có URL/QR hợp lệ từ `payment.momo.vn`.
- Các tác vụ có khả năng tạo hoặc xác nhận giao dịch phải được người vận hành khởi chạy rõ ràng; chúng không thuộc bước detect badge sau đăng ký.

### Nhập, xuất và tích hợp

- Xuất Session JSON, Codex JSON và Get Session TXT.
- Nhập/xuất CPA và SUB2API theo luồng riêng.
- Agent Identity chỉ được tạo hoặc sử dụng tại biên nhập SUB2API, không nằm trong luồng đăng ký chính.
- Hỗ trợ đổi email liên kết, xem hộp thư và phục hồi thông tin mailbox từ Session.

## Yêu cầu hệ thống

- Windows 10/11 x64.
- Python 3.10 trở lên; CI hiện dùng Python 3.12.
- .NET SDK `10.0.300` hoặc feature band tương thích để biên dịch.
- .NET 10 Desktop Runtime để chạy bản framework-dependent.
- Node.js 18+ trong `PATH` cho Sentinel QuickJS.
- Playwright Chromium cho các tác vụ cần Chromium/TLS trình duyệt.
- Kết nối hợp lệ tới nguồn email và các dịch vụ được cấu hình.

Các dependency Python chính nằm trong `requirements.txt`, bao gồm `curl_cffi==0.16.0`, `pyotp`, Playwright, Camoufox và các thư viện HTTP/OAuth.

## Cài đặt

### Chạy từ mã nguồn

```powershell
git clone https://github.com/huybitvvt/reggpt.git
cd reggpt

python -m venv .venv
.\.venv\Scripts\Activate.ps1
python -m pip install -r requirements.txt
python -m playwright install chromium

Copy-Item config.example.json config.json
powershell -ExecutionPolicy Bypass -File .\SmsWorkbench\build_dotnet.ps1
.\dist\net10\SmsWorkbench.exe
```

Không dùng `dotnet build` làm lệnh phát hành desktop. Điểm vào chuẩn là `SmsWorkbench/build_dotnet.ps1`; script này publish ứng dụng vào `dist/net10` và dọn thư mục trung gian.

### Bản portable hoặc installer

Nếu dùng tài sản từ GitHub Releases:

```text
GPT-Register-Tool-Setup-vYYYY.MM.DD.exe
GPT-Register-Tool-win-x64-vYYYY.MM.DD.zip
GPT-Register-Tool-vYYYY.MM.DD.sha256.txt
```

Với bản portable, giải nén rồi chạy:

```powershell
python -m pip install -r requirements.txt
Copy-Item config.example.json config.json
.\dist\net10\SmsWorkbench.exe
```

### Kiểm tra môi trường

```powershell
python scripts/preflight_env.py
python chatgpt_phone_reg.py --doctor --json
```

## Cấu hình ban đầu

Ứng dụng đọc cấu hình đã tách theo phạm vi:

| File | Nội dung |
| --- | --- |
| `runtime.json` | Đăng ký, email, timeout, lưu trữ và runtime |
| `proxy.json` | Proxy đăng ký, proxy mailbox và phone reuse |
| `payment.json` | Phương thức thanh toán và routing |
| `config.json` | Cấu hình local/legacy; bị Git bỏ qua |
| `config.example.json` | Mẫu an toàn được theo dõi trong Git |

Không ghi API key, token, mật khẩu email hoặc proxy thật vào `config.example.json`.

### Đăng ký, ưu đãi và detect phương thức

```json
{
  "registration": {
    "driver": "protocol",
    "detect_offer": true,
    "detect_payment_methods": true,
    "payment_country": "VN",
    "offer_check_timeout_seconds": 20,
    "retry_attempts": 2
  },
  "email_registration": {
    "registration_mode": "password",
    "otp_poll_interval": 1
  }
}
```

`payment_country` cần là mã quốc gia ISO hai ký tự, ví dụ `VN`, `US`, `PH`, `ID` hoặc `IN`. Nếu không đặt, backend chỉ suy luận khi proxy mang đủ thông tin khu vực.

### Smailr

```json
{
  "email_registration": {
    "smailr": {
      "enabled": true,
      "base_url": "https://smailr.com",
      "api_key": "",
      "default_domain": "smailr.com",
      "domain_ids": {},
      "reuse_existing_on_level_error": true,
      "timeout": 30
    }
  }
}
```

Có thể đặt API key bằng biến môi trường:

```powershell
$env:SMAILR_API_KEY = "your-key"
```

Khi tài khoản Smailr không đủ cấp để tạo mailbox qua API và bật `reuse_existing_on_level_error`, tool chỉ chọn mailbox còn trống (`mail_count = 0`), chưa archive, còn nhận thư và chưa có bản ghi tài khoản.

### ReMail

```json
{
  "email_registration": {
    "remail": {
      "enabled": true,
      "base_url": "https://remail.aishop6.com",
      "api_key": "",
      "project_id": 2,
      "service_mode": "purchase",
      "supply": "private_first",
      "email_suffix": "outlook.com",
      "otp_poll_interval": 1,
      "batch_timeout": 200
    }
  }
}
```

Biến môi trường tương ứng:

```powershell
$env:REMAIL_API_KEY = "your-key"
```

Chế độ `purchase` phù hợp khi cần giữ mailbox lâu dài. Service Token dùng để đọc thư và phải được bảo vệ như thông tin đăng nhập.

### Proxy

```json
{
  "mailbox_proxy": "http://127.0.0.1:7897",
  "proxy": {
    "registration": "http://user:pass@gateway:port",
    "default": "http://user:pass@gateway:port",
    "pool": ["http://user:pass@gateway:port"]
  }
}
```

Ba phạm vi được tách riêng:

- Proxy đăng ký cho Auth/ChatGPT.
- Proxy mailbox cho việc nhận OTP.
- Proxy Checkout/Approve theo từng phương thức thanh toán.

Không thay đổi hoặc xoay proxy nhằm né kiểm soát rủi ro. Một giao dịch xác thực phải giữ Session và đường truyền nhất quán.

## Sử dụng giao diện SmsWorkbench

1. Mở **Cài đặt**, nhập nguồn email và cấu hình cần thiết.
2. Chạy tự kiểm tra môi trường.
3. Chọn **Đăng ký nhanh** và nguồn email.
4. Dùng một tài khoản thử nghiệm trước khi chạy batch.
5. Theo dõi từng stage trong khung log.
6. Khi hoàn tất, kiểm tra AT, 2FA, ưu đãi và badge thanh toán trong bảng tài khoản.
7. Chọn các chip lọc rồi bấm **Xuất theo lọc** nếu cần xuất tài khoản đủ điều kiện.

Các driver đăng ký trong `registration.driver`: `protocol`, `playwright`, `camoufox`, `cloak`, `roxy` và `adspower`.

Driver trình duyệt không tự vượt CAPTCHA. Khi gặp thử thách cần thao tác người dùng, tác vụ kết thúc với `manual_challenge_required`.

## Lệnh CLI thường dùng

### Xem toàn bộ tham số

```powershell
python chatgpt_phone_reg.py --help
```

### Đăng ký bằng Smailr

```powershell
python chatgpt_phone_reg.py `
  --buy-smailr-mailbox `
  --smailr-domain smailr.com `
  --count 1 `
  --workers 1 `
  --registration-at-only `
  --no-phone-reuse `
  --check-promotion-after-registration
```

### Đăng ký bằng ReMail

```powershell
python chatgpt_phone_reg.py `
  --buy-remail-mailbox `
  --remail-service-mode purchase `
  --count 1 `
  --workers 1 `
  --registration-at-only `
  --no-phone-reuse `
  --check-promotion-after-registration
```

Thêm `--no-2fa` chỉ khi muốn chủ động bỏ qua TOTP 2FA.

### Đăng ký từ file mailbox

```powershell
python chatgpt_phone_reg.py --mailbox-file mailbox_tokens.txt --count 1 --workers 1
```

### Kiểm tra sống và quota

```powershell
python chatgpt_phone_reg.py --refresh-local-quota --email user@example.com
```

### Kiểm tra ưu đãi

```powershell
python chatgpt_phone_reg.py --check-promotion --email user@example.com
```

### Chỉ thăm dò phương thức thanh toán

```powershell
python chatgpt_phone_reg.py `
  --extract-payment-link `
  --payment-method momo `
  --email user@example.com `
  --payment-probe-only `
  --payment-country VN `
  --workers 1
```

### Batch thanh toán có checkpoint

```powershell
python chatgpt_phone_reg.py `
  --extract-payment-link `
  --payment-method momo `
  --email-file runtime\eligible.txt `
  --workers 2 `
  --payment-batch-id momo_vn_test `
  --payment-canary 1 `
  --payment-retries 1
```

## Cấu trúc dự án

```text
GPT-Register-Tool/
├── SmsWorkbench/                 # Giao diện WPF, cửa sổ, ViewModel và dịch vụ desktop
├── SmsWorkbench.Contracts/       # Hợp đồng lệnh/kết quả giữa UI và backend
├── sms_tool/                     # Backend Python và CLI
│   ├── commands/                 # Điều phối lệnh cấp cao
│   ├── pay_link/                 # Registry/adapter chuẩn hóa link thanh toán
│   ├── paypal/                   # Luồng PayPal chuyên biệt
│   ├── providers/                # Client nguồn mailbox
│   └── sentinel/                 # Sentinel bundle/client/runtime
├── services/
│   ├── mail-otp-web/             # Dịch vụ web chẩn đoán mailbox/OTP
│   └── protocol-payment/         # Các adapter thanh toán giao thức bổ sung
├── tests/
│   └── SmsWorkbench.Tests/       # Unit test C#
├── scripts/                      # Preflight, build, release, audit và security scan
├── docs/                         # Kiến trúc, hướng dẫn và release notes
├── chatgpt_phone_reg.py          # Entrypoint CLI tương thích
├── config.example.json           # Cấu hình mẫu
├── payment_methods.json          # Danh mục phương thức thanh toán
├── sensitive_policy.json         # Chính sách dữ liệu nhạy cảm
└── GPTRegisterTool.slnx          # Solution .NET
```

### Các module quan trọng

| Module | Trách nhiệm |
| --- | --- |
| `sms_tool/registration_handlers.py` | State machine đăng ký, OTP, Session, AT probe, detect phương thức và TOTP |
| `sms_tool/account_promotion.py` | Kiểm tra ưu đãi tài khoản |
| `sms_tool/payment_capability.py` | Checkout/Stripe init và chuẩn hóa badge phương thức |
| `sms_tool/checkout_contract.py` | Hợp đồng request/response Checkout |
| `sms_tool/mailbox_smailr.py` | Tạo/tái sử dụng Smailr và polling OTP |
| `sms_tool/mailbox_remail.py` | Mua mailbox ReMail, đọc thư và lấy OTP |
| `sms_tool/account_liveness.py` | Kiểm tra Access Token và quota |
| `sms_tool/payment_batch.py` | Batch payment, Canary, retry và checkpoint |
| `sms_tool/desktop_read.py` | Biên đọc dữ liệu an toàn cho desktop |
| `sms_tool/storage.py` | Session JSON, SQLite và trạng thái tài khoản |
| `SmsWorkbench/MainWindow.Pools.cs` | Đọc và hiển thị danh sách tài khoản |
| `SmsWorkbench/AccountPaymentFilters.cs` | Lọc AND theo ưu đãi/phương thức/trạng thái |
| `SmsWorkbench/AccountCredentialExport.cs` | Xuất `email|password|TOTP` có kiểm tra trường bắt buộc |
| `SmsWorkbench/PaymentBatchWindow.xaml` | Giao diện batch payment và badge kết quả |

Xem thêm [kiến trúc hệ thống](docs/architecture.md) và [bản đồ thư mục](docs/directory-map.md).

## Dữ liệu cục bộ và bảo mật

Các đường dẫn sau không được commit:

```text
config.json
runtime.json
proxy.json
payment.json
sessions/
runtime/
mailbox_tokens.txt
*.env
```

Các dữ liệu cần bảo vệ:

- Access Token và Refresh Token.
- Cookie và Session JSON.
- Mật khẩu tài khoản/email.
- TOTP secret và mã OTP.
- API key của mailbox, proxy, CAPTCHA hoặc dịch vụ thanh toán.
- URL thanh toán, payment token và mã QR.

Repository có pre-commit guard và các script quét dữ liệu nhạy cảm. Cài Git hook bằng:

```powershell
python scripts/install_git_hooks.py
```

Nếu một secret từng bị đưa lên Git hoặc gửi qua kênh không an toàn, hãy thu hồi/đổi secret; chỉ xóa khỏi commit mới không loại nó khỏi lịch sử Git.

## Kiểm thử

### Python

```powershell
python -m compileall -q sms_tool services
python -m pytest -q
```

### C#/.NET

```powershell
dotnet test .\GPTRegisterTool.slnx -c Release
```

### Kiểm tra bảo mật và kiến trúc

```powershell
python scripts/sensitive_field_scan.py
python scripts/scan_hardcoded_secrets.py
python scripts/architecture_scan.py
```

CI trên Windows chạy compile Python, toàn bộ pytest, xUnit, security scan, architecture scan và publish desktop chuẩn.

## Biên dịch và phát hành

### Publish desktop

```powershell
powershell -ExecutionPolicy Bypass -File .\SmsWorkbench\build_dotnet.ps1
```

Đầu ra: `dist/net10/SmsWorkbench.exe`.

### Tạo installer và portable ZIP

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\build_installer.ps1 -Version vYYYY.MM.DD
```

Kết quả nằm trong `dist/release/`. Có thể thêm `-SelfSign` cho bản ký nội bộ.

## Xử lý sự cố thường gặp

### `account_creation_failed`

- Đây là phản hồi từ API tạo tài khoản, không phải lỗi detect thanh toán hoặc TOTP.
- Không lặp lại hàng loạt trên cùng mailbox đã nhận thư xác minh.
- Kiểm tra xem Auth flow đã chuyển tới `/email-verification` hay chưa.
- Phiên bản hiện tại tự bỏ qua endpoint `user/register` cũ nếu OTP flow đã sẵn sàng.
- Nếu dịch vụ vẫn từ chối, dừng batch và kiểm tra giới hạn/rủi ro thay vì liên tục đổi IP để thử lại.

### `invalid_auth_step` khi xác minh OTP

- Thường xảy ra khi một endpoint cũ được gọi sau khi phiên đã chuyển bước hoặc mailbox đang mang trạng thái đăng ký dở dang.
- Dùng mailbox chưa có thư và khởi tạo phiên đăng ký mới.
- Không sử dụng OTP cũ từ lần chạy trước.

### `payment_country_unknown`

Đặt `registration.payment_country` trong Cài đặt hoặc cấu hình, ví dụ `VN`.

### `unusual activity` ở Checkout

Đây là từ chối của phía dịch vụ. Detect badge không tạo thanh toán nhưng vẫn khởi tạo Checkout để lấy cấu hình. Không có cấu hình local nào bảo đảm loại bỏ từ chối này; cần dừng thử lại dồn dập và tuân thủ giới hạn dịch vụ.

### Có `has_totp: true` nhưng không thấy secret

API đọc danh sách chỉ trả cờ hiện diện. Bấm **Xem → Mở file nguồn** và tìm `totp_secret` trong Session JSON. Không đăng Session JSON công khai.

### Không xuất được `email|password|TOTP`

Kiểm tra thông báo số lượng thiếu mật khẩu/2FA. Tài khoản passwordless có thể có AT và TOTP nhưng không có mật khẩu đăng nhập đã xác nhận, nên bị bỏ qua đúng thiết kế.

## Tài liệu liên quan

- [Kiến trúc](docs/architecture.md)
- [Bản đồ thư mục](docs/directory-map.md)
- [Kiến trúc đăng ký và proxy](docs/registration-and-proxy-architecture.md)
- [Hướng dẫn proxy](PROXY_GUIDE.md)
- [Thanh toán giao thức](docs/protocol-payment-enhancement.md)
- [PayPal zero-due](docs/paypal-zero-due-link.md)
- [Đánh giá bảo mật](docs/security-exposure-assessment-2026-08-31.md)
- [Release notes](docs/README.md)

## Nhà tài trợ

<img width="5728" height="672" alt="IPWO" src="https://github.com/user-attachments/assets/5f3b5b22-5132-4bc4-b8b8-3a0e92b47f37" />

[IPWO](https://www.ipwo.net) cung cấp proxy residential toàn cầu, nhiều khu vực và loại IP động/tĩnh. Việc lựa chọn, chi phí và tuân thủ chính sách proxy thuộc trách nhiệm của người sử dụng.

## Trách nhiệm sử dụng

Chỉ sử dụng dự án trong phạm vi được ủy quyền và phù hợp với điều khoản dịch vụ, pháp luật địa phương cùng chính sách của tổ chức. Người vận hành tự chịu trách nhiệm về chi phí dịch vụ bên thứ ba, bảo mật tài khoản, quyền riêng tư dữ liệu và mọi giao dịch được chủ động khởi chạy.

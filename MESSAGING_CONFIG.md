# Messaging Configuration (Email + SMS)

The email (Hostinger SMTP via MailKit) and SMS (MSG91 Flow API) **code is complete and
wired to live triggers**. Messages don't send because the provider **secrets are unset**.
Set the values below in **Azure App Service → Configuration → Application settings**.

> Azure App Service uses `__` (double underscore) as the config section separator.
> Do not commit secrets to `appsettings*.json`.

## Required — sending will not work without these

| App Service setting | Purpose | Current state |
|---|---|---|
| `Email__SmtpPassword` | Hostinger SMTP password for `support@racetik.com`. Gates **all email** (completion email, support-ticket email). | empty in appsettings |
| `Notification__Msg91__AuthKey` | MSG91 auth key. Gates **all SMS** (checkpoint + completion). | placeholder `SET_IN_AZURE_ENV_VARS` |
| `Notification__Msg91__CheckpointTemplateId` | MSG91 Flow template id for the **checkpoint-crossing SMS**. | placeholder `SET_IN_AZURE_ENV_VARS` |

## Already set (override only if they change)

| App Service setting | Purpose | Current state |
|---|---|---|
| `Notification__Msg91__CompletionTemplateId` | MSG91 Flow template for the **completion SMS**. | real id present in appsettings |
| `Email__SmtpHost` | SMTP host. | `smtp.hostinger.com` |
| `Email__SmtpPort` | SMTP port (465 = SSL-on-connect, which the code uses). | `465` |
| `Email__SmtpUsername` / `Email__FromAddress` | SMTP login / From address. | `support@racetik.com` |
| `Email__FromName` | From display name. | `Racetik` |
| `Notification__Msg91__SenderId` | Sender id (bound; the Flow template supplies the actual sender). | `RACTIK` |
| `AppSettings__FrontendUrl` | Base URL for reset/invite links in emails. | only set in Development json — **set in Azure too** |

## Trigger points (all live in code — for reference)

- **Completion email** — `ResultsService` → `NotifyRaceCompletionAsync` → `RaceNotificationService` (fires when a race's stored status is Finished).
- **Completion SMS** — same `NotifyRaceCompletionAsync` path.
- **Checkpoint SMS** — `OnlineTagIngestionService` → `NotifyCheckpointCrossingAsync` (fire-and-forget per crossing).
- **Support-ticket email** — `SupportQueryService` → `NotifySupportTicketCreatedAsync`.

## Notes (no code change made)

- `Email:UseSsl` in appsettings is **not read** — SSL is hardcoded to SSL-on-connect (correct for port 465; would need adjustment only if you switch to STARTTLS/587).
- `Notification:Msg91:SenderId` is bound but not placed in the MSG91 payload — the Flow template carries the sender, so this is expected.
- The legacy `MSG91:*` section + `Msg91SmsService`/`ISmsService` are registered but unused (no callers).
- Password-reset email is disabled in code (`AuthenticationService` ~L228, commented out) — independent of configuration; out of scope for this change.

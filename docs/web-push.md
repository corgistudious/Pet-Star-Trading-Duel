# Web Push

Players get game alerts when Pet Star is closed. Examples: card sold, outbid, auction won, duel challenge, guild
request, new friend or comment, achievement, admin notice.

## Flow

1. **Settings → Notifications on this device → switch on.** The switch is a user tap; the permission is never
   requested on page load. The browser creates a subscription with our VAPID public key, and the client registers
   it with `POST /api/push/subscriptions`.
2. **A notification row is stored** (Notification Center), exactly as before. `NotificationService` also queues a
   push job.
3. **`PushDispatcher`** handles each job in this order:
   1. Waits until the notification row is **committed**. A rolled-back transaction never produces a push.
   2. Skips the push if the player has a **live realtime connection** on any API instance (Redis key
      `ws_online:{membership}`, refreshed by the 20 s client ping). They already see the in-app toast.
   3. Picks the account's devices that enabled the type's **category**: market, auction, pvp, guild, social or
      system.
   4. Writes title, body and URL in the **account language** (en/vi) from `database/seed/push_texts.json`.
   5. Encrypts per device (**RFC 8291 aes128gcm**) and signs with **VAPID (RFC 8292, ES256)**. Only the browser
      can read the payload.
   6. Sends to the push service. A `404/410` response deletes the subscription; 10 failures in a row also delete it.
4. **Service worker:**
   - It shows the notification with the Pet Star icon and a `tag`, so repeats replace each other.
   - Tapping it focuses the open app and navigates to the screen, or opens the app. Only same-origin paths are
     followed.

## Safety and limits

- **Endpoint allowlist (anti-SSRF):** a subscription is accepted only for HTTPS endpoints on the default port with
  no user-info, at these hosts:
  - `fcm.googleapis.com` (Chrome, Edge, Android)
  - `*.push.services.mozilla.com` / `updates.push.services.mozilla.com` (Firefox)
  - `web.push.apple.com` / `*.push.apple.com` (Safari, iOS)
  - `*.notify.windows.com`

  The allowlist is checked again before every send.
- At most 10 devices per account, and 20 pushes per account per 10 minutes. Test pushes are exempt.
- One device belongs to one account: signing into another account on the same browser moves the subscription.
  Signing out unsubscribes the device.
- The payload contains no secrets (no serials or emails), and it is encrypted end-to-end anyway.
- **Not configured** (no VAPID keys) means the feature is disabled and the app says so. Nothing is faked.

## Platform support

| Platform | Support |
|---|---|
| Chrome / Edge / Firefox (Windows, macOS, Android) | yes |
| Safari macOS 16+ | yes |
| iPhone / iPad | only for the web app **added to the Home Screen** (iOS/iPadOS 16.4+), opened from there. In a Safari tab the settings screen explains how to install; no permission prompt is shown. |

## Configuration

Keys are generated **on the server** and never pasted into chat or git:

```bash
docker compose -p petstar-prod -f deployment/vps/docker-compose.yml --env-file /opt/petstar/petstar.env run --rm --no-deps api generate-vapid
```

Put `WEBPUSH_PUBLIC_KEY`, `WEBPUSH_PRIVATE_KEY` and `WEBPUSH_SUBJECT` (a `mailto:` you control) in
`/opt/petstar/petstar.env` (mode 600), then re-run `deploy.sh`. The VPS needs outbound HTTPS to the push hosts
above. No inbound port is needed, and the CSP needs no change.

Rotating the VAPID key invalidates all subscriptions. When players next open the app, the client notices the key
change and re-subscribes, provided permission is still granted.

## Tests

- **Backend:**
  - RFC 8291 Appendix A vector reproduced **byte for byte**.
  - Random messages decrypt like a browser would.
  - VAPID JWT verifies with the public key and has the right `aud`.
  - Subscribe rejects foreign or HTTP/IP/port/user-info endpoints and bad keys.
  - An offline seller receives an encrypted, signed "Card sold" push (headers checked, body decrypted).
  - A connected player gets no push.
  - Categories are respected.
  - `410 Gone` removes the subscription.
  - Device takeover works.
  - A rolled-back notification is never pushed.
- **Web unit:** availability rules (iOS install-first, unsupported, not configured, denied) and VAPID key decoding.
- **Web e2e** (full Chromium headless):
  - iPhone Safari tab shows the Home Screen hint with no switch.
  - A push delivered to the service worker (`ServiceWorker.deliverPushMessage`) becomes a visible notification
    with the right title, body, URL and tag.
  - An absolute URL in a payload is neutralised.
  - The tap message navigates; a cross-origin URL is ignored.
  - Desktop: the switch is offered, and a failed subscription is reported rather than shown as enabled.
- **Not testable here:** a real subscription to FCM/Apple/Mozilla (headless browsers in this environment have no
  push-service connection) and a real iPhone. After deployment, please enable notifications once on an iPhone Home
  Screen app and on a desktop browser, then use **Send a test notification**.

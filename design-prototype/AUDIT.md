# Redesign audit — 2026-10-04

Baseline: native net10.0-windows WPF; Core/Storage remain unchanged. No embedded browser.

| Screen | Existing interaction to preserve | Handler / ownership |
|---|---|---|
| Sell | Category, cross-category search, unavailable products, add, name autosave, +/- quantity, expand cart, hold | RenderSelling, RefreshProducts, RefreshCart, Save, nameTimer |
| Payment | Cash/QRIS tabs, numeric keyboard, quick cash, insufficient amount, back, completion | RenderPayment, UpdateChange, Complete |
| Success | Change / QRIS amount, next order, reprint, uncertain printing message | Complete, Print, ReceiptPrinter |
| Held/drafts | Search, resume preserving current draft, cancel confirmation | RenderOrders(true), OrderRules |
| History | Search last 1000 orders, details, cancelled reason, historical prices, reprint | RenderOrders(false), RenderOrderDetail |
| Refund | Requested/completed/failed, amount/reason/channel, owner PIN, explicit money-returned checkbox | RenderRefunds, RenderResolveRefund, LocalStore |
| Settings | PIN setup/unlock/cooldown, printer selection, PIN change, connection test/save, direct Sheets activation | MaintenanceWindow, SettingsFile, ActivationWindow |
| Startup | Local initialization, activation or offline, locked/corrupt database errors | App, DatabaseLease |
| Background | Outbox, ordered finance sync, QRIS proof notification, catalog refresh, direct Sheets | ConnectAsync, SyncDataAsync, PollPaymentsAsync, SyncSheetsAsync |

There is no remaining cash-management, backup, or restore UI. Do not reintroduce it. Sales recap is maintained in Google Sheets. The active receipt pipeline is Windows FlowDocument printing (not a new raw ESC-POS implementation).

## Problems
- Tall horizontal masthead plus nested padded catalog consume vertical and horizontal space.
- Product cards 128px high, 190px column threshold: three columns at common laptop sizes.
- Cart and catalog look like equivalent dashboard cards rather than a working counter.
- Excess ornamental layers and repeated card decoration add clutter without helping selection.
- Settings is difficult to find in a small footer target.

## New direction: Counter / Receipt
- 88px left navigation rail: stable navigation and dedicated settings target.
- Slim contextual topbar. Catalog has no outer card; neutral background distinguishes it from the white order slip.
- Four columns at 1280, compact 94px tiles, selected count and price hierarchy, no per-category colors.
- White fixed-width order slip, slim customer field, scroll only the items, anchored settlement zone.
- Payment follows the same left order / right action hierarchy; number pad and footer remain pinned.
- Native scroll only. No custom inertia. No per-card blur or shadow.

## Boundaries
Prototype is local dummy data only, including payment/printer/sync state controls. It must never be mistaken for production payment confirmation. Native logic keeps existing IDs, services, async I/O and transaction semantics. Hardware print and live integrations require real-device confirmation; CI is not evidence of those.

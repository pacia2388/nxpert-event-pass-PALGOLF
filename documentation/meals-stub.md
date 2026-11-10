# Meal stubs

Meal stubs are meal coupons given to registrants of an event and redeemed at meal counters.
Events are described in [events.md](events.md); table columns in
[database-table.md](database-table.md).

Main code:

| Area | Location |
|---|---|
| Definitions (saved with the event) | `Controllers/EventsController.cs` (`ValidateMeals`, `BuildMeal`, `RebuildMeals`) |
| Issuing stubs | `Services/MealStubService.cs` (`EnsureStubsAsync`), called from `Services/PassService.cs` |
| Stub image (ticket PNG) | `Services/MealStubImageService.cs`; served by `PassesController` |
| Merchant scanner | `Controllers/MealScanController.cs`; page `/merchant/scan` |
| Admin scanner (Meal stubs mode) | `Controllers/CheckInController.cs` (`redeem-meals`) |
| Attendance tab actions | `Controllers/AttendanceController.cs` (meal endpoints) |

All backend paths are under `backend/EventRegistration.Api/`.

---

## 1. Concepts

| Term | Table | Meaning |
|---|---|---|
| **Meal (definition)** | `Meals` | What registrants are entitled to: name, value (₱ denomination), quantity per registrant, color, valid dates. Set on the event form |
| **Meal stub (issued)** | `MealStubs` | One physical coupon for one registrant, with its own unique **control number** (QR). Created from the definitions |
| **Sub-event set redemption** | `Registrations.MealsRedeemed*` | Flag: a sub-event registration's whole stub set was handed out |
| **Per-day redemption** | `MealRedemptions` | Row per registrant per day: that day's stubs were handed out (events without sub-events) |

## 2. Defining stubs (event form)

| Event type | Where | Grouping |
|---|---|---|
| With sub-events | Each sub-event's **Meal stubs** block → Add stub | `Meals.SessionId` set, `Day` = 0 |
| Without sub-events | **Meal stubs (per day)** → Add day → Add stub | `Meals.SessionId` NULL, `Day` = 1, 2, … |

Per stub: **Name** (required; blank rows are ignored), **Denomination** (₱, ≥ 0), **Qty** per
registrant (≥ 1), **Valid from / Valid to**, **Color** (hex or name).

Rules applied on save:

- Denomination below 0 or quantity below 1 is rejected.
- Valid dates are **clamped to the event's start–end dates**; blank means the event's first/last
  day; if To is before From, To = From.
- **Every save deletes and recreates all meal definitions** of the event. Already-issued stubs
  are not touched (they keep their snapshot values).

Online events have no meal stubs.

## 3. Issuing stubs

```
Registration confirmed ──► pass is sent ──► EnsureStubsAsync(registration)
                                              ├─ stubs already exist? → reuse them
                                              └─ else create Qty copies of each applicable definition
```

- Applicable definitions: the registration's sub-event stubs, or (no sub-event) all event-level
  stubs across all days.
- Each copy gets a new `ControlNumber` (GUID) and a **snapshot** of name, value, color, day and
  valid dates, plus `CopyIndex` 1..Qty.
- Stubs are created **once**. Later edits to definitions don't change issued stubs.
- A registration confirmed **before** any stubs were defined gets them the next time its pass is
  sent (e.g. **Resend pass**).
- Only In person and Hybrid events issue stubs (`EventFormats.HasVenue`).

Delivery: each stub is a printable ticket PNG (info panel, cut guides, colored tear-off tab, QR)
sent with the pass by email / Viber.

| URL | Returns |
|---|---|
| `GET /api/passes/meal-stub/{control}/stub.png` | Full ticket image |
| `GET /api/passes/meal-stub/{control}/qr.png` | QR only |

Both are anonymous (the control number is the key) and cached for 1 day.

## 4. Redeeming

There are **three separate redemption records**. They are not linked to each other.

### A. Individual stubs (stub QR) — `MealStubs`

The normal path. Scans the stub's own QR (payload `MEAL:{controlNumber}`).

| Who / where | Endpoint |
|---|---|
| Merchant (or Admin) on the **Meal Stub Scanner** (`/merchant/scan`), camera or barcode | `POST /api/meal-scan/redeem` |
| Admin, Can confirm: Attendance → **Meal Stubs** tab, per stub | `POST .../attendance/redeem-stub-ids`, `unredeem-stub-ids` |
| Admin, Can confirm: all unredeemed stubs of selected registrants | `POST .../attendance/redeem-stubs`, `unredeem-stubs` |

Scanner results:

| Result | When |
|---|---|
| **valid** — "Stub redeemed" | Sets `RedeemedAt`, `RedeemedBy` (scanner's name), `RedeemedByUserId`, `RedeemedMethod = Scan` |
| **already** | Stub was redeemed before (shows by whom) |
| **notvalid** | Today is outside the stub's valid dates (shows the range) |
| **invalid** | Code is not a known control number (or the registration was removed) |

Each stub is redeemable **once**. Manual redemptions use `RedeemedMethod = Manual`.
`GET /api/meal-scan/my-tally` gives the signed-in scanner's all-time count and ₱ total.

### B. Sub-event set (entry pass QR) — `Registrations.MealsRedeemed*`

Admin check-in scanner in **Meal stubs** mode scans the **entry pass** (`REG:{token}`) of a
sub-event registration and marks its whole stub set handed out. Also from Attendance:
`redeem-meals` / `unredeem-meals`.

### C. Per day (entry pass QR) — `MealRedemptions`

Same scanner mode for an event **without** sub-events: redeems the chosen **Day**'s stubs (one
`MealRedemptions` row per registrant per day). Also from Attendance: `meal-board`,
`redeem-meal-day` / `unredeem-meal-day`.

Results for B and C: **valid**, **already**, **unconfirmed** (registration not confirmed),
**nomeals** (nothing defined for that sub-event/day), **invalid**.

## 5. Rules summary

| Rule | Where enforced |
|---|---|
| Stubs only for confirmed registrations | Issued when the pass is sent (on confirm) |
| No stubs for Online events | `PassService` (`HasVenue`) |
| Redeem once | All three paths |
| Valid dates | Stub QR scanner (path A) only |
| Merchant sees only the Meal Stub Scanner | Frontend `merchantGuard`; API `MealScanController` allows Admin, SuperAdmin, Merchant |
| Needs internet | Meal-stub scanning has no offline mode |

## 6. Admin views

- **Meal Stubs tab** (event detail): one row per issued stub: participant, stub, denomination,
  control #, status, redeemed at / by. Filters: search, status, merchant. Shows total ₱ redeemed.
- **CSV export** (Attendance) includes kit and meal columns.

## 7. Known gaps

| Gap | Detail |
|---|---|
| Three unlinked redemption records | Handing out a sub-event set (B) or a day (C) does not mark the individual stubs (A) redeemed, so the same meals can be redeemed again by scanning the stubs |
| Valid dates not checked on paths B and C | Only the stub-QR scanner checks `ValidFrom` / `ValidTo` |
| Path B records the registrant as "redeemed by" | `CheckInController.RedeemMeals` sets `MealsRedeemedBy = reg.DisplayName`, not the scanning user |
| No concurrency guard on redeem | Two scanners reading the same stub at the same moment could both succeed (no row version on `MealStubs`) |

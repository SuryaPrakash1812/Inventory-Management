# Offline Testing — Manual Procedures

Covers every module that actually exists in the application today (Auth, Products,
Categories, Suppliers, Customers, Purchases, Inventory, Outbox). Sales and Stock
Adjustments are **not included** — they have database schema only, no working
functionality, so there is nothing to test.

For every test: **disconnect your machine from the internet, or simply don't start
`InventoryManagement.Api`**, before you begin. The whole point is proving the app
needs neither.

---

## 1. Fresh application startup

**Setup:** Rename or delete `%AppData%\InventoryManagement\inventory.db` (back it up
first if it has data you care about).

**Steps:** Launch the WPF app.

**Expected result:** No crash. A fresh `inventory.db` is created automatically, with
the full schema and seeded default roles/permissions/admin user.

**Verify:** Open `inventory.db` in DB Browser for SQLite -> confirm `Users`, `Roles`,
`Permissions` tables have rows.

---

## 2. Offline login

**Setup:** API not running.

**Steps:** Launch the app, log in with a valid username/password.

**Expected result:** Login succeeds with no delay or network-related error.

**Steps (negative case):** Log in with a wrong password.

**Expected result:** "Invalid username or password." No indication of whether the
username itself was valid (this is deliberate - prevents username enumeration).

**Database verification:** None needed - this is a pure local check against the
`Users` table's `PasswordHash` column.

---

## 3. Product management

**Steps:** Create a product, edit it, search for it by SKU/name, filter by category,
deactivate it.

**Expected result:** Every action completes instantly with no network activity.

**Database verification:**
```sql
SELECT * FROM "Products" ORDER BY "CreatedAtUtc" DESC LIMIT 1;
```

---

## 4. Supplier management

**Steps:** Create a supplier, edit it, view its purchase history tab, deactivate it.

**Expected result:** Works fully offline.

**Database verification:**
```sql
SELECT * FROM "Suppliers" ORDER BY "CreatedAtUtc" DESC LIMIT 1;
```

---

## 5. Customer management

**Steps:** Create a customer, edit it, search, deactivate.

**Expected result:** Works fully offline.

**Database verification:**
```sql
SELECT * FROM "Customers" ORDER BY "CreatedAtUtc" DESC LIMIT 1;
```

---

## 6. Purchase creation (Draft)

**Setup:** At least one active Supplier and Product must already exist.

**Steps:** New Purchase -> select supplier -> add one or more line items -> Save Draft.

**Expected result:** Purchase appears in the list with status Draft and a
`PO-{tag}-{sequence}` number (e.g. `PO-A1B2C3-00001`).

**Database verification:**
```sql
SELECT "Id", "PurchaseNumber", "Status", "TotalAmount" FROM "Purchases" ORDER BY "CreatedAtUtc" DESC LIMIT 1;
SELECT * FROM "PurchaseItems" WHERE "PurchaseId" = '<Id from above>';
SELECT * FROM "OutboxOperations" WHERE "EntityId" = '<Id from above>';
```
Expect exactly one `OutboxOperations` row, `OperationType = 'Purchase.Create'`,
`Status = 0` (Pending).

---

## 7. Purchase draft editing

**Steps:** Open the draft created above -> change quantity or add a line -> Save.

**Expected result:** Totals recompute correctly.

**Database verification:**
```sql
SELECT * FROM "OutboxOperations" WHERE "EntityId" = '<Id>' ORDER BY "CreatedAtUtc";
```
Expect **two** rows now: the original `Purchase.Create`, plus a new
`Purchase.Update`.

---

## 8. Purchase confirmation

**Setup:** Note the product's current stock level first (Products screen).

**Steps:** Open the draft -> Confirm.

**Expected result:** Status becomes Confirmed. Product's stock increases by the
purchased quantity.

**Database verification:**
```sql
SELECT "Status" FROM "Purchases" WHERE "Id" = '<Id>';
SELECT * FROM "StockMovements" WHERE "ReferenceId" = '<Id>';
SELECT "QuantityOnHand" FROM "Products" WHERE "Id" = '<ProductId>';
SELECT * FROM "OutboxOperations" WHERE "EntityId" = '<Id>' AND "OperationType" = 'Purchase.Confirm';
```

**Negative case:** Try confirming the same purchase again (if the UI allows
re-triggering it, e.g. via a repeated click before the screen refreshes).
**Expected result:** Rejected - "Purchase is already Confirmed and cannot be
confirmed again." Stock must NOT increase a second time.

---

## 9. Purchase cancellation

**Steps:** Create and confirm a second purchase, then Cancel it.

**Expected result:** Status becomes Cancelled. Stock decreases back by the same
quantity it was increased by on confirmation.

**Database verification:**
```sql
SELECT "Status" FROM "Purchases" WHERE "Id" = '<Id>';
SELECT * FROM "StockMovements" WHERE "ReferenceId" = '<Id>' ORDER BY "CreatedAtUtc";
-- Should show two rows: PurchaseReceipt (+qty) then PurchaseReturn (-qty)
SELECT * FROM "OutboxOperations" WHERE "EntityId" = '<Id>' AND "OperationType" = 'Purchase.Cancel';
```

**Negative case - insufficient stock to reverse:** Confirm a purchase, then use
some of that stock some other way that reduces it below the original purchased
quantity (currently, this would require another confirmed purchase's cancellation
consuming it, or a future Sales/Adjustment feature - if no such path exists yet,
this case cannot be exercised and should be noted as such rather than skipped
silently).
**Expected result if reachable:** Cancellation is rejected with a message
naming how much stock remains vs. how much would need to be reversed. No stock
movement is created; status stays Confirmed.

---

## 10. Payment status

**Steps:** Open a Confirmed purchase -> change Payment Status -> Save (explicit
button click - this does not autosave).

**Expected result:** Status updates.

**Database verification:**
```sql
SELECT "PaymentStatus" FROM "Purchases" WHERE "Id" = '<Id>';
SELECT * FROM "OutboxOperations" WHERE "EntityId" = '<Id>' AND "OperationType" = 'Purchase.SetPaymentStatus';
```

---

## 11. Inventory / stock movements

**Steps:** After the Confirm and Cancel tests above, review the product's stock
history (if a history view exists) or query directly.

**Database verification:**
```sql
SELECT "MovementType", "QuantityChange", "QuantityBalanceAfter", "CreatedAtUtc"
FROM "StockMovements" WHERE "ProductId" = '<ProductId>' ORDER BY "CreatedAtUtc";
```
Expect a coherent running balance - each `QuantityBalanceAfter` should equal the
previous one plus that row's `QuantityChange`.

---

## 12. Application restart persistence

**This is the most important test in this document.**

**Steps:**
1. Create a Purchase draft. Note its `PurchaseNumber`.
2. **Close the WPF application completely** (not just minimize).
3. Reopen it and log back in.
4. Navigate to Purchases and find that same draft.

**Expected result:** The draft still exists, with the same items and totals.

**Database verification (do this with the app closed, to avoid file locking):**
```sql
SELECT * FROM "Purchases" WHERE "PurchaseNumber" = '<the number you noted>';
SELECT * FROM "OutboxOperations" WHERE "EntityId" = '<Id>';
```
Both the Purchase and its Outbox row must survive the restart - this proves
persistence isn't relying on anything in-memory.

---

## 13. Outbox survives restart with correct status

Repeat test 12, but this time also confirm the Outbox row's exact fields:
```sql
SELECT "OperationType", "Status", "RetryCount", "PayloadJson" FROM "OutboxOperations"
WHERE "EntityId" = '<Id>';
```
`Status` should still read `0` (Pending) - nothing should have changed it just
from the app being closed and reopened, since nothing runs sync automatically
unless `Api:BaseAddress` is configured **and** the API is actually reachable.

---

## 14. Transaction rollback on validation failure

**Steps:** Try to save a Purchase draft with a **negative quantity** on one line
item (if the UI allows entering one - otherwise, this specific case may need to
be exercised via a unit test instead of the UI, and that's fine to note).

**Expected result:** The save is rejected with "Item quantity must be greater
than zero." No partial data is written.

**Database verification:**
```sql
SELECT COUNT(*) FROM "Purchases"; -- note the count before attempting
-- (attempt the invalid save)
SELECT COUNT(*) FROM "Purchases"; -- must be unchanged
SELECT COUNT(*) FROM "OutboxOperations"; -- must also be unchanged
```

---

## 15. Validation failures - reference data

**Steps:** Try to create a Purchase item referencing a Product that has since
been deleted (not really possible through the UI directly, since the dropdown
only shows existing products - this specific case is covered by an automated
test instead; see `PurchaseServiceTests`).

**Steps (reachable through the UI):** Try to save a Purchase with **no line
items at all**.

**Expected result:** Rejected - "At least one item is required."

---

## 16. Permission checks

**Setup:** Log in as a user whose role does NOT have permission for an action
you're about to try (e.g. a restricted role without Purchase-delete rights, if
your seeded roles distinguish this).

**Steps:** Attempt that restricted action.

**Expected result:** The action is blocked/hidden according to the user's actual
permissions - not silently allowed because the app is offline.

**Note:** If your currently seeded roles don't yet include a deliberately
restricted test role, create one via the Users/Roles screen first, assign a
test user to it with a known-missing permission, and use that for this test.

---

## 17. Search / filter / pagination

**Steps (any list screen - Products, Suppliers, Customers, Purchases):** Search
by a partial term, apply a status filter, sort a column, page forward and back.

**Expected result:** All operate correctly and instantly, entirely against local
SQLite.

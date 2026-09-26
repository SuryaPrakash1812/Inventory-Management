-- ============================================================
-- STEP 1: Run these against your SQLite database first
-- (DB Browser for SQLite, or any SQLite client)
-- File: %AppData%\InventoryManagement\inventory.db
-- ============================================================

SELECT "Id", "Name" FROM "Suppliers" WHERE "IsDeleted" = 0 LIMIT 1;

SELECT "Id", "Sku", "Name", "Brand", "CategoryId", "Unit",
       "CostPrice", "SellingPrice", "TaxPercentage", "ReorderLevel", "QuantityOnHand"
FROM "Products" WHERE "IsDeleted" = 0 LIMIT 1;

-- Take the CategoryId from the Product row above and run:
SELECT "Id", "Name" FROM "Categories" WHERE "Id" = '<CategoryId from above>';

-- ============================================================
-- STEP 2: Copy the exact values from Step 1 into the INSERTs
-- below, then run these against PostgreSQL (pgAdmin, inventory_dev)
--
-- Why this is needed: Postgres only ever received the SCHEMA via
-- migration, never any actual rows. Only Purchase creation has sync
-- support right now - Suppliers/Products/Categories were never synced.
-- The server correctly rejects any online purchase referencing a
-- supplier/product it has never heard of. This seeds ONE matching
-- row of each so you can test the online flow end-to-end; it is not
-- a permanent solution (a real Products/Suppliers sync stage is
-- separate, later work).
-- ============================================================

INSERT INTO "Categories" ("Id", "Name", "CreatedAtUtc", "IsDeleted")
VALUES ('<CategoryId from SQLite>', '<Category Name from SQLite>', NOW(), false);

INSERT INTO "Suppliers" ("Id", "Name", "IsActive", "CreatedAtUtc", "IsDeleted")
VALUES ('<Supplier Id from SQLite>', '<Supplier Name from SQLite>', true, NOW(), false);

INSERT INTO "Products"
    ("Id", "Sku", "Name", "Brand", "CategoryId", "Unit",
     "CostPrice", "SellingPrice", "TaxPercentage", "ReorderLevel", "QuantityOnHand",
     "IsActive", "CreatedAtUtc", "IsDeleted")
VALUES
    ('<Product Id from SQLite>', '<Sku>', '<Name>', '<Brand or NULL>', '<CategoryId>', '<Unit e.g. pcs>',
     <CostPrice>, <SellingPrice>, <TaxPercentage>, <ReorderLevel>, <QuantityOnHand>,
     true, NOW(), false);

-- ============================================================
-- STEP 3: Verify the seed worked
-- ============================================================
SELECT * FROM "Suppliers";
SELECT * FROM "Products";

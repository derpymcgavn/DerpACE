-- ================================================================================
-- DerpACE Database Cleanup Script
-- Removes duplicate property entries from biota tables
-- Database: ace_shard_test_retail
-- ================================================================================
-- INSTRUCTIONS FOR MySQL WORKBENCH:
-- 1. Open MySQL Workbench and connect to your server
-- 2. MAKE SURE YOU'RE CONNECTED TO THE CORRECT SERVER (not ace_auth_test_retail!)
-- 3. Copy and paste this entire script into a new SQL tab
-- 4. BACKUP YOUR DATABASE FIRST (Server > Data Export > ace_shard_test_retail)
-- 5. Execute the script (Lightning bolt icon or Ctrl+Shift+Enter)
-- ================================================================================

-- Force the correct database
USE ace_shard_test_retail;

-- ================================================================================
-- Step 1: Show current duplicates
-- ================================================================================
SELECT '=== STEP 1: Finding Duplicates ===' as Status;

SELECT 'biota_properties_int' as table_name, object_Id, type, COUNT(*) as duplicate_count
FROM biota_properties_int
GROUP BY object_Id, type
HAVING COUNT(*) > 1

UNION ALL

SELECT 'biota_properties_bool', object_Id, type, COUNT(*)
FROM biota_properties_bool
GROUP BY object_Id, type
HAVING COUNT(*) > 1

UNION ALL

SELECT 'biota_properties_float', object_Id, type, COUNT(*)
FROM biota_properties_float
GROUP BY object_Id, type
HAVING COUNT(*) > 1

UNION ALL

SELECT 'biota_properties_string', object_Id, type, COUNT(*)
FROM biota_properties_string
GROUP BY object_Id, type
HAVING COUNT(*) > 1

UNION ALL

SELECT 'biota_properties_d_i_d', object_Id, type, COUNT(*)
FROM biota_properties_d_i_d
GROUP BY object_Id, type
HAVING COUNT(*) > 1

UNION ALL

SELECT 'biota_properties_i_i_d', object_Id, type, COUNT(*)
FROM biota_properties_i_i_d
GROUP BY object_Id, type
HAVING COUNT(*) > 1

UNION ALL

SELECT 'biota_properties_int64', object_Id, type, COUNT(*)
FROM biota_properties_int64
GROUP BY object_Id, type
HAVING COUNT(*) > 1;

-- ================================================================================
-- Step 2: Create temporary tables to identify duplicates to keep
-- ================================================================================
SELECT '=== STEP 2: Identifying Duplicates to Remove ===' as Status;

-- For biota_properties_int: keep entries with MIN(value) for each (object_Id, type) pair
CREATE TEMPORARY TABLE IF NOT EXISTS temp_int_keep AS
SELECT object_Id, type, MIN(value) as value_to_keep
FROM biota_properties_int
GROUP BY object_Id, type
HAVING COUNT(*) > 1;

-- For biota_properties_bool
CREATE TEMPORARY TABLE IF NOT EXISTS temp_bool_keep AS
SELECT object_Id, type, MIN(value) as value_to_keep
FROM biota_properties_bool
GROUP BY object_Id, type
HAVING COUNT(*) > 1;

-- For biota_properties_float
CREATE TEMPORARY TABLE IF NOT EXISTS temp_float_keep AS
SELECT object_Id, type, MIN(value) as value_to_keep
FROM biota_properties_float
GROUP BY object_Id, type
HAVING COUNT(*) > 1;

-- For biota_properties_string
CREATE TEMPORARY TABLE IF NOT EXISTS temp_string_keep AS
SELECT object_Id, type, MIN(value) as value_to_keep
FROM biota_properties_string
GROUP BY object_Id, type
HAVING COUNT(*) > 1;

-- For biota_properties_d_i_d
CREATE TEMPORARY TABLE IF NOT EXISTS temp_did_keep AS
SELECT object_Id, type, MIN(value) as value_to_keep
FROM biota_properties_d_i_d
GROUP BY object_Id, type
HAVING COUNT(*) > 1;

-- For biota_properties_i_i_d
CREATE TEMPORARY TABLE IF NOT EXISTS temp_iid_keep AS
SELECT object_Id, type, MIN(value) as value_to_keep
FROM biota_properties_i_i_d
GROUP BY object_Id, type
HAVING COUNT(*) > 1;

-- For biota_properties_int64
CREATE TEMPORARY TABLE IF NOT EXISTS temp_int64_keep AS
SELECT object_Id, type, MIN(value) as value_to_keep
FROM biota_properties_int64
GROUP BY object_Id, type
HAVING COUNT(*) > 1;

-- ================================================================================
-- Step 3: Delete duplicates (keeps the entry with MIN value)
-- ================================================================================
SELECT '=== STEP 3: Removing Duplicates ===' as Status;

-- Delete from biota_properties_int
DELETE bpi FROM biota_properties_int bpi
INNER JOIN temp_int_keep tk ON bpi.object_Id = tk.object_Id AND bpi.type = tk.type
WHERE bpi.value != tk.value_to_keep;

SELECT CONCAT('Removed duplicates from biota_properties_int: ', ROW_COUNT(), ' rows') as Result;

-- Delete from biota_properties_bool
DELETE bpb FROM biota_properties_bool bpb
INNER JOIN temp_bool_keep tk ON bpb.object_Id = tk.object_Id AND bpb.type = tk.type
WHERE bpb.value != tk.value_to_keep;

SELECT CONCAT('Removed duplicates from biota_properties_bool: ', ROW_COUNT(), ' rows') as Result;

-- Delete from biota_properties_float
DELETE bpf FROM biota_properties_float bpf
INNER JOIN temp_float_keep tk ON bpf.object_Id = tk.object_Id AND bpf.type = tk.type
WHERE bpf.value != tk.value_to_keep;

SELECT CONCAT('Removed duplicates from biota_properties_float: ', ROW_COUNT(), ' rows') as Result;

-- Delete from biota_properties_string
DELETE bps FROM biota_properties_string bps
INNER JOIN temp_string_keep tk ON bps.object_Id = tk.object_Id AND bps.type = tk.type
WHERE bps.value != tk.value_to_keep;

SELECT CONCAT('Removed duplicates from biota_properties_string: ', ROW_COUNT(), ' rows') as Result;

-- Delete from biota_properties_d_i_d
DELETE bpd FROM biota_properties_d_i_d bpd
INNER JOIN temp_did_keep tk ON bpd.object_Id = tk.object_Id AND bpd.type = tk.type
WHERE bpd.value != tk.value_to_keep;

SELECT CONCAT('Removed duplicates from biota_properties_d_i_d: ', ROW_COUNT(), ' rows') as Result;

-- Delete from biota_properties_i_i_d
DELETE bpi FROM biota_properties_i_i_d bpi
INNER JOIN temp_iid_keep tk ON bpi.object_Id = tk.object_Id AND bpi.type = tk.type
WHERE bpi.value != tk.value_to_keep;

SELECT CONCAT('Removed duplicates from biota_properties_i_i_d: ', ROW_COUNT(), ' rows') as Result;

-- Delete from biota_properties_int64
DELETE bp64 FROM biota_properties_int64 bp64
INNER JOIN temp_int64_keep tk ON bp64.object_Id = tk.object_Id AND bp64.type = tk.type
WHERE bp64.value != tk.value_to_keep;

SELECT CONCAT('Removed duplicates from biota_properties_int64: ', ROW_COUNT(), ' rows') as Result;

-- Clean up temp tables
DROP TEMPORARY TABLE IF EXISTS temp_int_keep;
DROP TEMPORARY TABLE IF EXISTS temp_bool_keep;
DROP TEMPORARY TABLE IF EXISTS temp_float_keep;
DROP TEMPORARY TABLE IF EXISTS temp_string_keep;
DROP TEMPORARY TABLE IF EXISTS temp_did_keep;
DROP TEMPORARY TABLE IF EXISTS temp_iid_keep;
DROP TEMPORARY TABLE IF EXISTS temp_int64_keep;

-- ================================================================================
-- Step 4: Verify cleanup - should return no rows if successful
-- ================================================================================
SELECT '=== STEP 4: Verifying Cleanup ===' as Status;

SELECT 'biota_properties_int' as table_name, object_Id, type, COUNT(*) as remaining_duplicates
FROM biota_properties_int
GROUP BY object_Id, type
HAVING COUNT(*) > 1

UNION ALL

SELECT 'biota_properties_bool', object_Id, type, COUNT(*)
FROM biota_properties_bool
GROUP BY object_Id, type
HAVING COUNT(*) > 1

UNION ALL

SELECT 'biota_properties_float', object_Id, type, COUNT(*)
FROM biota_properties_float
GROUP BY object_Id, type
HAVING COUNT(*) > 1

UNION ALL

SELECT 'biota_properties_string', object_Id, type, COUNT(*)
FROM biota_properties_string
GROUP BY object_Id, type
HAVING COUNT(*) > 1

UNION ALL

SELECT 'biota_properties_d_i_d', object_Id, type, COUNT(*)
FROM biota_properties_d_i_d
GROUP BY object_Id, type
HAVING COUNT(*) > 1

UNION ALL

SELECT 'biota_properties_i_i_d', object_Id, type, COUNT(*)
FROM biota_properties_i_i_d
GROUP BY object_Id, type
HAVING COUNT(*) > 1

UNION ALL

SELECT 'biota_properties_int64', object_Id, type, COUNT(*)
FROM biota_properties_int64
GROUP BY object_Id, type
HAVING COUNT(*) > 1;

SELECT '=== Cleanup Complete! ===' as Status;
SELECT 'If Step 4 returned no rows, all duplicates have been successfully removed.' as Result;

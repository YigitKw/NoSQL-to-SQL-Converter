# NoSQL (JSON) -> SQL Converter

A Windows Forms desktop application that parses JSON documents and imports them into a relational SQLite database. It flattens nested objects into parent table columns and normalizes arrays into separate child tables with foreign key relationships.

---

## Requirements and Running

You need **.NET 9.0** installed on your system.

1. **Using Visual Studio:**
   - Open `240201025/Proje3.sln` in Visual Studio.
   - Press **Start (F5)** to build and run the application.

2. **Using the Command Line:**
   ```bash
   cd 240201025
   dotnet run
   ```

---

## How to Use

The main window contains top control buttons, a JSON hierarchy tree on the left, an SQL log panel on the bottom left, and table tabs with an SQL editor on the right.

1. **Select JSON:** Click `JSON Dosyası Seç` (Select JSON) on the top bar and choose a `.json` file.
   - The tree view on the left displays the structure of the JSON document.
   - The `Dönüştür` (Convert) button becomes active.
2. **Convert:** Click `Dönüştür`.
   - The app runs a schema discovery pass, creates the tables (`CREATE TABLE`), inserts records (`INSERT INTO`), and opens a tab for each table on the right.
   - You can monitor executed statements in the `SQL LOG` panel on the bottom left.
3. **Inspect and Query:**
   - Click through the tabs on the right to view populated rows in styled data grids.
   - Enter any query in the `SQL SORGUSU` text box and click `Çalıştır` (Run) to filter or inspect results.
4. **Reset:**
   - Click `Sıfırla` (Reset) to drop all user tables (`DropAllTables`) and clear the interface for a new file.

---

## Configuration and Tuning Guide

Below are the internal parameters you can modify or leave as default based on your needs:

### 1. Database File Location and Persistence (`_databaseFilePath` — `Form1.cs`)
* **Default Value:** `proje3.db` in the application base directory.
* **Persistence Behavior:** The application retains the existing `proje3.db` across sessions and loads existing tables upon launch. If the database file does not exist, a new one is initialized.
* **Resetting Database:** To clear all tables and reset the schema, click the `Sıfırla` (Reset) button on the top bar.
* **Changing File Path:** Update `_databaseFilePath` in `InitializeNewDatabase` to point to any custom path.

### 2. INSERT Statement Logging Threshold (`_persistedRecordLogCount` — `Form1.cs`)
* **Default Value:** The first `100` inserts are logged; subsequent ones are suppressed.
* **Why:** Writing every single insert statement to the `RichTextBox` slows down the UI thread on large files.
* **How to Change:** Increase the threshold in `_persistedRecordLogCount <= 100` inside `InsertRecordRecursive` if you want to view more statements in the log.

### 3. SQLite Pragmas (`DatabaseManager.cs`)
* **Settings:** `PRAGMA journal_mode = WAL;` and `PRAGMA synchronous = NORMAL;`
* **Why Keep Defaults:** SQLite locks the entire database file during writes by default. WAL (Write-Ahead Logging) mode prevents disk I/O bottlenecks during batch inserts. Unless the database resides on a network share (SMB), keep these settings.

### 4. System Keys (`_id` and `{ParentTable}__id`)
* **Why Keep Defaults:** Input JSON files can contain existing fields named `id` or `ID`. Using `_id` for surrogate primary keys and `__id` (double underscore) for foreign keys prevents naming collisions with original JSON data.

### 5. Grid Preview Row Limit (`LIMIT 500` — `Form1.cs`)
* **Default Value:** Tabular views in the right-side tabs preview the first `500` rows. The total row count is logged in the SQL log window (`SELECT COUNT(*)`).
* **Why This Limit Exists:** Binding tens of thousands of rows directly to WinForms `DataGridView` controls can cause memory spikes and UI freezing. All imported data remains fully intact in SQLite.
* **Accessing All Rows:** Run targeted SQL queries (e.g. `SELECT * FROM table WHERE ...`) in the SQL query editor on the top right to filter or paginate records.

---

## Technical Details and Complexity

### Architecture and Method Mapping

| Pipeline Step | Class & Method | Description |
|---|---|---|
| **Pass 1 (Schema Discovery)** | `JsonParser.WalkForSchemas()` | Scans the JSON tree without writing data. Determines tables, columns, and widest SQL data types using `EnsureColumn()` and `PromoteType()`. |
| **Pass 2 (Record Generation)** | `JsonParser.ProcessObject()` | Produces `FlatRecord` instances using the finalized schemas. |
| **Flattening** | `JsonParser.FlattenObject()` | Denormalizes 1:1 nested objects into `parent_child` columns on the parent table to avoid unnecessary JOIN costs. |
| **Normalization (1NF & 2NF)** | `JsonParser.ProcessArray()` | Normalizes 1:N arrays into child tables. Assigns surrogate `_id` and references `parent__id`. |
| **Prepared Statement Batching** | `DatabaseManager.InsertRecord()` | Reuses cached `SqliteCommand` instances per table from `_preparedInsertCommands`, updating parameters in place. |

### Time Complexity

Let **N** be the total count of JSON tokens/nodes in the document, and **M** be the total number of inserted rows:

1. **Schema Discovery (Pass 1):** Visits every token once -> O(N).
2. **Record Extraction (Pass 2):** Visits every token to extract values -> O(N).
3. **Database Insertion:** Inserts run inside a single transaction with prepared statements, taking amortized O(1) time per row -> O(M).

> **Total Time Complexity:** O(N + M) = O(N) (Linear). Runtime scales linearly with file size.

### Space Complexity

* **Memory Usage:** The full JSON tree is loaded into memory via `JToken.Parse`, giving O(N) space complexity.
* **Schema Metadata:** The schema dictionary stores table and column definitions O(T * C), which is negligible compared to data size.
* **Practical Impact:** A 15-20 MB JSON file typically uses around 60-90 MB of RAM, well within standard hardware limits.

---

## Test Files (`240201025/Samples/`)

The `240201025/Samples` folder includes three test cases:

* `simple.json`: Single-level object with primitive values.
* `nested.json`: Nested objects and an items array (order model).
* `complex.json`: Multi-level structure with departments, instructors, courses, and students.

---

## Dependencies

* [.NET 9.0](https://dotnet.microsoft.com/)
* [Newtonsoft.Json (13.0.4)](https://www.nuget.org/packages/Newtonsoft.Json/) — JSON parsing
* [Microsoft.Data.Sqlite (10.0.8)](https://www.nuget.org/packages/Microsoft.Data.Sqlite/) — SQLite ADO.NET provider

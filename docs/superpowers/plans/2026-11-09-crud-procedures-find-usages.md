# Generate CRUD Procedures + Find Usages — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** เพิ่มฟีเจอร์ 2 ข้อที่ผู้ใช้เลือกไว้: (7) สร้าง stored procedure ชุด CRUD จากตาราง และ (8) Find Usages ดูว่า
object ไหนอ้างถึงตาราง / view / procedure / function / คอลัมน์ / type นี้ ทั้งสองข้อเรียกจากเมนูคลิกขวาในหน้าต่าง Search

**Architecture:** logic ที่ test ได้อยู่ใน `SqlAssist.Core` (สร้าง script, รวม/คัดผล usages, จับคำในข้อความ) ส่วน
`SqlAssist.Vsix` มีแค่ query ไปที่ฐานข้อมูล, หน้าต่างตั้งค่า และการต่อเข้ากับ `SearchControl` โดยใช้ของที่มีอยู่แล้ว

**Tech Stack:** C# (netstandard2.0 Core / net48 Vsix, WPF), xUnit, `System.Data.SqlClient`

**Spec:** ไม่มีไฟล์ spec แยก ขอบเขตมาจากข้อเสนอข้อ 7, 8 ในบทสนทนาเมื่อ 2026-11-07 และการตัดสินใจในหัวข้อ "ค่าเริ่มต้นที่เลือกไว้" ด้านล่าง

## ก่อนเริ่ม (Task 0)

- [ ] งาน **Function Parameter Hints** ยังไม่ได้ commit (`src/SqlAssist.Core/Hints/`, `ParameterHintTip.cs`, test 2 ไฟล์ และไฟล์อื่นอีก 8 ไฟล์)
  ให้ commit แยกให้เสร็จก่อน (หรือให้ผู้ใช้ยืนยันว่าจะรวมไปด้วยกัน) จะได้ไม่ปนกับ diff ของงานนี้

## สิ่งที่มีอยู่แล้ว (อย่าเขียนซ้ำ)

- `SchemaCache.Instance.GetAsync(target, wait, token)` + `ConnectionTarget.ForDatabase(db)`: ได้ `SchemaModel` ของ database ใดก็ได้
  `DbColumn` มี `IsIdentity`, `IsComputed`, `IsPrimaryKey`, `IsNullable`, `TypeName` (format แล้ว เช่น `nvarchar(50)`)
- `StatementExpander`: `ParameterName(column, used)` (private → ทำเป็น `internal static` แล้วใช้ร่วม), เงื่อนไข "คอลัมน์ที่เขียนได้"
  (ข้าม identity / computed / `timestamp` / `rowversion`, บรรทัดประมาณ 290) และ `DefaultValue(typeName)` แยกเป็น helper ร่วมกันได้
- `TableScriptBuilder.Bracket(name)` ใส่ `[...]` แบบ SSMS
- `SearchControl`: เมนูคลิกขวาของแถวผลลัพธ์ (`rowMenu`, บรรทัดประมาณ 327), `SelectedItem`, `SetStatus`, `_server`, `_matcher`,
  preview แบบ `RichTextBox` ที่ highlight ช่วงคำได้, `ShowPreview(SearchMatch)`
- `SearchIndexCache`: index ของแต่ละ database มี `Definition` ของ view / procedure / function / trigger ครบอยู่ใน memory แล้ว
- `SearchMatcher` (whole word / match case) สำหรับหาตำแหน่งชื่อใน definition
- `SsmsNavigator.OpenQueryWindow(uiConnectionInfo, text)` และ `ScriptDelivery` (หน้าต่างใหม่ / clipboard / ไฟล์ ไม่รันให้)
- `DialogHost` สำหรับเปิด WPF window ที่มี SSMS เป็น owner (`UpdateScriptWindow` เป็นตัวอย่างหน้าต่างเลือกตัวเลือก)

## ค่าเริ่มต้นที่เลือกไว้ (ผู้ใช้เปลี่ยนได้ก่อนเริ่ม Task 1)

| เรื่อง | ค่าเริ่มต้น | เหตุผล |
|---|---|---|
| ชื่อ procedure | `usp_{Table}_{Action}` (`Select`, `Insert`, `Update`, `Delete`) แก้ pattern ได้ในหน้าต่าง CRUD และจำไว้ใน settings.xml | dbForge / ทีมส่วนใหญ่ใช้รูปนี้ |
| คำสั่งสร้าง | `CREATE OR ALTER PROCEDURE` (มีตัวเลือกเปลี่ยนเป็น `CREATE`) | รันซ้ำได้ ใช้ได้ตั้งแต่ SQL Server 2016 SP1 |
| Insert คืนค่า identity | มี `OUTPUT` parameter ของคอลัมน์ identity กำหนดค่าด้วย `SCOPE_IDENTITY()` | ผู้เรียกได้ key ใหม่โดยไม่ต้อง SELECT ซ้ำ |
| Select | `usp_X_Select` รับ PK ทุกตัวเป็น parameter แบบ `= NULL`; ถ้าส่ง NULL ครบจะได้ทุกแถว | procedure เดียวใช้ได้ทั้งดึงทั้งตารางและดึงทีละแถว |
| ตารางไม่มี PK | สร้างแค่ Insert + Select (ทุกแถว) แล้วใส่ comment บอกว่าข้าม Update / Delete เพราะไม่มี primary key | ไม่เดา key (ตรงกับหลักของ UPDATE script) |
| ส่งผลลัพธ์ | ผ่าน `ScriptDelivery` (หน้าต่าง query ใหม่เป็นค่าเริ่มต้น / clipboard / ไฟล์) **ไม่รัน script ให้** | ผู้ใช้ต้องอ่านก่อนรันเอง |
| UI ของ Find Usages | ใช้ตารางผลลัพธ์ของหน้าต่าง Search เดิม เปลี่ยนเป็นโหมด "Usages of dbo.X (n)" มีปุ่ม ← Back กลับไปผลค้นหาเดิม | ไม่ต้องสร้าง tool window ใหม่ และยังได้ preview + highlight ที่มีอยู่ |
| ขอบเขต database ของ Usages | database ของ object นั้นเท่านั้น (v1) | การอ้างข้าม database ต้องยิง query ทุก db ทำในรอบถัดไป |

## Global Constraints

- Exception ต้องไม่หลุดเข้า SSMS: ครอบด้วย `catch` แล้วเขียน `Log.Error(...)`; query ไปที่ฐานข้อมูลทำนอก UI thread แล้วค่อยกลับมา UI thread ด้วย `SwitchToMainThreadAsync`
- ห้ามรัน SQL ที่ generate ให้ผู้ใช้ (CRUD) เด็ดขาด: ส่งเป็นข้อความเท่านั้น
- query ของ Find Usages อ่านอย่างเดียวจาก catalog view, ส่งชื่อ object เป็น `SqlParameter` เสมอ (ห้ามต่อ string), `CommandTimeout` 30 วินาที
- ห้ามเขียนข้อความ SQL / definition ของผู้ใช้ลง log (log เฉพาะชื่อ object, จำนวนแถว, เวลา, exception)
- Comment ในโค้ดเป็นภาษาอังกฤษ ส่วน README / CHANGELOG เป็นภาษาไทย ใช้คำใน `CONTEXT.md` (Search Item, Definition, Match ฯลฯ)
  และเพิ่มคำใหม่ (`Usage`, `CRUD Procedure Set`) ลง `CONTEXT.md`
- ทุก task เพิ่มรายการใน `CHANGELOG.md` ใต้ `## [Unreleased]`
- ไม่ bump version และไม่ publish GitHub Release ระหว่าง task ทำที่ Task 6 หลังผู้ใช้ทดสอบใน SSMS แล้วเท่านั้น
- ไม่ใช้ปุ่มลัดใหม่ทั้งระบบ (เมนูคลิกขวาใน Search เท่านั้น) ปุ่มลัดภายในตาราง Search ใช้ได้ถ้าไม่ชนของเดิม (Enter, Ctrl+Enter, Ctrl+C):
  Find Usages = **Shift+F12** ภายในหน้าต่าง Search (ไม่ผูกกับ editor)

## Review Focus

1. **ชื่อที่ต้อง bracket / escape**: ตารางหรือคอลัมน์ชื่อมี space, `]`, ภาษาไทย, ขึ้นต้นด้วยตัวเลข → ชื่อ parameter ต้องเป็นตัวแปรที่ถูกต้อง (ใช้ `ParameterName` เดิม) และชื่อ object ต้อง bracket → test Task 1
2. **Composite PK และ PK ที่เป็น identity**: Update ต้องไม่ SET คอลัมน์ PK, Insert ต้องไม่ใส่ identity แต่คืนค่าผ่าน OUTPUT → test Task 1
3. **คอลัมน์ชนิดพิเศษ**: `rowversion`, computed, `sql_variant`, `xml`, `geography`, user-defined alias type, `nvarchar(max)` → ใช้ `TypeName` ตามที่ schema ให้มา ข้าม rowversion / computed ใน Insert / Update แต่ยังอยู่ใน Select
4. **Deferred name resolution**: procedure ที่อ้างตารางซึ่งสร้างทีหลัง `referenced_id` เป็น NULL → ต้องจับด้วย `referenced_schema_name` + `referenced_entity_name` ด้วย (ไม่ใส่ schema = จับเฉพาะชื่อ แล้วติดป้าย "ชื่อตรง ไม่ระบุ schema")
5. **Usages ของคอลัมน์**: `sys.sql_expression_dependencies.referenced_minor_id` มีค่าเฉพาะ schema-bound ดังนั้นใช้วิธีหา object ที่อ้างตารางแม่แล้วค้นชื่อคอลัมน์แบบ whole word ใน definition และติดป้ายว่า "พบจากข้อความ" (อาจเป็นคอลัมน์ชื่อเดียวกันของตารางอื่น) ต้องไม่อ้างว่าแม่นยำ 100%
6. **Dynamic SQL / การอ้างจากแอปพลิเคชัน**: หาไม่เจอ ต้องเขียนใน README ข้อจำกัด

---

### Task 1: Core — `CrudScriptBuilder` (ข้อ 7)

**Files:**
- Create: `src/SqlAssist.Core/Completion/CrudScriptBuilder.cs` (ใช้ `DbObject` จาก Schema และ helper ของ `StatementExpander`)
- Modify: `src/SqlAssist.Core/Completion/StatementExpander.cs` — เปิด `ParameterName` และเงื่อนไข writable column เป็น `internal static` (ไม่เปลี่ยนพฤติกรรม)
- Test: `tests/SqlAssist.Core.Tests/CrudScriptBuilderTests.cs`

API ที่จะได้:

```csharp
[Flags] public enum CrudActions { None = 0, Select = 1, Insert = 2, Update = 4, Delete = 8, All = 15 }

public sealed class CrudOptions
{
    public CrudActions Actions { get; set; } = CrudActions.All;
    public string NamePattern { get; set; } = "usp_{Table}_{Action}";   // {Schema}, {Table}, {Action}
    public bool CreateOrAlter { get; set; } = true;
    public string NewLine { get; set; } = "\r\n";
}

public sealed class CrudScript
{
    public string Text { get; }                         // all procedures, separated by GO
    public IReadOnlyList<string> Skipped { get; }       // e.g. "Update: no primary key"
}

public static class CrudScriptBuilder
{
    public static CrudScript Build(DbObject table, CrudOptions options);
    public static string ProcedureName(string pattern, string schema, string table, CrudActions action); // invalid chars -> "_"
}
```

รูปแบบที่ต้องได้ (ตาราง `dbo.Orders(OrderID int IDENTITY PK, CustomerID int NOT NULL, Note nvarchar(200) NULL)`):

```sql
CREATE OR ALTER PROCEDURE [dbo].[usp_Orders_Insert]
    @CustomerID int,
    @Note nvarchar(200) = NULL,
    @OrderID int OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    INSERT INTO [dbo].[Orders] ([CustomerID], [Note])
    VALUES (@CustomerID, @Note);

    SET @OrderID = SCOPE_IDENTITY();
END
GO
```

- [ ] **Step 1:** เขียน test ก่อน (ต้อง fail): Select/Insert/Update/Delete ของตารางด้านบน; composite PK 2 คอลัมน์ (WHERE เชื่อมด้วย AND, Update ไม่ SET คอลัมน์ PK); ตารางไม่มี PK (ได้แค่ Select + Insert และ `Skipped` มี Update/Delete); ตารางที่มีแต่ PK (Update ถูกข้ามเพราะไม่มีคอลัมน์ให้ SET); คอลัมน์ rowversion / computed (ไม่อยู่ใน Insert/Update แต่อยู่ใน Select); ชื่อคอลัมน์ `Order Date` และ `a]b` (bracket + parameter `@Order_Date`); `CreateOrAlter = false` ได้ `CREATE PROCEDURE`; `NamePattern` ที่มี `{Schema}`; view (`IsView`) → คืน null / throw `ArgumentException` (ไม่สร้าง CRUD ให้ view)
- [ ] **Step 2:** `dotnet test tests\SqlAssist.Core.Tests --filter "FullyQualifiedName~CrudScriptBuilderTests"` → fail
- [ ] **Step 3:** เขียน `CrudScriptBuilder` ให้ผ่าน: nullable column ใน Insert ได้ `= NULL` default, Select เรียงคอลัมน์ตามลำดับในตาราง และ `WHERE (@K IS NULL OR [K] = @K)` สำหรับแต่ละ PK, Delete/Update ใช้ PK ล้วน
- [ ] **Step 4:** test ผ่านทั้งหมด รวมถึง test เดิมของ `StatementExpander` (`dotnet test tests\SqlAssist.Core.Tests`)
- [ ] **Step 5:** CHANGELOG (ยังไม่ต้องบอกผู้ใช้จนกว่า Task 2 จะเสร็จ: ใส่ไว้ที่ Task 2)

### Task 2: Vsix — หน้าต่าง CRUD + เมนูใน Search (ข้อ 7)

**Files:**
- Create: `src/SqlAssist.Vsix/Search/CrudWindow.cs` (plain WPF `Window` เหมือน `UpdateScriptWindow`: checkbox 4 action, ช่อง name pattern พร้อมตัวอย่างชื่อจริงที่เปลี่ยนตามที่พิมพ์, checkbox `CREATE OR ALTER`, ปุ่ม New Query Window / Copy / Save…)
- Modify: `src/SqlAssist.Vsix/Search/SearchControl.cs` — เมนู "Generate CRUD procedures…" ใน `rowMenu` แสดง/เปิดใช้เฉพาะเมื่อ `SelectedItem.Type == Table` (ซ่อนผ่าน `ContextMenu.Opened`)
- Modify: `src/SqlAssist.Core/Settings/SqlAssistSettings.cs` + `SettingsProvider.cs` — จำ `CrudNamePattern` และ `CrudCreateOrAlter` ใน settings.xml
- Test: `tests/SqlAssist.Core.Tests/SettingsTests.cs` (อ่าน/เขียนค่าใหม่ และไฟล์เก่าที่ไม่มีค่านี้ได้ค่าเริ่มต้น)

- [ ] **Step 1:** test settings round-trip → fail → เพิ่ม property → ผ่าน
- [ ] **Step 2:** handler ของเมนู: บน UI thread เอา `_server.ForDatabase(item.Database)` → `SchemaCache.Instance.GetAsync(..., wait 15s)` นอก UI thread → หา `DbObject` จาก schema + ชื่อ (ไม่เจอ = `SetStatus("ไม่พบตาราง … ใน schema กด Refresh แล้วลองใหม่")`) → กลับ UI thread แล้วเปิด `CrudWindow` ผ่าน `DialogHost`
- [ ] **Step 3:** ปุ่มใน `CrudWindow` เรียก `CrudScriptBuilder.Build` แล้วส่งผ่าน `ScriptDelivery`; หน้าต่าง query ใหม่ใช้ connection ของ database นั้น (`USE [db]` นำหน้าแบบเดียวกับ Open in new query window); `Skipped` แสดงเป็นข้อความใต้ปุ่มและเป็น comment บนสุดของ script
- [ ] **Step 4:** render `CrudWindow` นอก SSMS เพื่อเช็ค layout (ตามแนวทางใน CLAUDE.md: ห้ามมี `ThrowIfNotOnUIThread` ใน constructor)
- [ ] **Step 5:** `dotnet build src\SqlAssist.Vsix -c Release` ผ่าน
- [ ] **Step 6:** CHANGELOG `### เพิ่ม`: "หน้าต่าง Search: คลิกขวาที่ตาราง › Generate CRUD procedures… สร้าง procedure Select / Insert / Update / Delete ตาม primary key ไม่รัน script ให้"

### Task 3: Core — `UsageModel` + การรวมผล (ข้อ 8)

**Files:**
- Create: `src/SqlAssist.Core/Search/Usages.cs`
- Test: `tests/SqlAssist.Core.Tests/UsageTests.cs`

```csharp
public enum UsageKind { Dependency, NameOnly, ForeignKey, Trigger, Synonym, TextMatch }

/// <summary>One object that refers to the Usage Target, and how we know.</summary>
public sealed class Usage
{
    public SearchItem Referrer { get; }          // the referencing view/proc/function/trigger/table(FK)/synonym
    public UsageKind Kind { get; }
    public IReadOnlyList<TextSpan> Hits { get; } // where the target's name appears in Referrer.PreviewText (for highlight)
}

public static class UsageFinder
{
    /// Merges raw rows from the database with the Search Index: one Usage per referrer (strongest Kind wins:
    /// Dependency > ForeignKey > Trigger > Synonym > NameOnly > TextMatch), attaches the referrer's Definition from
    /// the index and the hit spans (whole word, case-insensitive, bracketed or not: Orders, [Orders], dbo.Orders).
    public static IReadOnlyList<Usage> Merge(UsageTarget target, IEnumerable<RawUsage> rows, IReadOnlyList<SearchItem> index);

    /// For a column target: keep only referrers of the parent table whose definition names the column (TextMatch).
    public static IReadOnlyList<Usage> ForColumn(UsageTarget column, IReadOnlyList<Usage> parentUsages);
}
```

- [ ] **Step 1:** test ก่อน: แถวซ้ำของ object เดียวกัน (อ้างหลายที่) รวมเป็น 1; Kind ที่แรงกว่าชนะ; referrer ที่ไม่อยู่ใน index (เช่นถูก drop ระหว่างนั้น) ยังแสดงได้แต่ไม่มี preview; Hits หาเจอทั้ง `Orders`, `[Orders]`, `dbo.Orders`, `[dbo].[Orders]` แต่ไม่ใช่ `OrdersArchive`; คอลัมน์ `Note` เจอใน `SELECT o.Note` แต่ไม่เจอใน `-- note` ถ้าอยู่ใน comment? (ตัดสินใจ: **นับ** เพราะเป็นการค้นข้อความ ป้าย TextMatch บอกอยู่แล้ว) ; target เป็น object ที่ไม่มี referrer → list ว่าง
- [ ] **Step 2:** test fail → เขียนโค้ด → ผ่าน

### Task 4: Vsix — `UsageLoader` (query ฐานข้อมูล) (ข้อ 8)

**Files:**
- Create: `src/SqlAssist.Vsix/Search/UsageLoader.cs`

query เดียวหลาย result set (ทั้งหมดใช้ `@schema`, `@name` เป็น parameter):

```sql
DECLARE @id int = OBJECT_ID(QUOTENAME(@schema) + N'.' + QUOTENAME(@name));
-- 1. expression dependencies: by id, or by name when the reference was not resolved (deferred name resolution)
SELECT DISTINCT s.name, o.name, o.type, CASE WHEN d.referenced_id = @id THEN 0 ELSE 1 END AS name_only
FROM sys.sql_expression_dependencies d
JOIN sys.objects o ON o.object_id = d.referencing_id
JOIN sys.schemas s ON s.schema_id = o.schema_id
WHERE d.referenced_id = @id
   OR (d.referenced_id IS NULL AND d.referenced_database_name IS NULL AND d.referenced_entity_name = @name
       AND (d.referenced_schema_name IS NULL OR d.referenced_schema_name = @schema));
-- 2. foreign keys that point at the table
SELECT s.name, t.name, fk.name FROM sys.foreign_keys fk JOIN sys.tables t ON t.object_id = fk.parent_object_id
JOIN sys.schemas s ON s.schema_id = t.schema_id WHERE fk.referenced_object_id = @id;
-- 3. triggers on the table/view
SELECT s.name, tr.name FROM sys.triggers tr JOIN sys.objects o ON o.object_id = tr.parent_id
JOIN sys.schemas s ON s.schema_id = o.schema_id WHERE tr.parent_id = @id;
-- 4. synonyms
SELECT s.name, sy.name FROM sys.synonyms sy JOIN sys.schemas s ON s.schema_id = sy.schema_id
WHERE OBJECT_ID(sy.base_object_name) = @id;
```

- [ ] **Step 1:** `LoadAsync(ConnectionTarget, schema, name, CancellationToken) → IReadOnlyList<RawUsage>` รันนอก UI thread, timeout 30s, error → log + คืน exception ให้ผู้เรียกแสดงใน status
- [ ] **Step 2:** สำหรับ user-defined type ใช้ query อีกชุด (`sys.columns` / `sys.parameters` ที่ `user_type_id` ตรงกัน) เป็น `UsageKind.Dependency`
- [ ] **Step 3:** ทดสอบ query กับ AdventureWorks ด้วย `sqlcmd` หรือ SSMS (`Person.Person`, `dbo.ufnGetContactInformation`, คอลัมน์ `Person.BusinessEntityID`) จดจำนวนผลไว้ใน PR/commit message
- [ ] **Step 4:** `dotnet build src\SqlAssist.Vsix -c Release` ผ่าน

### Task 5: Vsix — โหมด Usages ในหน้าต่าง Search (ข้อ 8)

**Files:**
- Modify: `src/SqlAssist.Vsix/Search/SearchControl.cs`

- [ ] **Step 1:** เมนู "Find Usages" (`InputGestureText = "Shift+F12"`) ใน `rowMenu` เปิดใช้กับ Table, View, Procedure, Function, Column, UserDefinedType (ปิดสำหรับ Schema, Index, Constraint, Trigger)
- [ ] **Step 2:** ระหว่างโหลด: `SetStatus("Finding usages of dbo.X…")`, ยกเลิกได้ถ้าผู้ใช้ค้นใหม่ (ใช้ `CancellationTokenSource` แบบเดียวกับการค้น)
- [ ] **Step 3:** แสดงผล: แถบหัวเหนือตาราง "Usages of [dbo].[X] (n)" + ปุ่ม "← Back to results" (และ Esc / Backspace) ที่คืนผลค้นหาเดิมพร้อมแถวที่เลือกไว้; คอลัมน์ "Matched in" เปลี่ยนเป็น "How" (Dependency / Name only / Foreign key / Trigger / Synonym / Text); preview highlight จาก `Usage.Hits`; Enter / Ctrl+Enter / Copy name ทำงานเหมือนเดิมกับ referrer
- [ ] **Step 4:** ไม่มีผล = ข้อความ "ไม่พบ object ที่อ้างถึง (ไม่รวม dynamic SQL และการอ้างจาก database อื่น)"; search index ของ database ยังไม่โหลด = โหลดก่อน (ใช้ `SearchIndexCache` ที่มี) แล้วค่อย merge
- [ ] **Step 5:** `dotnet build` ผ่าน; ส่ง `ssms-safety-reviewer` agent ตรวจ diff ของ Vsix ทั้ง Task 2, 4, 5
- [ ] **Step 6:** CHANGELOG `### เพิ่ม`: "หน้าต่าง Search: คลิกขวา › Find Usages (Shift+F12) ดู view / procedure / function / trigger / foreign key / synonym ที่อ้างถึง object นั้น"

### Task 6: เอกสาร + ตรวจทั้งหมดก่อนปล่อย

- [ ] **Step 1:** README หัวข้อ "ค้นหา object": เพิ่ม Generate CRUD procedures และ Find Usages; หัวข้อ "ข้อจำกัด": Usages ไม่รวม dynamic SQL, การอ้างจาก database อื่น และการอ้างจากแอปพลิเคชัน; usages ของคอลัมน์เป็นการค้นข้อความ; CRUD ใช้ `CREATE OR ALTER` ต้อง SQL Server 2016 SP1 ขึ้นไป
- [ ] **Step 2:** `CLAUDE.md` (ส่วน `Search/` ของ Architecture) และ `CONTEXT.md` (คำว่า Usage, Usage Target, CRUD Procedure Set)
- [ ] **Step 3:** `dotnet test tests\SqlAssist.Core.Tests` ผ่านทั้งหมด, `.\build-setup.ps1` ผ่าน
- [ ] **Step 4:** ผู้ใช้ทดสอบใน SSMS 22 (checklist): CRUD ของตาราง identity / composite PK / ไม่มี PK แล้วรัน script ที่ได้บน database ทดสอบว่า compile ผ่าน; Find Usages ของตาราง, view, procedure, function, คอลัมน์, table type; ปุ่ม Back; Shift+F12; ตาราง Search ในหลาย database
- [ ] **Step 5:** หลังผู้ใช้ยืนยันเท่านั้น: bump version ใน `Directory.Build.props` (1.6.0), เปลี่ยนหัว `[Unreleased]` ใน CHANGELOG, commit + push; GitHub Release รอคำสั่งผู้ใช้ (`publish-release.ps1`)

# Explain / Review SQL (AI) — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** เลือก SQL ในหน้า query แล้วสั่ง **Explain SQL** ได้คำอธิบายภาษาไทยว่า query ทำอะไร พร้อมรายการข้อสังเกต (จุดที่น่าจะผิดหรือช้า)
ที่คลิกแล้วกระโดดไปบรรทัดนั้น โดยส่งออกไปเฉพาะข้อความ SQL กับ schema ของตารางที่ query อ้างถึง

**Architecture:** ทุกอย่างที่ตัดสินว่า "ส่งอะไรออกไป" และ "อ่านคำตอบอย่างไร" อยู่ใน `SqlAssist.Core/Ai/` (แทนค่า literal, สรุป schema,
ประกอบ request, แปลง response) จึง test ได้โดยไม่แตะเครือข่าย ส่วน `SqlAssist.Vsix/Ai/` มีแค่ HTTP, ที่เก็บ API key, หน้าต่างยินยอม และหน้าต่างผลลัพธ์
ที่ใช้รูปแบบเดียวกับ `RiskWindow`

**Tech Stack:** C# (netstandard2.0 Core / net48 Vsix, WPF), xUnit, `HttpClient`, `DataContractJsonSerializer` (ตัวเดียวกับ `ReleaseInfo`),
Windows DPAPI (`ProtectedData`), Claude Messages API (`POST /v1/messages`)

**Spec:** ไม่มีไฟล์ spec แยก ขอบเขตมาจากข้อเสนอข้อ 18 และคำอธิบายเพิ่มเติมในบทสนทนาเมื่อ 2026-11-14 และตาราง "ค่าเริ่มต้นที่เลือกไว้" ด้านล่าง

## สิ่งที่มีอยู่แล้ว (อย่าเขียนซ้ำ)

- `ActiveSqlEditor.Find()` / `ActiveSqlEditor.RunScope(view, out isSelection)`: ได้ `SnapshotSpan` ของส่วนที่เลือก หรือทั้งหน้า
- `RiskCheckCommand` + `RiskWindow`: รูปแบบคำสั่งที่อ่านข้อความแล้วเปิดหน้าต่างรายการ คลิกแถวแล้ว `Jump` ไปที่ editor (ดู `RiskWindow.Jump`, `FocusEditor`)
- `SqlTokenizer.Tokenize(text)` → `List<SqlToken>` (`Kind`: `Word`, `QuotedIdentifier`, `Variable`, `String`, `Number`, `Symbol`, `Comment`; `Start`, `Length`, `Text`, `Value`)
- `SchemaCache.Instance.TryGetLoaded(target)` → `SchemaModel` ที่โหลดไว้แล้ว (ไม่ยิง query); `SchemaModel.Find(schema, name)`, `ForeignKeysFrom(obj)`,
  `DbColumn` มี `TypeName`, `IsNullable`, `IsIdentity`, `IsPrimaryKey`, `Description`
- `EditorConnection.Get(window)`: connection ของหน้า query (ตาม `USE` เหนือ caret)
- `SqlAssistSettings.Parse` / `ToXml` (+ helper `Flag`, `Child`, `Attribute`), `SettingsProvider.Load()` / `Save()`, `OptionsWindow` (แท็บสร้างใน `BuildLayout` บรรทัด 134–137)
- `UpdateService.CreateClient()` (บรรทัด 294): ตั้ง TLS 1.2 ก่อนสร้าง `HttpClient` ให้ทำแบบเดียวกัน
- `ReleaseInfo` (บรรทัด 45, 70–80): ตัวอย่างอ่าน JSON ด้วย `DataContractJsonSerializer` + `[DataContract]`
- `DialogHost.SetOwner` / `ShowDialog`, `ScriptDelivery.Status` / `Show`, `Log.Info` / `Log.Error`
- `.vsct`: `cmdCheckRisks` (0x010A, `SubmenuGroup`) และ `cmdFormatSqlContext` (0x010B, `EditorContextGroup`) เป็นตัวอย่างปุ่มทั้งสองที่; `ProvideMenuResource("Menus.ctmenu", 13)`

## ค่าเริ่มต้นที่เลือกไว้ (ผู้ใช้เปลี่ยนได้ก่อนเริ่ม Task 1)

| เรื่อง | ค่าเริ่มต้น | เหตุผล |
|---|---|---|
| Provider | Claude API ของ Anthropic: `https://api.anthropic.com/v1/messages` (แก้ endpoint ได้ใน Options) | **ต้องให้ผู้ใช้ยืนยัน** ถ้าบริษัทบังคับให้ข้อมูลอยู่ใน Azure tenant ต้องเปลี่ยนเป็น Claude บน Microsoft Foundry ซึ่ง endpoint และ auth ต่างกัน (เกินขอบเขตแผนนี้) |
| Model | `claude-opus-5-5` (แก้ได้ใน Options) | ค่าเริ่มต้นที่แนะนำของ Claude API ถ้าต้องการถูกลงเปลี่ยนเป็น `claude-haiku-5-5` ได้เองใน Options |
| วิธีเรียก | `HttpClient` + JSON เอง ไม่ใช้ NuGet `Anthropic` | ปกติควรใช้ SDK ทางการ แต่ SqlAssist ส่งเป็น DLL เดียวและโหลดอยู่ใน process ของ SSMS การแนบ SDK กับ dependency ของมันเสี่ยงชนกับ assembly ที่ SSMS โหลดไว้ **ผู้ใช้เลือกใช้ SDK แทนได้** แต่ต้องทดสอบการโหลดใน SSMS จริงก่อน |
| สถานะเริ่มต้น | ปิด (`Enabled=false`) และไม่มี key | ไม่มีข้อมูลออกจากเครื่องจนกว่าผู้ใช้เปิดเอง |
| API key | ผู้ใช้ใส่ใน Options เก็บที่ `%LOCALAPPDATA%\SqlAssist\ai.key` เข้ารหัสด้วย DPAPI (`DataProtectionScope.CurrentUser`) | ไม่อยู่ใน `settings.xml` (ที่อาจถูกคัดลอกไปเครื่องอื่น) และไม่ลง log |
| แทนค่า literal | เปิดเป็นค่าเริ่มต้น: string → `'?'`, number → `0` | SQL อาจมีชื่อลูกค้า / เลขบัตรฝังอยู่ |
| Comment ใน SQL | ส่งไปตามเดิม | ช่วยให้อธิบายได้ตรง แต่ comment อาจมีข้อมูลจริง: ต้องเขียนบอกในหน้าต่างยินยอม |
| Schema ที่ส่ง | เฉพาะตาราง / view ที่ชื่อปรากฏใน SQL และหาเจอใน schema ที่โหลดไว้แล้ว สูงสุด 30 ตาราง | ไม่ส่งทั้ง database และไม่ยิง query เพิ่ม |
| MS_Description | ส่งด้วย (ปิดได้ใน Options) | ช่วยให้คำอธิบายใช้ศัพท์ของระบบ |
| สิ่งที่ไม่ส่งเด็ดขาด | ข้อมูลในตาราง, ผลลัพธ์ใน grid, ชื่อ server, ชื่อ database, login, connection string | |
| ขนาด SQL สูงสุด | 20,000 ตัวอักษร เกินแล้วบอกให้เลือกเฉพาะส่วน | ห้ามตัดข้อความเงียบ ๆ |
| การยินยอม | ครั้งแรกแสดงหน้าต่างพร้อมข้อความที่จะส่งจริงทั้งก้อน หลังจากนั้นมีปุ่ม "ดูสิ่งที่ส่ง" ในหน้าต่างผลลัพธ์ | |
| UI ผลลัพธ์ | `ExplainWindow` เป็น `Window` ธรรมดาแบบ `RiskWindow` (ไม่ใช่ tool window) | ใช้ของเดิมได้ และ render นอก SSMS เพื่อตรวจ layout ได้ |
| ปุ่มลัด | ไม่มี (เมนู Tools › SqlAssist และคลิกขวาใน editor) | ไม่ต้องตรวจการชนของ chord |
| ภาษาคำตอบ | ไทย คงคำศัพท์ SQL เป็นอังกฤษ | |
| Request | ไม่ stream, `max_tokens` 16000, `output_config.effort` = `"medium"`, timeout 120 วินาที, ยกเลิกได้ | คำตอบสั้น ไม่ต้อง stream |
| Refusal | ส่ง `fallbacks: "default"` พร้อม header `anthropic-beta: server-side-fallback-2026-07-01` และตรวจ `stop_reason` ก่อนอ่าน `content` | |

## Global Constraints

- Exception ต้องไม่หลุดเข้า SSMS: ครอบด้วย `catch` แล้ว `Log.Error(...)`; HTTP ทำนอก UI thread แล้วกลับด้วย `SwitchToMainThreadAsync`
- **ห้ามเขียนลง log:** ข้อความ SQL, schema ที่ส่ง, คำตอบของ model, API key, header ของ request ให้ log ได้แค่: จำนวนตัวอักษร, จำนวนตาราง, HTTP status, เวลา, ชนิด exception
- ไม่มี request ใดออกจากเครื่องถ้า `Enabled=false`, ไม่มี key หรือผู้ใช้ยังไม่กดยินยอม
- ข้อความที่แสดงในหน้าต่างยินยอมต้องเป็น **string เดียวกันทุก byte** กับ body ที่ส่งจริง (สร้างครั้งเดียว ใช้สองที่)
- API key ไปใน header `x-api-key` เท่านั้น; header อื่น: `anthropic-version: 2026-10-21`, `content-type: application/json`
- ไม่ส่ง `temperature` / `top_p` / `top_k` และไม่ส่ง `thinking` (model รุ่นนี้ตอบ 400)
- ข้อความใน UI ต้องบอกว่าข้อสังเกตเป็น **คำแนะนำจากการอ่านข้อความ SQL** ไม่ได้ดู execution plan หรือสถิติจริง
- Comment ในโค้ดเป็นภาษาอังกฤษ; README / CHANGELOG เป็นภาษาไทย; ทุก task เพิ่มรายการใน `CHANGELOG.md` ใต้ `## [Unreleased]`
- `.vsct` เปลี่ยน → `ProvideMenuResource` เป็น 14
- ไม่ bump version และไม่ publish GitHub Release จนกว่าผู้ใช้ทดสอบใน SSMS แล้ว (Task 8)

## Review Focus

1. **Literal หลายบรรทัด:** string ที่มีขึ้นบรรทัดใหม่ข้างใน ถ้าแทนด้วย `'?'` เฉย ๆ เลขบรรทัดที่ model ตอบจะเลื่อน → masker ต้องคงจำนวนบรรทัด (test Task 2)
2. **ชื่อที่ดูเหมือน literal:** `[Order Details]`, `"quoted"`, `N'...'`, `0x1F`, `1e5`, `$1.50`, ตัวเลขใน `TOP 10` / `varchar(50)` → identifier ต้องไม่ถูกแทน; ตัวเลขในชนิดข้อมูลและ `TOP` ถูกแทนได้ (ยอมรับ) แต่ต้องไม่ทำให้ tokenizer พัง (test Task 2)
3. **Schema ยังไม่โหลด / ตารางข้าม database / temp table / CTE:** ต้องส่ง SQL ได้โดยมี schema ว่าง ไม่ throw และบอกผู้ใช้ว่า "ไม่มี schema ประกอบ" (test Task 3)
4. **คำตอบที่ไม่ใช่ JSON ตามรูป:** HTTP 401 / 429 / 5xx, `stop_reason` = `refusal` / `max_tokens`, body ว่าง, JSON ถูกแต่ `line` เกินจำนวนบรรทัดหรือเป็น 0 → ต้องได้ข้อความบอกสาเหตุ ไม่ crash และ `line` ที่ใช้ไม่ได้กลายเป็น "ไม่ระบุบรรทัด" (test Task 4)
5. **ส่วนที่เลือกไม่ได้เริ่มที่บรรทัด 1:** เลขบรรทัดจาก model นับจากข้อความที่ส่ง ต้องบวก offset ของ selection ก่อน Jump (test Task 4 ฝั่ง Core: `ExplainFinding.Line` เป็น 1-based ในข้อความที่ส่ง; Task 7 บวก offset)
6. **ผู้ใช้แก้ SQL ระหว่างรอคำตอบ:** Jump ต้องใช้ tracking span จาก snapshot ตอนส่ง (แบบเดียวกับ `RiskWindow`) ไม่ใช่เลขบรรทัดดิบ

---

### Task 1: `AiOptions` ใน settings

**Files:**
- Create: `src/SqlAssist.Core/Settings/AiOptions.cs`
- Modify: `src/SqlAssist.Core/Settings/SqlAssistSettings.cs` (property `Ai`, `Parse` หลังบล็อก `Results`, `ToXml`)
- Test: `tests/SqlAssist.Core.Tests/SettingsTests.cs`

**Interfaces:**
- Produces: `SqlAssistSettings.Ai` ชนิด `AiOptions` มี `bool Enabled` (false), `string Endpoint` (`AiOptions.DefaultEndpoint` = `"https://api.anthropic.com/v1/messages"`),
  `string Model` (`AiOptions.DefaultModel` = `"claude-opus-5-5"`), `bool MaskLiterals` (true), `bool SendDescriptions` (true), `bool ConsentGiven` (false)
  XML: `<Ai Enabled="" Endpoint="" Model="" MaskLiterals="" SendDescriptions="" ConsentGiven="" />`

- [ ] **Step 1: เขียน test ที่ยังไม่ผ่าน**

```csharp
[Fact]
public void Ai_defaults_are_off_and_masking()
{
    var s = SqlAssistSettings.Parse("<SqlAssistSettings />");
    Assert.False(s.Ai.Enabled);
    Assert.False(s.Ai.ConsentGiven);
    Assert.True(s.Ai.MaskLiterals);
    Assert.True(s.Ai.SendDescriptions);
    Assert.Equal("https://api.anthropic.com/v1/messages", s.Ai.Endpoint);
    Assert.Equal("claude-opus-5-5", s.Ai.Model);
}

[Fact]
public void Ai_round_trips_and_ignores_bad_endpoint()
{
    var s = new SqlAssistSettings();
    s.Ai.Enabled = true; s.Ai.Model = "claude-haiku-5-5"; s.Ai.MaskLiterals = false; s.Ai.ConsentGiven = true;
    var back = SqlAssistSettings.Parse(s.ToXml());
    Assert.True(back.Ai.Enabled);
    Assert.Equal("claude-haiku-5-5", back.Ai.Model);
    Assert.False(back.Ai.MaskLiterals);
    Assert.True(back.Ai.ConsentGiven);

    var bad = SqlAssistSettings.Parse("<SqlAssistSettings><Ai Endpoint=\"http://example.com/x\" Model=\"  \" /></SqlAssistSettings>");
    Assert.Equal(AiOptions.DefaultEndpoint, bad.Ai.Endpoint);   // only absolute https:// is accepted
    Assert.Equal(AiOptions.DefaultModel, bad.Ai.Model);
}
```

- [ ] **Step 2:** `dotnet test tests\SqlAssist.Core.Tests --filter "FullyQualifiedName~SettingsTests.Ai_"` → FAIL (ไม่มี `Ai`)
- [ ] **Step 3:** เขียน `AiOptions` และต่อเข้ากับ `Parse` / `ToXml` endpoint รับเฉพาะ absolute URI ที่ scheme เป็น `https`; model ว่าง = ค่าเริ่มต้น
- [ ] **Step 4:** รัน filter เดิม → PASS แล้วรัน `SettingsTests` ทั้ง class → PASS
- [ ] **Step 5:** Commit `Settings: AI options (off by default)`

### Task 2: `LiteralMasker`

**Files:**
- Create: `src/SqlAssist.Core/Ai/LiteralMasker.cs`
- Test: `tests/SqlAssist.Core.Tests/LiteralMaskerTests.cs`

**Interfaces:**
- Produces: `public static string LiteralMasker.Mask(string sql)` — token ชนิด `String` → `'?'`, ชนิด `Number` → `0`, อย่างอื่นคัดลอกตามเดิมทุกตัวอักษร
  (รวมช่องว่างและ comment) ถ้า token ที่ถูกแทนมีขึ้นบรรทัดใหม่ n ครั้ง ให้ต่อ `\n` n ตัวหลังตัวแทน จำนวนบรรทัดของผลลัพธ์จึงเท่ากับต้นฉบับเสมอ

- [ ] **Step 1: เขียน test ที่ยังไม่ผ่าน**

```csharp
[Theory]
[InlineData("SELECT * FROM t WHERE Name = N'สมชาย' AND Age > 30", "SELECT * FROM t WHERE Name = '?' AND Age > 0")]
[InlineData("WHERE Id IN (1, 22, 333)", "WHERE Id IN (0, 0, 0)")]
[InlineData("SELECT [Order 1], \"Col 2\" FROM [dbo].[T1]", "SELECT [Order 1], \"Col 2\" FROM [dbo].[T1]")]
[InlineData("SELECT 'it''s' -- keep 'this' 42", "SELECT '?' -- keep 'this' 42")]
[InlineData("SELECT 0x1F, 1e5, 1.50", "SELECT 0, 0, 0")]
[InlineData("SELECT 'unterminated", "SELECT '?'")]
[InlineData("", "")]
public void Masks_literals_only(string sql, string expected) => Assert.Equal(expected, LiteralMasker.Mask(sql));

[Fact]
public void Keeps_line_count_when_a_string_spans_lines()
{
    string sql = "SELECT 'a\r\nb\r\nc'\r\nFROM t";
    string masked = LiteralMasker.Mask(sql);
    Assert.Equal(sql.Split('\n').Length, masked.Split('\n').Length);
    Assert.DoesNotContain("a", masked.Substring(0, masked.IndexOf("FROM")));
    Assert.EndsWith("FROM t", masked);
}

[Fact]
public void Null_is_empty() => Assert.Equal("", LiteralMasker.Mask(null));
```

ถ้า `SqlTokenizer` แยก `0x1F` / `1e5` / `1.50` ไม่เป็น `Number` token เดียว ให้แก้ค่าที่คาดหวังของ case นั้นให้ตรงกับที่ tokenizer ให้จริง
โดยเงื่อนไขที่ห้ามผ่อนคือ **ตัวเลขและตัวอักษรใน literal ต้องไม่เหลือในผลลัพธ์** (อย่าแก้ tokenizer ใน task นี้ เพราะ completion ใช้อยู่)

- [ ] **Step 2:** `dotnet test tests\SqlAssist.Core.Tests --filter "FullyQualifiedName~LiteralMaskerTests"` → FAIL
- [ ] **Step 3:** เขียน `Mask` โดยเดิน token จาก `SqlTokenizer.Tokenize` แล้วคัดลอกช่วงระหว่าง token จากต้นฉบับ
- [ ] **Step 4:** รัน filter เดิม → PASS
- [ ] **Step 5:** Commit `Ai: mask string and number literals before sending SQL`

### Task 3: `SchemaDigest`

**Files:**
- Create: `src/SqlAssist.Core/Ai/SchemaDigest.cs`
- Test: `tests/SqlAssist.Core.Tests/SchemaDigestTests.cs` (ใช้ `TestSchema` ที่มีอยู่)

**Interfaces:**
- Produces:
  - `public sealed class SchemaDigest { public string Text { get; } public int TableCount { get; } public bool Truncated { get; } }`
  - `public static SchemaDigest SchemaDigest.Build(string sql, SchemaModel schema, bool includeDescriptions, int maxTables = 30)`
- วิธีหาตาราง: เดิน token ที่ `IsName` และไม่ใช่ `Variable`; ถ้า token ก่อนหน้าเป็น `.` และก่อนนั้นเป็นชื่อ ให้ใช้เป็น schema; เรียก `schema.Find(schemaOrNull, name)`
  เจอแล้วเก็บ (ไม่ซ้ำ ตามลำดับที่เจอ) ชื่อคอลัมน์ที่บังเอิญตรงกับชื่อตารางจะถูกรวมด้วย ซึ่งยอมรับได้
- รูปแบบ 1 บรรทัดต่อ 1 object (ขึ้นบรรทัดใหม่ด้วย `\n`):
  `dbo.Orders (table): OrderId int NOT NULL PK IDENTITY, CustomerId int NOT NULL -> dbo.Customers.CustomerId, Note nvarchar(200) NULL -- หมายเหตุ`
  `-> x.y.z` มาจาก `ForeignKeysFrom`; ` -- คำอธิบาย` ใส่เฉพาะเมื่อ `includeDescriptions` และ `Description` ไม่ว่าง (ตัดขึ้นบรรทัดใหม่ในคำอธิบายเป็นช่องว่าง)
- `schema` เป็น null หรือไม่เจอตารางเลย → `Text == ""`, `TableCount == 0`

- [ ] **Step 1: เขียน test ที่ยังไม่ผ่าน** (ปรับชื่อตารางให้ตรงกับที่ `TestSchema` มี)

```csharp
[Fact]
public void Lists_only_tables_named_in_the_sql()
{
    var d = SchemaDigest.Build("SELECT * FROM dbo.Orders o JOIN [Customers] c ON c.CustomerId = o.CustomerId", TestSchema.Build(), true);
    Assert.Equal(2, d.TableCount);
    Assert.Contains("dbo.Orders (table):", d.Text);
    Assert.Contains("dbo.Customers (table):", d.Text);
    Assert.Equal(2, d.Text.Split('\n').Length);
    Assert.Contains("CustomerID int NULL -> dbo.Customers.CustomerID", d.Text);
}

[Fact]
public void Unknown_tables_null_schema_and_names_in_strings_give_nothing()
{
    Assert.Equal("", SchemaDigest.Build("SELECT * FROM #tmp JOIN other.dbo.Nope n ON 1 = 1", TestSchema.Build(), true).Text);
    Assert.Equal(0, SchemaDigest.Build("SELECT * FROM dbo.Orders", null, true).TableCount);
    Assert.Equal(0, SchemaDigest.Build("SELECT 'Orders' -- Orders", TestSchema.Build(), true).TableCount);
}

[Fact]
public void Stops_at_max_tables_and_says_so()
{
    var d = SchemaDigest.Build("SELECT * FROM dbo.Orders, dbo.Customers", TestSchema.Build(), true, maxTables: 1);
    Assert.Equal(1, d.TableCount);
    Assert.True(d.Truncated);
}

[Fact]
public void Descriptions_can_be_left_out()
{
    string with = SchemaDigest.Build("SELECT * FROM dbo.Orders", TestSchema.Build(), true).Text;
    string without = SchemaDigest.Build("SELECT * FROM dbo.Orders", TestSchema.Build(), false).Text;
    Assert.DoesNotContain(" -- ", without);
    Assert.True(with.Length >= without.Length);
}
```

- [ ] **Step 2:** `dotnet test tests\SqlAssist.Core.Tests --filter "FullyQualifiedName~SchemaDigestTests"` → FAIL
- [ ] **Step 3:** เขียน `Build` ตาม Interfaces
- [ ] **Step 4:** รัน filter เดิม → PASS
- [ ] **Step 5:** Commit `Ai: schema digest of the tables a script names`

### Task 4: `ExplainRequest` และ `ExplainResponse`

**Files:**
- Create: `src/SqlAssist.Core/Ai/ExplainRequest.cs`, `src/SqlAssist.Core/Ai/ExplainResponse.cs`
- Test: `tests/SqlAssist.Core.Tests/ExplainRequestTests.cs`, `tests/SqlAssist.Core.Tests/ExplainResponseTests.cs`

**Interfaces:**
- Consumes: `LiteralMasker.Mask`, `SchemaDigest.Build`, `AiOptions`
- Produces:
  - `public sealed class ExplainRequest { public string Body { get; } public string SentSql { get; } public int TableCount { get; } public bool SchemaTruncated { get; } }`
  - `public const int ExplainRequest.MaxSqlChars = 20000;`
  - `public static ExplainRequest ExplainRequest.Build(string sql, SchemaModel schema, AiOptions options)` — `sql` ยาวเกิน `MaxSqlChars` → `ArgumentException`
    (ผู้เรียกตรวจก่อนและบอกผู้ใช้เอง) `Body` คือ JSON ที่จะส่ง และเป็นข้อความเดียวกับที่หน้าต่างยินยอมแสดง
  - `public enum FindingSeverity { Info, Warning }`
  - `public sealed class ExplainFinding { public int Line; public FindingSeverity Severity; public string Title; public string Detail; }` (`Line` 1-based ในข้อความที่ส่ง; 0 = ไม่ระบุ)
  - `public sealed class ExplainResult { public string Summary; public IReadOnlyList<ExplainFinding> Findings; public string Problem; }` (`Problem` ไม่ null = ใช้ไม่ได้ และเป็นข้อความภาษาไทยสำหรับแสดง)
  - `public static ExplainResult ExplainResponse.Parse(int httpStatus, string body, int sentLineCount)`

**Body ที่ต้องได้** (ลำดับ field ตามนี้ เพื่อให้ test เทียบได้):

```json
{
  "model": "<options.Model>",
  "max_tokens": 16000,
  "fallbacks": "default",
  "output_config": {
    "effort": "medium",
    "format": {
      "type": "json_schema",
      "schema": {
        "type": "object",
        "additionalProperties": false,
        "required": ["summary", "findings"],
        "properties": {
          "summary": { "type": "string" },
          "findings": {
            "type": "array",
            "items": {
              "type": "object",
              "additionalProperties": false,
              "required": ["line", "severity", "title", "detail"],
              "properties": {
                "line": { "type": "integer" },
                "severity": { "type": "string", "enum": ["info", "warning"] },
                "title": { "type": "string" },
                "detail": { "type": "string" }
              }
            }
          }
        }
      }
    }
  },
  "system": "<SystemPrompt>",
  "messages": [ { "role": "user", "content": "<UserContent>" } ]
}
```

`SystemPrompt` (ค่าคงที่ `ExplainRequest.SystemPrompt`):

```text
You review T-SQL for a developer working in SQL Server Management Studio. Answer in Thai; keep SQL keywords, object names and technical terms in English.
"summary": what the script does, step by step, in plain language (a short paragraph, or a few lines for a long script).
"findings": things that are likely wrong or likely slow, judged from the text alone. You cannot see data, statistics or execution plans, so describe a performance point as something to check, not as a measured fact. Report nothing when there is nothing worth the developer's time; do not pad the list with style remarks.
"line" is the 1-based line number in the SQL you were given where the finding starts, or 0 when it is about the whole script.
Literal values may have been replaced with '?' and 0 before the SQL was sent; do not report those placeholders as problems.
The schema lists only the tables the script names; a table missing from it is not an error.
```

`UserContent`: `"SQL:\n" + sentSql + "\n\nSchema:\n" + (digest.Text == "" ? "(none available)" : digest.Text)` โดย `sentSql` คือ `LiteralMasker.Mask(sql)` เมื่อ `options.MaskLiterals` ไม่เช่นนั้นคือ `sql`

**กฎของ `Parse`:**

| เงื่อนไข | ผลลัพธ์ |
|---|---|
| status 401 / 403 | `Problem` = "API key ไม่ถูกต้องหรือไม่มีสิทธิ์ (HTTP 401)" (ใส่ status จริง) |
| status 429 | `Problem` = "ถูกจำกัดจำนวนการเรียก ลองใหม่อีกครั้งภายหลัง (HTTP 429)" |
| status อื่นที่ไม่ใช่ 200 | `Problem` = "บริการ AI ตอบกลับผิดพลาด (HTTP n)" ต่อด้วย `error.message` จาก body ถ้าอ่านได้ ตัดที่ 300 ตัวอักษร |
| 200 และ `stop_reason` = `"refusal"` | `Problem` = "model ปฏิเสธคำขอนี้" |
| 200 และ `stop_reason` = `"max_tokens"` | `Problem` = "คำตอบยาวเกินกำหนด ให้เลือก SQL ให้สั้นลง" |
| 200 แต่ไม่มี block `type == "text"` หรือ text ไม่ใช่ JSON ตามรูป | `Problem` = "อ่านคำตอบจาก AI ไม่ได้" |
| 200 ปกติ | `Summary` + `Findings` จาก JSON ใน text block แรกที่ `type == "text"` (ข้าม block ชนิด `thinking` และอื่น ๆ); `line` ที่ < 1 หรือ > `sentLineCount` → 0; `severity` ที่ไม่รู้จัก → `Info` |

- [ ] **Step 1: ยืนยันรูป `output_config.format` และ `fallbacks`** เปิดหน้า Structured Outputs และ Refusal fallbacks ของเอกสาร Claude API
  (`https://platform.claude.com/docs`) เทียบกับ Body ข้างบน ถ้าชื่อ field ต่าง ให้แก้ Body ในแผนนี้และใน test ก่อนเขียนโค้ด และจดสิ่งที่แก้ไว้ใน commit message
- [ ] **Step 2: เขียน test ที่ยังไม่ผ่าน**

```csharp
// ExplainRequestTests
[Fact]
public void Body_carries_masked_sql_schema_and_no_identity_of_the_server()
{
    var options = new AiOptions();
    var r = ExplainRequest.Build("SELECT * FROM dbo.Orders WHERE Note = 'ลับ' AND Qty > 5", TestSchema.Build(), options);
    Assert.Equal("SELECT * FROM dbo.Orders WHERE Note = '?' AND Qty > 0", r.SentSql);
    Assert.DoesNotContain("ลับ", r.Body);
    Assert.Contains("\"model\":\"claude-opus-5-5\"", r.Body);
    Assert.Contains("\"max_tokens\":16000", r.Body);
    Assert.Contains("\"fallbacks\":\"default\"", r.Body);
    Assert.Contains("\"effort\":\"medium\"", r.Body);
    Assert.Contains("dbo.Orders (table):", r.Body);
    Assert.DoesNotContain("temperature", r.Body);
    Assert.DoesNotContain("\"thinking\"", r.Body);
    Assert.Equal(1, r.TableCount);
}

[Fact]
public void Masking_off_sends_the_sql_as_typed()
{
    var r = ExplainRequest.Build("SELECT 'x'", null, new AiOptions { MaskLiterals = false });
    Assert.Equal("SELECT 'x'", r.SentSql);
    Assert.Contains("(none available)", r.Body);
}

[Fact]
public void Body_is_valid_json_with_quotes_newlines_and_thai()
{
    var r = ExplainRequest.Build("SELECT \"a\\b\"\r\nFROM [ตาราง] -- คอมเมนต์", null, new AiOptions());
    using (var doc = System.Text.Json.JsonDocument.Parse(r.Body))   // the test project is net8.0; Core itself stays on DataContractJsonSerializer
    {
        string content = doc.RootElement.GetProperty("messages")[0].GetProperty("content").GetString();
        Assert.Contains("SELECT \"a\\b\"\r\nFROM [ตาราง] -- คอมเมนต์", content);
        Assert.Equal(16000, doc.RootElement.GetProperty("max_tokens").GetInt32());
    }
}

[Fact]
public void Too_long_is_rejected_not_truncated() =>
    Assert.Throws<ArgumentException>(() => ExplainRequest.Build(new string('x', ExplainRequest.MaxSqlChars + 1), null, new AiOptions()));

// ExplainResponseTests
private const string Ok = "{\"stop_reason\":\"end_turn\",\"content\":[{\"type\":\"thinking\",\"thinking\":\"\"},{\"type\":\"text\",\"text\":" +
    "\"{\\\"summary\\\":\\\"สรุป\\\",\\\"findings\\\":[{\\\"line\\\":2,\\\"severity\\\":\\\"warning\\\",\\\"title\\\":\\\"t\\\",\\\"detail\\\":\\\"d\\\"}," +
    "{\\\"line\\\":99,\\\"severity\\\":\\\"odd\\\",\\\"title\\\":\\\"u\\\",\\\"detail\\\":\\\"e\\\"}]}\"}]}";

[Fact]
public void Reads_summary_and_findings_and_clamps_lines()
{
    var r = ExplainResponse.Parse(200, Ok, sentLineCount: 3);
    Assert.Null(r.Problem);
    Assert.Equal("สรุป", r.Summary);
    Assert.Equal(2, r.Findings.Count);
    Assert.Equal(2, r.Findings[0].Line);
    Assert.Equal(FindingSeverity.Warning, r.Findings[0].Severity);
    Assert.Equal(0, r.Findings[1].Line);
    Assert.Equal(FindingSeverity.Info, r.Findings[1].Severity);
}

[Theory]
[InlineData(401, "{\"type\":\"error\",\"error\":{\"message\":\"invalid x-api-key\"}}", "API key")]
[InlineData(429, "{}", "429")]
[InlineData(500, "<html>", "500")]
[InlineData(200, "{\"stop_reason\":\"refusal\",\"content\":[]}", "ปฏิเสธ")]
[InlineData(200, "{\"stop_reason\":\"max_tokens\",\"content\":[{\"type\":\"text\",\"text\":\"{\"}]}", "ยาวเกิน")]
[InlineData(200, "{\"stop_reason\":\"end_turn\",\"content\":[{\"type\":\"text\",\"text\":\"not json\"}]}", "อ่านคำตอบ")]
[InlineData(200, "", "อ่านคำตอบ")]
[InlineData(200, null, "อ่านคำตอบ")]
public void Failures_become_a_problem_text(int status, string body, string expectedPart)
{
    var r = ExplainResponse.Parse(status, body, 10);
    Assert.Contains(expectedPart, r.Problem);
    Assert.Empty(r.Findings);
}
```

- [ ] **Step 3:** `dotnet test tests\SqlAssist.Core.Tests --filter "FullyQualifiedName~Explain"` → FAIL
- [ ] **Step 4:** เขียนทั้งสอง class ประกอบ JSON ด้วยการ escape string เอง (helper `JsonString(string)` ใน `ExplainRequest`: `"` `\` control character และ
  ` ` / ` `) ส่วนการอ่านใช้ `DataContractJsonSerializer` กับ class `[DataContract]` ภายใน แบบ `ReleaseInfo`
- [ ] **Step 5:** รัน filter เดิม → PASS แล้วรัน test ทั้งหมด `dotnet test tests\SqlAssist.Core.Tests` → PASS
- [ ] **Step 6:** Commit `Ai: Explain SQL request and response`

### Task 5: `AiKeyStore` และ `AiClient` (Vsix)

**Files:**
- Create: `src/SqlAssist.Vsix/Ai/AiKeyStore.cs`, `src/SqlAssist.Vsix/Ai/AiClient.cs`
- Modify: `src/SqlAssist.Vsix/SqlAssist.Vsix.csproj` (เพิ่ม `<Reference Include="System.Security" />` ถ้ายังไม่มี)

**Interfaces:**
- Produces:
  - `internal static class AiKeyStore` — `static readonly string FilePath = Path.Combine(Log.Directory, "ai.key")`; `static bool HasKey`; `static string Read()` (null ถ้าไม่มีหรือถอดรหัสไม่ได้);
    `static void Save(string key)` (ค่าว่าง = ลบไฟล์) ใช้ `ProtectedData.Protect(Encoding.UTF8.GetBytes(key), null, DataProtectionScope.CurrentUser)`
  - `internal static class AiClient` — `static Task<(int status, string body)> SendAsync(string endpoint, string apiKey, string body, CancellationToken token)`
    POST พร้อม header ตาม Global Constraints; `HttpClient` ตัวเดียวแบบ `Lazy` ตั้ง TLS 1.2 เหมือน `UpdateService.CreateClient`, `Timeout` 120 วินาที;
    network error / timeout → `(0, null)` หลัง `Log.Error("AI request", ex)`; `OperationCanceledException` จาก `token` ปล่อยผ่าน

- [ ] **Step 1:** เขียน `AiKeyStore` ทุก method ครอบ `try/catch` → `Log.Error` และคืนค่า "ไม่มี key"; ห้าม log ค่า key
- [ ] **Step 2:** เขียน `AiClient.SendAsync` log หลังจบแค่: `"AI request: HTTP " + status + ", " + body.Length + " chars sent, " + elapsedMs + " ms"`
- [ ] **Step 3:** `ExplainResponse.Parse` รับ status 0 → เพิ่มกฎ: status 0 → `Problem` = "ติดต่อบริการ AI ไม่ได้ (เครือข่ายหรือหมดเวลา)" พร้อม test
  `[InlineData(0, null, "ติดต่อ")]` ใน `Failures_become_a_problem_text` แล้วรัน `--filter "FullyQualifiedName~ExplainResponseTests"` → PASS
- [ ] **Step 4:** `dotnet build src\SqlAssist.Vsix -c Release` → สำเร็จ ไม่มี warning ใหม่
- [ ] **Step 5:** Commit `Ai: key store (DPAPI) and HTTP client`

### Task 6: แท็บ AI ใน Options และหน้าต่างยินยอม

**Files:**
- Modify: `src/SqlAssist.Vsix/Settings/OptionsWindow.cs` (เพิ่ม `tabs.Items.Add(new TabItem { Header = "AI", Content = AiTab() });` ต่อจากบรรทัด 137 และ method `AiTab()`)
- Create: `src/SqlAssist.Vsix/Ai/AiConsentWindow.cs`

**Interfaces:**
- Consumes: `AiOptions`, `AiKeyStore`
- Produces: `internal static bool AiConsentWindow.Ask(string requestBody, string endpoint)` — true เมื่อผู้ใช้กด "ส่ง"; เป็น `Window` ธรรมดา เปิดผ่าน `DialogHost.ShowDialog`

**เนื้อหาแท็บ AI:** checkbox "เปิดใช้ Explain SQL (ส่ง SQL ไปยังบริการ AI ภายนอก)", ช่อง Endpoint, ช่อง Model, `PasswordBox` สำหรับ API key
(แสดงว่างเสมอ มีข้อความ "มี key บันทึกไว้แล้ว" / "ยังไม่มี key" และปุ่ม "ลบ key"; บันทึกเฉพาะเมื่อผู้ใช้พิมพ์ค่าใหม่), checkbox "แทนค่า literal ก่อนส่ง",
checkbox "ส่งคำอธิบายคอลัมน์ (MS_Description)", ปุ่ม "ถามยืนยันอีกครั้งในการส่งครั้งถัดไป" (ตั้ง `ConsentGiven=false`) และข้อความคงที่:

```text
เมื่อเปิดใช้ SqlAssist จะส่งข้อความ SQL ที่คุณเลือก และชื่อตาราง/คอลัมน์/ชนิดข้อมูลของตารางที่ SQL นั้นอ้างถึง ไปยัง endpoint ด้านบน
ไม่ส่งข้อมูลในตาราง ผลลัพธ์ query ชื่อ server ชื่อ database หรือ login
comment ใน SQL ถูกส่งไปตามเดิม ถ้ามีข้อมูลที่ไม่ควรออกนอกองค์กร ให้ลบออกก่อน
```

**หน้าต่างยินยอม:** หัวข้อ "ข้อมูลนี้จะถูกส่งไปที่ {endpoint}", `TextBox` อ่านอย่างเดียวแบบ monospace แสดง `requestBody` ทั้งก้อน (เลื่อนได้), checkbox
"ไม่ต้องถามอีก" (ติ๊กแล้ว caller ตั้ง `ConsentGiven=true` และ `SettingsProvider.Save`), ปุ่ม "ส่ง" / "ยกเลิก" (ยกเลิกเป็นค่าเริ่มต้นของ Esc และ Enter)

- [ ] **Step 1:** เขียน `AiTab()` และต่อการอ่าน/เขียนเข้ากับปุ่ม OK ของ `OptionsWindow` แบบเดียวกับแท็บอื่น
- [ ] **Step 2:** เขียน `AiConsentWindow` (ห้ามมี `ThreadHelper.ThrowIfNotOnUIThread` ใน constructor)
- [ ] **Step 3:** build Release → สำเร็จ แล้ว render ทั้งสองหน้าต่างนอก SSMS (วิธีเดียวกับที่เคยตรวจ `OptionsWindow`) ตรวจว่าข้อความไม่ล้นที่ความกว้างเริ่มต้นและที่ธีม Dark
- [ ] **Step 4:** Commit `Options: AI tab and the consent window`

### Task 7: คำสั่ง Explain SQL และ `ExplainWindow`

**Files:**
- Create: `src/SqlAssist.Vsix/Ai/ExplainSqlCommand.cs`, `src/SqlAssist.Vsix/Ai/ExplainWindow.cs`
- Modify: `src/SqlAssist.Vsix/SqlAssistPackage.vsct` (ปุ่ม `cmdExplainSql` = `0x010C` ใน `SubmenuGroup` priority `0x009D`, ข้อความ `Explain SQL (AI)...`;
  ปุ่ม `cmdExplainSqlContext` = `0x010D` ใน `EditorContextGroup` priority `0x0101`, ข้อความ `Explain SQL (SqlAssist AI)...`; เพิ่ม `IDSymbol` ทั้งสอง)
- Modify: `src/SqlAssist.Vsix/SqlAssistPackage.cs` (ค่าคงที่ `CmdExplainSql`, `CmdExplainSqlContext`; ลงทะเบียนแบบเดียวกับบรรทัด 61–69; `ProvideMenuResource` → 14)
- Modify: `README.md` (หัวข้อใหม่ "อธิบาย SQL ด้วย AI" + ข้อจำกัด), `CHANGELOG.md`, `CLAUDE.md` (ย่อหน้า `Ai/` ใน Architecture), `CONTEXT.md` (คำว่า Explain Request, Finding)

**Interfaces:**
- Consumes: ทุกอย่างจาก Task 1–6
- Produces: `internal static class ExplainSqlCommand { static bool CanRun(); static void Run(); }`,
  `internal sealed class ExplainWindow : Window { static void ShowResult(IWpfTextView view, SnapshotSpan scope, ExplainRequest request, ExplainResult result); }`

**ลำดับใน `Run()`** (UI thread, ครอบ `try/catch` ทั้งหมดแบบ `RiskCheckCommand.Run`):

1. ไม่มี editor → `ScriptDelivery.Status("SqlAssist: Explain SQL — เปิดหน้า query ก่อน")`
2. `!settings.Ai.Enabled` หรือ `!AiKeyStore.HasKey` → `ScriptDelivery.Show("Explain SQL ยังไม่ได้เปิดใช้ เปิดและใส่ API key ที่ Tools › SqlAssist › Options… › AI", ...)` แล้วจบ
3. `scope = ActiveSqlEditor.RunScope(view, out isSelection)`; ข้อความว่างหรือมีแต่ช่องว่าง → Status แล้วจบ; ยาวเกิน `ExplainRequest.MaxSqlChars` → `ScriptDelivery.Show` บอกให้เลือกเฉพาะส่วน แล้วจบ
4. `schema = SchemaCache.Instance.TryGetLoaded(EditorConnection.Get(...))` (null ได้)
5. `request = ExplainRequest.Build(scope.GetText(), schema, settings.Ai)`
6. `!settings.Ai.ConsentGiven` → `AiConsentWindow.Ask(request.Body, settings.Ai.Endpoint)`; false → จบโดยไม่ส่ง
7. เปิด `ExplainWindow` ในสถานะ "กำลังรอคำตอบ…" พร้อมปุ่มยกเลิก แล้วส่งด้วย `JoinableTaskFactory.RunAsync` → `AiClient.SendAsync` → `ExplainResponse.Parse(status, body, lineCount ของ request.SentSql)` → กลับ UI thread → เติมผล

**`ExplainWindow`:** บนสุดเป็นคำอธิบาย (`Summary`, เลือกคัดลอกได้), ถัดมา `ListView` ของ findings (ระดับ, บรรทัด, หัวข้อ, รายละเอียด) คลิกแถวที่ `Line > 0`
แล้วกระโดดไปที่บรรทัด `scope.Start` ของ snapshot ตอนส่ง + (`Line` − 1) ผ่าน tracking span (คัดลอกวิธีของ `RiskWindow.Jump` / `FocusEditor`),
ไม่มี findings → ข้อความ "ไม่พบข้อสังเกต", `Problem` ไม่ null → แสดงข้อความนั้นแทนทั้งหมด, ล่างสุดมีข้อความคงที่
"ข้อสังเกตมาจากการอ่านข้อความ SQL เท่านั้น ไม่ได้ดู execution plan หรือข้อมูลจริง ให้ตรวจสอบก่อนนำไปใช้", ปุ่ม "ดูสิ่งที่ส่ง" (แสดง `request.Body`) และ
บรรทัดสถานะ "ส่ง schema ของ {TableCount} ตาราง" (+ "ตัดที่ 30 ตาราง" เมื่อ `SchemaTruncated`, หรือ "ไม่มี schema ประกอบ" เมื่อ 0)

- [ ] **Step 1:** เพิ่มปุ่มใน `.vsct`, ค่าคงที่และการลงทะเบียนใน `SqlAssistPackage.cs`, `ProvideMenuResource` → 14
- [ ] **Step 2:** เขียน `ExplainWindow` (constructor ไม่มี `ThrowIfNotOnUIThread`) แล้ว render นอก SSMS ด้วย `ExplainResult` ตัวอย่าง 3 แบบ: มี findings, ไม่มี findings, มี `Problem`
- [ ] **Step 3:** เขียน `ExplainSqlCommand` ตามลำดับข้างบน
- [ ] **Step 4:** `dotnet test tests\SqlAssist.Core.Tests` → PASS ทั้งหมด; `dotnet build src\SqlAssist.Vsix -c Release` → ได้ `SqlAssist.vsix`
- [ ] **Step 5:** ให้ agent `ssms-safety-reviewer` ตรวจ diff ของ `src/SqlAssist.Vsix` (exception หลุด, UI thread, การ log ข้อมูลผู้ใช้) แล้วแก้ตามที่พบ
- [ ] **Step 6:** เขียน README / CHANGELOG / CLAUDE.md / CONTEXT.md
- [ ] **Step 7:** Commit `Explain SQL: command, result window, docs`

### Task 8: ทดสอบใน SSMS จริง (ผู้ใช้)

- [ ] ติดตั้งด้วย `.\install.ps1` (ถ้าเมนูไม่ขึ้น `.\clear-ssms-cache.ps1`)
- [ ] ยังไม่เปิดใช้: สั่ง Explain SQL → ได้ข้อความชี้ไปที่ Options และ **ไม่มี** บรรทัด `AI request` ใน `SqlAssist.log`
- [ ] เปิดใช้ + ใส่ key: ครั้งแรกเห็นหน้าต่างยินยอม ข้อความในนั้นไม่มีค่า literal จริง ชื่อ server หรือชื่อ database
- [ ] query ที่มี `LEFT JOIN` + `WHERE` ของตารางขวา → ได้คำอธิบายภาษาไทย และคลิก finding แล้ว caret ไปบรรทัดที่ถูก ทั้งกรณีเลือกบางส่วนกลางไฟล์
- [ ] key ผิด → ข้อความ "API key ไม่ถูกต้อง…" ไม่มี exception ใน log; ถอดสาย network → ข้อความ "ติดต่อบริการ AI ไม่ได้"
- [ ] กดยกเลิกระหว่างรอ → หน้าต่างปิด ไม่มี error
- [ ] เปิด `SqlAssist.log` ค้นคำจาก SQL ที่ส่ง → ต้องไม่เจอ
- [ ] ผ่านครบแล้วจึง bump version ใน `Directory.Build.props`, เปลี่ยนหัวข้อ CHANGELOG, commit + push; GitHub Release ทำเมื่อผู้ใช้สั่งเท่านั้น

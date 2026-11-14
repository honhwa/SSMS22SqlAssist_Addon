# Entra ID Authentication — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** ให้ฟีเจอร์ที่ต้องอ่าน schema (completion, `*` + Tab, hover, F12, Search, Find Usages, CRUD) ทำงานกับหน้า query ที่เชื่อมต่อด้วย Microsoft Entra ID
โดยผู้ใช้ไม่ต้อง login ซ้ำ

**Architecture:** ใช้ access token ที่ SSMS ถืออยู่แล้วของหน้า query นั้น (`UIConnectionInfo.RenewableToken`, อ่านด้วย reflection) ใส่ใน
`System.Data.SqlClient.SqlConnection.AccessToken` จึงไม่ต้องเพิ่ม `Microsoft.Data.SqlClient` หรือ DLL ใด ๆ ทุกจุดที่เปิด connection
เปลี่ยนไปเรียก `ConnectionTarget.OpenAsync` จุดเดียว ซึ่งรู้ว่าต้องใช้ token หรือ connection string

**Tech Stack:** C# (netstandard2.0 Core / net48 Vsix), xUnit, `System.Data.SqlClient` (in-box ของ .NET Framework 4.8), reflection เข้า `SQLEditors`

**Spec:** ไม่มีไฟล์ spec แยก ขอบเขตมาจากข้อเสนอข้อ 21 และคำอธิบายเพิ่มเติมในบทสนทนาเมื่อ 2026-11-14

> **แก้จากที่อธิบายไว้ในบทสนทนา:** ตอนนั้นบอกว่าต้องเปลี่ยนไปใช้ `Microsoft.Data.SqlClient` ซึ่งไม่จำเป็น เพราะ
> [ActiveConnection.cs:74](../../../src/SqlAssist.Vsix/Connection/ActiveConnection.cs) ตรวจเจอ Entra จาก `RenewableToken` อยู่แล้ว และ
> `System.Data.SqlClient.SqlConnection` มี property `AccessToken` ตั้งแต่ .NET Framework 4.6 งานจึงเล็กกว่าที่ประเมินไว้ ถ้า Task 0 ยืนยันว่าอ่าน token ได้

## สิ่งที่ต้องมีก่อนเริ่ม

- [ ] **ฐานข้อมูลที่ login ด้วย Entra ID ได้จาก SSMS 22** (Azure SQL Database, Managed Instance หรือ SQL Server ที่เปิด Entra auth) สำหรับ Task 0 และ Task 6
  ถ้าไม่มี แผนนี้ทำต่อไม่ได้: Task 1–2 ยังทำได้ (เป็น refactor ที่ไม่เปลี่ยนพฤติกรรม) แต่ Task 3 เป็นต้นไปต้องรอ

## สิ่งที่มีอยู่แล้ว (อย่าเขียนซ้ำ)

- `ActiveConnection.TryGet()` (บรรทัด 58–121): อ่าน `UIConnectionInfo` ของหน้า query; บรรทัด 74–75 คืน `Unsupported(...)` เมื่อ `RenewableToken != null`;
  helper `Get(object, property)` อ่าน property ด้วย reflection แบบไม่ throw; `Problem(message)` log ข้อความซ้ำครั้งเดียว
- `ConnectionTarget` (บรรทัด 10–41): `Server`, `Database`, `ConnectionString`, `Key`, `ServerKey`, `IntegratedSecurity`, `UserName`, `UiConnectionInfo`, `ForDatabase(database)`
- `ActiveConnection.TryGetForToolWindow()`: ไม่ fallback ไป connection เก่าเมื่อหน้าปัจจุบันเป็น Entra (`_activeUnsupported`)
- จุดที่เปิด connection เองทั้งหมด (7 จุด ใน 4 ไฟล์):

| ไฟล์ | บรรทัด | method | ผู้เรียกส่งอะไรมา |
|---|---|---|---|
| `Schema/SchemaLoader.cs` | 71 | `LoadAsync(string connectionString, CancellationToken)` | `SchemaCache.cs:80` → `target.ConnectionString` |
| `Schema/DefinitionLoader.cs` | 13 | `LoadAsync(string connectionString, string schema, string name, CancellationToken)` | `GoToDefinition.cs:101`, `TabCommandFilter.cs:292` |
| `Search/SearchIndexLoader.cs` | 98 | `LoadDatabasesAsync(string connectionString, CancellationToken)` | `SearchControl.cs:739`, `801` |
| `Search/SearchIndexLoader.cs` | 112 | `CurrentDatabaseAsync(string connectionString, CancellationToken)` | `SearchControl.cs:730`, `GoToDefinition.cs:80`, `100` |
| `Search/SearchIndexLoader.cs` | 127 | `LoadAsync(string connectionString, string database, CancellationToken)` | `SearchIndexCache.cs:75` |
| `Search/UsageLoader.cs` | 88 | `LoadAsync(ConnectionTarget server, UsageTarget target, CancellationToken)` | — |
| `Search/SearchIndexLoader.cs` | 91 | `ForDatabase(string connectionString, string database)` (helper สร้าง connection string) | บรรทัด 127, `UsageLoader.cs:88` |

- `SsmsNavigator.LocateAsync` บรรทัด 38: `ConnectToServer(server.Server, item.Database, server.IntegratedSecurity, server.UserName, server.ConnectionString)` เมื่อ server ยังไม่อยู่ใน Object Explorer
- script ที่ขึ้นต้นด้วย `USE [db]`: `SsmsNavigator.cs:70` และ `SearchControl.cs:488`

## ค่าเริ่มต้นที่เลือกไว้ (ผู้ใช้เปลี่ยนได้ก่อนเริ่ม Task 1)

| เรื่อง | ค่าเริ่มต้น | เหตุผล |
|---|---|---|
| ที่มาของ token | `RenewableToken` ของหน้า query ผ่าน reflection เรียกใหม่ทุกครั้งที่เปิด connection | SSMS เป็นคนต่ออายุ token; SqlAssist ไม่เก็บ ไม่ cache และไม่เปิดหน้า login เอง |
| ถ้าอ่าน token ไม่ได้ | ทำงานเหมือนวันนี้: log "…Entra ID…not supported" ครั้งเดียว ฟีเจอร์ที่ต้องใช้ schema เงียบ | ไม่แย่ลงกว่าเดิมใน SSMS build ที่ member เปลี่ยน |
| ไม่ใช้ | `Microsoft.Data.SqlClient`, MSAL, `Authentication=Active Directory …` ใน connection string | คง VSIX เป็น DLL เดียว และไม่มีหน้าต่าง MFA จาก extension |
| Cache key ของ Entra | `server|database|entra:<user>` (ตัวพิมพ์เล็ก) | วันนี้ connection ที่ไม่มี password ถูกนับเป็น `"windows"` ถ้าไม่แยก cache ของ Windows auth กับ Entra บน server เดียวกันจะปนกัน |
| `USE [db]` บน Azure SQL Database | ไม่ใส่ (หน้าต่างใหม่เปิดบน database นั้นอยู่แล้ว) | Azure SQL Database ไม่รองรับ `USE` ข้าม database |
| Search ข้าม database บน Azure SQL Database | แสดงเฉพาะ database ที่เชื่อมต่ออยู่ | token / สิทธิ์เป็นราย database และ `sys.databases` ให้ผลไม่ครบเมื่อไม่ได้อยู่ที่ `master` |
| Locate in Object Explorer เมื่อ server ยังไม่อยู่ใน Object Explorer และเป็น Entra | ไม่เรียก `ConnectToServer`; บอกที่ status ว่า "เชื่อมต่อ server นี้ใน Object Explorer ก่อน" | `ConnectToServer` รับ connection string ซึ่งของ Entra ไม่มี credential |

## Global Constraints

- **ห้ามเขียน token ลง log, ลงไฟล์ หรือเก็บใน field ที่อยู่นานกว่าการเปิด connection หนึ่งครั้ง** log ได้แค่: มี/ไม่มี token, ชื่อ type, เวลาหมดอายุ, ชื่อ member ที่หาไม่เจอ
- reflection ทุกขั้นต้องทนต่อ member ที่หายไป (คืน null แล้วไปทาง "ไม่รองรับ") ห้าม throw ออกจาก `ActiveConnection`
- การอ่าน `UIConnectionInfo` อยู่บน UI thread (`ThreadHelper.ThrowIfNotOnUIThread`) ตามเดิม; การเปิด connection อยู่นอก UI thread ตามเดิม
- connection ที่ใช้ `AccessToken` ต้องไม่มี `User ID`, `Password` หรือ `Integrated Security=true` ใน connection string (`SqlConnection` จะ throw)
- พฤติกรรมของ Windows auth และ SQL auth ต้องเหมือนเดิมทุกอย่าง: connection string เดิม, `Key` / `ServerKey` เดิม (cache ของผู้ใช้เดิมยังใช้ได้)
- Exception ต้องไม่หลุดเข้า SSMS; Comment ในโค้ดเป็นภาษาอังกฤษ; ทุก task เพิ่มรายการใน `CHANGELOG.md` ใต้ `## [Unreleased]`
- ห้ามเขียนใน README ว่า "รองรับ Entra ID" โดยไม่มีเงื่อนไข จนกว่า Task 6 ผ่าน ให้ระบุวิธี login ที่ทดสอบแล้วเท่านั้น
- ไม่ bump version และไม่ publish GitHub Release จนกว่าผู้ใช้ทดสอบใน SSMS แล้ว (Task 6)

## Review Focus

1. **Token หมดอายุระหว่าง session ยาว:** schema reload หลัง 10 นาที / Search หลังเปิด SSMS ค้างไว้เกิน 1 ชั่วโมง → ต้องขอ token ใหม่ทุกครั้งที่เปิด connection ไม่ใช้ค่าที่อ่านไว้ตอน `TryGet` (Task 3 + ข้อทดสอบใน Task 6)
2. **`RenewableToken` ถูกเรียกนอก UI thread:** provider ถูกเรียกจาก thread pool ตอนเปิด connection ถ้า SSMS ต้องการ UI thread จะค้างหรือ throw → Task 0 ต้องตอบข้อนี้ก่อน
3. **Windows auth กับ Entra บน server เดียวกัน, หรือ Entra สองบัญชี:** cache ต้องไม่ปนกัน (test Task 1)
4. **หน้า query Entra ที่ `ForDatabase` ไป database อื่น (`USE x` ในสคริปต์):** token ของ Azure SQL ใช้ได้ทุก database ใน server เดียวกันที่ผู้ใช้มีสิทธิ์ แต่ database ที่ไม่มีสิทธิ์ต้องจบที่ log + ไม่มี completion ไม่ใช่ exception (Task 6)
5. **`TryGetForToolWindow` fallback:** เมื่อ Entra รองรับแล้ว `_activeUnsupported` ต้องเป็น true เฉพาะกรณีอ่าน token ไม่ได้ ไม่เช่นนั้น Search จะไม่ fallback ไป connection ล่าสุดอย่างที่ควร (Task 3)
6. **Sovereign cloud / Fabric / Synapse:** resource ของ token อาจไม่ใช่ `database.windows.net` → ใช้ token ที่ SSMS ให้มาตามเดิม ไม่ตรวจหรือขอเอง และไม่อ้างว่ารองรับสิ่งที่ไม่ได้ทดสอบ

---

### Task 0: สำรวจ `RenewableToken` ใน SSMS จริง (ตัดสินว่าทำต่อหรือหยุด)

**Files:**
- Modify (ชั่วคราว ไม่ commit เข้า main): `src/SqlAssist.Vsix/Connection/ActiveConnection.cs` บรรทัด 74

- [ ] **Step 1:** ก่อน `return Unsupported(...)` เพิ่มโค้ดชั่วคราวที่ log (ครั้งเดียวต่อ session): ชื่อ type เต็มของ `RenewableToken`, ชื่อ interface ที่ implement,
  รายชื่อ public property และ method (ชื่อ + ชนิด + parameter) **ห้าม log ค่า** ยกเว้นค่าของ property ที่ชื่อมี `Expir` และค่าของ `Resource` / `Tenant`
  ถ้ามี method ไม่รับ parameter ที่คืน `string` (คาดว่าชื่อ `GetAccessToken`) ให้เรียกแล้ว log แค่ความยาวของผลลัพธ์
- [ ] **Step 2:** เพิ่มโค้ดชั่วคราวอีกชุดที่เรียก method เดียวกันจาก `Task.Run` แล้ว log ว่าได้ความยาวเท่าไร หรือ exception ชนิดไหน (ตอบ Review Focus ข้อ 2)
- [ ] **Step 3:** build, `.\install.ps1`, เปิด SSMS 22, เชื่อมต่อด้วย Entra (ลองอย่างน้อย **Microsoft Entra MFA** และแบบอื่นที่ทีมใช้จริง) เปิดหน้า query แล้วอ่าน `SqlAssist.log`
- [ ] **Step 4:** จดผลลงตารางนี้ในไฟล์แผน:

| คำถาม | ผล |
|---|---|
| type ของ `RenewableToken` | |
| ชื่อ method / property ที่ให้ token (string) | |
| property เวลาหมดอายุ | |
| เรียกจาก thread pool ได้หรือไม่ | |
| วิธี login ที่ลอง และ `RenewableToken` เป็น null หรือไม่ในแต่ละวิธี | |
| `UserName` ของ `UIConnectionInfo` เป็นค่าอะไร (ใช้ทำ cache key) | |

- [ ] **Step 5: จุดตัดสินใจ** ถ้าได้ token เป็น string จาก thread pool → ทำ Task 1 ต่อ ถ้าได้เฉพาะบน UI thread → Task 3 ต้องอ่าน token บน UI thread ตอน `TryGet`
  และอ่านซ้ำเมื่อใกล้หมดอายุ (แจ้งผู้ใช้ก่อนทำต่อ เพราะเปลี่ยนการออกแบบ) ถ้าไม่ได้ token เลย → **หยุด** รายงานผู้ใช้ ทางเลือกที่เหลือคือ clone
  connection ของ SSMS หรือแนบ `Microsoft.Data.SqlClient` ซึ่งต้องวางแผนใหม่
- [ ] **Step 6:** ลบโค้ดชั่วคราว (`git checkout -- src/SqlAssist.Vsix/Connection/ActiveConnection.cs`) แล้ว commit เฉพาะตารางผลในไฟล์แผน

### Task 1: `ConnectionKey` (Core)

**Files:**
- Create: `src/SqlAssist.Core/Schema/ConnectionKey.cs`
- Test: `tests/SqlAssist.Core.Tests/ConnectionKeyTests.cs`

**Interfaces:**
- Produces:
  - `public enum AuthKind { Windows, Sql, Entra }`
  - `public static string ConnectionKey.ForDatabase(string server, string database, AuthKind auth, string user)`
  - `public static string ConnectionKey.ForServer(string server, AuthKind auth, string user)`
  - ผลลัพธ์เป็นตัวพิมพ์เล็กทั้งหมด (`ToLowerInvariant`) ส่วน login: Windows → `windows`, Sql → `user`, Entra → `entra:` + `user` (user null → ค่าว่าง)

- [ ] **Step 1: เขียน test ที่ยังไม่ผ่าน**

```csharp
[Fact]
public void Windows_and_sql_keys_are_what_they_were_before()
{
    Assert.Equal("srv1|db1|windows", ConnectionKey.ForDatabase("SRV1", "Db1", AuthKind.Windows, null));
    Assert.Equal("srv1|db1|sa", ConnectionKey.ForDatabase("SRV1", "Db1", AuthKind.Sql, "SA"));
    Assert.Equal("srv1||windows", ConnectionKey.ForDatabase("SRV1", null, AuthKind.Windows, null));
    Assert.Equal("srv1|windows", ConnectionKey.ForServer("SRV1", AuthKind.Windows, "ignored"));
    Assert.Equal("srv1|sa", ConnectionKey.ForServer("SRV1", AuthKind.Sql, "sa"));
}

[Fact]
public void Entra_never_shares_a_key_with_windows_or_another_account()
{
    string a = ConnectionKey.ForDatabase("x.database.windows.net", "db", AuthKind.Entra, "A@corp.com");
    string b = ConnectionKey.ForDatabase("x.database.windows.net", "db", AuthKind.Entra, "b@corp.com");
    Assert.Equal("x.database.windows.net|db|entra:a@corp.com", a);
    Assert.NotEqual(a, b);
    Assert.NotEqual(a, ConnectionKey.ForDatabase("x.database.windows.net", "db", AuthKind.Windows, null));
    Assert.Equal("x.database.windows.net|entra:", ConnectionKey.ForServer("x.database.windows.net", AuthKind.Entra, null));
}
```

- [ ] **Step 2:** `dotnet test tests\SqlAssist.Core.Tests --filter "FullyQualifiedName~ConnectionKeyTests"` → FAIL
- [ ] **Step 3:** เขียน `ConnectionKey` แล้วเปลี่ยน `ActiveConnection.cs` บรรทัด 34, 107, 108 ให้เรียกมัน (ยังมีแค่ Windows / Sql)
- [ ] **Step 4:** รัน filter เดิม → PASS; `dotnet build src\SqlAssist.Vsix -c Release` → สำเร็จ
- [ ] **Step 5:** Commit `Connection: cache keys built in one place`

### Task 2: เปิด connection ที่จุดเดียว (refactor ไม่เปลี่ยนพฤติกรรม)

**Files:**
- Modify: `src/SqlAssist.Vsix/Connection/ActiveConnection.cs` (`ConnectionTarget`)
- Modify: 4 ไฟล์ loader และผู้เรียกทั้งหมดในตาราง "จุดที่เปิด connection เอง"

**Interfaces:**
- Produces (บน `ConnectionTarget`):
  - `public AuthKind Auth { get; set; }` (แทนการเดาจาก `IntegratedSecurity`; คง `IntegratedSecurity` ไว้เพราะ `SsmsNavigator` ใช้)
  - `public Func<string> AccessToken { get; set; }` (null เมื่อไม่ใช่ Entra; `ForDatabase` ต้องคัดลอกไปด้วย)
  - `public async Task<SqlConnection> OpenAsync(CancellationToken token)` — สร้าง `SqlConnection(ConnectionString)`, ถ้า `AccessToken != null` ตั้ง
    `conn.AccessToken = AccessToken()` (ผลเป็น null/ว่าง → `InvalidOperationException("No access token for " + Server)`), `OpenAsync(token)`, คืน connection ที่เปิดแล้ว;
    เปิดไม่สำเร็จต้อง `Dispose` ก่อน throw ต่อ
- เปลี่ยน signature: ทุก loader รับ `ConnectionTarget` แทน `string connectionString`
  - `SchemaLoader.LoadAsync(ConnectionTarget target, CancellationToken token)`
  - `DefinitionLoader.LoadAsync(ConnectionTarget target, string schema, string name, CancellationToken token)`
  - `SearchIndexLoader.LoadDatabasesAsync(ConnectionTarget server, CancellationToken token)`
  - `SearchIndexLoader.CurrentDatabaseAsync(ConnectionTarget server, CancellationToken token)`
  - `SearchIndexLoader.LoadAsync(ConnectionTarget server, string database, CancellationToken token)` (ข้างในใช้ `server.ForDatabase(database).OpenAsync(token)`)
  - ลบ `SearchIndexLoader.ForDatabase(string, string)`; `UsageLoader.cs:88` ใช้ `server.ForDatabase(target.Database).OpenAsync(token)`

- [ ] **Step 1:** เพิ่ม member บน `ConnectionTarget` และตั้ง `Auth` ใน `TryGet` (`Windows` / `Sql`) กับ `ForDatabase`
- [ ] **Step 2:** เปลี่ยน loader ทีละไฟล์จาก `using (var conn = new SqlConnection(cs)) { await conn.OpenAsync(token)...` เป็น `using (var conn = await target.OpenAsync(token).ConfigureAwait(false))`
  แล้วแก้ผู้เรียกให้ส่ง `ConnectionTarget`
- [ ] **Step 3:** ตรวจว่าไม่เหลือจุดเปิด connection นอก `ConnectionTarget.OpenAsync`: ค้น `new SqlConnection(` ใน `src/SqlAssist.Vsix` → ต้องเจอที่เดียวคือใน `OpenAsync`
- [ ] **Step 4:** `dotnet test tests\SqlAssist.Core.Tests` → PASS; `dotnet build src\SqlAssist.Vsix -c Release` → สำเร็จ
- [ ] **Step 5:** ผู้ใช้ทดสอบเร็วใน SSMS ด้วย Windows auth และ SQL auth: completion ของตาราง, `*` + Tab, Search, Find Usages, F12 ยังทำงาน (ถ้าไม่ bump version ต้องปิด SSMS แล้ว `/install` เอง)
- [ ] **Step 6:** Commit `Connection: every loader opens through ConnectionTarget.OpenAsync`

### Task 3: สร้าง `ConnectionTarget` ของ Entra

**Files:**
- Modify: `src/SqlAssist.Vsix/Connection/ActiveConnection.cs` (บรรทัด 74–75 และ `TryGet` ส่วนที่สร้าง builder)

**Interfaces:**
- Consumes: ชื่อ member ของ `RenewableToken` จากตารางผล Task 0, `ConnectionKey`, `ConnectionTarget.AccessToken`
- Produces: `TryGet()` คืน `ConnectionTarget` ที่ `Auth == AuthKind.Entra`, `IntegratedSecurity == false`, `UserName` = ค่าจาก `UIConnectionInfo`,
  `ConnectionString` มีแค่ `DataSource`, `InitialCatalog`, `ApplicationName`, `ConnectTimeout`, `Encrypt=true`, `TrustServerCertificate=false`
  (Azure มี certificate ที่เชื่อถือได้ ไม่ต้องข้ามการตรวจ), `AccessToken` = delegate ที่อ่าน `RenewableToken` จาก `UiConnectionInfo` **ใหม่ทุกครั้ง** แล้วเรียก method ที่ให้ token

- [ ] **Step 1:** เขียน `private static Func<string> TokenProvider(object uiConnectionInfo)` ทุกขั้น reflection ครอบ `try/catch` คืน null เมื่อหา member ไม่เจอ
  และ log ชื่อ member ที่หายผ่าน `Problem(...)` (ครั้งเดียว)
- [ ] **Step 2:** แทนบรรทัด 74–75: ถ้า `RenewableToken != null` ให้ลองเรียก provider หนึ่งครั้งบน UI thread; ได้ token → สร้าง target แบบ Entra;
  ไม่ได้ → `Unsupported("Connection to " + server + " uses Entra ID authentication, but SSMS did not give SqlAssist a token")`
- [ ] **Step 3:** ตรวจว่า `_activeUnsupported` เป็น true เฉพาะทางที่อ่าน token ไม่ได้ และ `Seen` (รายชื่อ server ของ Search) รวม target แบบ Entra ด้วย
- [ ] **Step 4:** แก้ doc comment ของ `TryGet` และ `TryGetForToolWindow` ที่ยังเขียนว่า Entra ไม่รองรับ
- [ ] **Step 5:** build Release → สำเร็จ; ให้ agent `ssms-safety-reviewer` ตรวจ diff (reflection, UI thread, การ log) แล้วแก้ตามที่พบ
- [ ] **Step 6:** Commit `Connection: Entra ID windows use the token SSMS already holds`

### Task 4: ความต่างของ Azure SQL Database

**Files:**
- Create: `src/SqlAssist.Core/Search/ServerEdition.cs`
- Test: `tests/SqlAssist.Core.Tests/ServerEditionTests.cs`
- Modify: `src/SqlAssist.Vsix/Search/SearchIndexLoader.cs` (`LoadDatabasesAsync`), `src/SqlAssist.Vsix/Search/SsmsNavigator.cs` (บรรทัด 38, 70),
  `src/SqlAssist.Vsix/Search/SearchControl.cs` (บรรทัด 488), `src/SqlAssist.Vsix/Connection/ActiveConnection.cs` (`ConnectionTarget.IsSingleDatabase`)

**Interfaces:**
- Produces:
  - `public static bool ServerEdition.IsSingleDatabase(int engineEdition)` — true เมื่อ `SERVERPROPERTY('EngineEdition')` เป็น 5 (Azure SQL Database) หรือ 6 (Azure Synapse dedicated pool)
  - `public static string ServerEdition.UsePrefix(string bracketedDatabase, bool singleDatabase, string newline = "\r\n")` — `""` เมื่อ `singleDatabase`; ไม่เช่นนั้น `"USE " + bracketedDatabase + ";" + newline + "GO" + newline + newline` (ข้อความเดิมทุกตัวอักษร)
  - `ConnectionTarget.IsSingleDatabase` (`bool?`, null = ยังไม่รู้) ตั้งค่าครั้งแรกที่ `LoadDatabasesAsync` หรือ `CurrentDatabaseAsync` รัน โดยอ่าน
    `CAST(SERVERPROPERTY('EngineEdition') AS int)` ใน query เดียวกัน

- [ ] **Step 1: เขียน test ที่ยังไม่ผ่าน**

```csharp
[Theory]
[InlineData(2, false)] [InlineData(3, false)] [InlineData(4, false)] [InlineData(8, false)]   // Standard, Enterprise, Express, Managed Instance
[InlineData(5, true)] [InlineData(6, true)]
[InlineData(0, false)] [InlineData(99, false)]
public void Single_database_editions(int edition, bool expected) => Assert.Equal(expected, ServerEdition.IsSingleDatabase(edition));

[Fact]
public void Use_prefix_is_unchanged_on_a_normal_server_and_empty_on_azure_sql_database()
{
    Assert.Equal("USE [Db1];\r\nGO\r\n\r\n", ServerEdition.UsePrefix("[Db1]", false));
    Assert.Equal("", ServerEdition.UsePrefix("[Db1]", true));
}
```

- [ ] **Step 2:** `dotnet test tests\SqlAssist.Core.Tests --filter "FullyQualifiedName~ServerEditionTests"` → FAIL
- [ ] **Step 3:** เขียน `ServerEdition`; ใช้ `UsePrefix` ที่ `SsmsNavigator.cs:70` และ `SearchControl.cs:488` (`IsSingleDatabase ?? false`)
- [ ] **Step 4:** `LoadDatabasesAsync`: เมื่อเป็น single database คืนรายการที่มีแค่ database ปัจจุบัน
- [ ] **Step 5:** `SsmsNavigator.LocateAsync` บรรทัด 35–39: เมื่อ `rootUrn == null` และ `server.Auth == AuthKind.Entra` คืน
  `Fail("เชื่อมต่อ " + server.Server + " ใน Object Explorer ก่อน แล้วลองอีกครั้ง")` แทนการเรียก `ConnectToServer`
- [ ] **Step 6:** รัน filter เดิม → PASS; test ทั้งหมด → PASS; build Release → สำเร็จ
- [ ] **Step 7:** Commit `Search: no USE prefix and one database on Azure SQL Database`

### Task 5: เอกสาร

**Files:**
- Modify: `README.md` (หัวข้อ "ข้อจำกัด (v1)": เอาข้อ Entra ID ออก แล้วเขียนแทนว่ารองรับเมื่อ login ด้วยวิธีที่ระบุในตารางผล Task 0 และข้อจำกัดบน Azure SQL Database),
  `CHANGELOG.md`, `CLAUDE.md` (ย่อหน้า `Connection/ActiveConnection.cs`: แก้ "Windows/SQL auth only (no Entra ID)" และเพิ่มว่า loader ทุกตัวเปิดผ่าน `ConnectionTarget.OpenAsync`)

- [ ] **Step 1:** แก้ทั้งสามไฟล์ ใช้คำใน `CONTEXT.md` (Active Connection) และเพิ่มคำว่า Access Token ถ้ายังไม่มี
- [ ] **Step 2:** Commit `Docs: Entra ID connections`

### Task 6: ทดสอบใน SSMS จริง (ผู้ใช้)

- [ ] **Entra + Azure SQL Database:** completion ของตาราง/คอลัมน์, `SELECT *` + Tab, hover, F12 ไปที่ view / procedure, Search, Find Usages, Generate CRUD (script ไม่มี `USE`)
- [ ] **เปิด SSMS ค้างไว้เกิน 1 ชั่วโมง** แล้วกด Ctrl+Shift+R (refresh schema) และค้นใน Search → ยังทำงาน ไม่มี error เรื่อง token ใน log
- [ ] **หน้า query สองหน้า:** หน้าหนึ่ง Entra อีกหน้า Windows auth สลับไปมา → completion ของแต่ละหน้าเป็นตารางของ server นั้น
- [ ] **`USE` ไป database ที่ไม่มีสิทธิ์** → ไม่มี completion ของตาราง มีบรรทัด error ใน log หนึ่งครั้ง SSMS ไม่ค้าง
- [ ] **Locate in Object Explorer** เมื่อ server ยังไม่อยู่ใน Object Explorer → ได้ข้อความให้เชื่อมต่อก่อน
- [ ] **Windows auth / SQL auth บน SQL Server ปกติ:** ทุกอย่างเหมือนก่อนแก้ (ทดสอบซ้ำชุดเดียวกับ Task 2 Step 5)
- [ ] ค้น `SqlAssist.log` ด้วย `eyJ` (ขึ้นต้นของ JWT) → ต้องไม่เจอ
- [ ] ผ่านครบแล้วจึง bump version ใน `Directory.Build.props`, เปลี่ยนหัวข้อ CHANGELOG, commit + push; GitHub Release ทำเมื่อผู้ใช้สั่งเท่านั้น

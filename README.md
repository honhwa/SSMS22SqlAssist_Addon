# SqlAssist — IntelliSense + Snippets สำหรับ SSMS 22

add-on ขนาดเล็กสำหรับ SQL Server Management Studio 22 ทำแค่ 5 อย่างที่ใช้จริงจาก dbForge SQL Complete และมีหน้าต่างค้นหา object แบบ dbForge Search (ดู [ค้นหา object](#ค้นหา-object))

| ฟีเจอร์ | ใช้ยังไง |
|---|---|
| ขยาย `*` เป็นรายชื่อคอลัมน์ | `SELECT *` แล้วกด **Tab** จะได้ `c.CustomerID, c.Name, ...` (หรือ `c.*` + Tab เพื่อขยายเฉพาะตาราง `c`) |
| alias อัตโนมัติ | เลือกตารางหลัง `FROM`/`JOIN` จะได้ `Customers c`, `OrderDetails od` และถ้าซ้ำจะได้ `c2` |
| JOIN ตาม Foreign Key | พิมพ์ `JOIN ` แล้วรายการจะเริ่มด้วย `Orders o ON o.CustomerID = c.CustomerID` |
| ดูโครงสร้างตาราง | เลื่อนเลือกตาราง/วิวในรายการแนะนำค้างไว้ 1.5 วินาที จะมีกล่องข้าง ๆ แสดงคอลัมน์ ชนิดข้อมูล PK/FK, `IDENTITY` / `NOT NULL` และจำนวนแถวโดยประมาณ (Est. row count) |
| ชี้เมาส์ดูตาราง | เอาเมาส์ชี้ชื่อตารางหรือ alias ใน query (เช่น `Work_InitialApproval`, `wia`, หรือ `wr` ใน `wr.ReqId`) จะขึ้นกล่องโครงสร้างตารางแบบเดียวกัน |
| CTE / subquery | คอลัมน์ของ `WITH Sales_CTE AS (...)` และ `FROM (SELECT ...) d` ถูกแนะนำเหมือนตารางจริง ใช้กับ `*` + Tab, JOIN, การชี้เมาส์ได้ ถ้ายังไม่มี FROM (`SELECT |`) จะแนะนำคอลัมน์ของ CTE ให้เลย |
| เลือกคอลัมน์แบบกิ่ง | ใน `SELECT |` ที่ยังไม่มี FROM รายการจะเป็นกิ่ง: CTE และทุกตาราง กด ▸ (หรือ → / ←) เพื่อขยายดูคอลัมน์ ติ๊ก ☐ (หรือ Shift+Space) ได้หลายตัว แล้ว Enter / Tab จะ gen `SELECT ... FROM ... JOIN ...` ให้ทั้งก้อน (JOIN ตาม FK หรือคอลัมน์ `...Id` ที่ชื่อตรงกัน) ไม่ติ๊กอะไร = ได้ทั้งตารางหรือคอลัมน์ที่เลือก |
| Column picker | ใน select list ที่มี FROM แล้ว (`SELECT | FROM Person.PersonPhone pp JOIN Person.Person p ...`) กด Space หลัง `SELECT` / `,` หรือ Ctrl+Space จะได้กิ่งของแต่ละตารางตาม alias (กางไว้แล้ว) ติ๊ก ☐ (หรือ Shift+Space) แล้ว `p.FirstName` ถูกเขียนลง select list ทันที (comma นำหน้าบรรทัดละคอลัมน์) เอาติ๊กออก = ลบออก พิมพ์เพื่อกรองชื่อคอลัมน์ได้ Enter / Tab / Esc ปิดรายการ |
| ตัวแปร / parameter | พิมพ์ `@` จะแนะนำตัวแปรที่ `DECLARE` ไว้ด้านบน และ parameter ของ `CREATE/ALTER PROCEDURE / FUNCTION` พร้อมชนิดข้อมูล และ INPUT / OUTPUT (นับเฉพาะใน batch เดียวกัน คือหลัง `GO` ตัวล่าสุด) |
| ข้อมูลคอลัมน์ | เลือกคอลัมน์ในรายการแนะนำ จะเห็นชนิดข้อมูล, PK / IDENTITY / NOT NULL, คำอธิบายจากฐานข้อมูล (MS_Description) และ Foreign key ไปตารางไหน |
| เงื่อนไข ON อัตโนมัติ | พิมพ์ `JOIN Work_InitialApproval wia ON ` แล้วจะได้ `wr.ReqId = wia.ReqId` เรียงจาก Foreign Key ก่อน แล้วตามด้วยคอลัมน์ที่ชื่อตรงกัน (คอลัมน์ `...Id` ขึ้นก่อน) ใช้หลัง `AND` ได้ด้วย |
| ค้นชื่อไม่ต้องตรงตัว | `custid` → `CustomerID`, `od` → `OrderDetails` |
| Snippets | `ssf` + **Tab** → `SELECT * FROM ` แล้วรายชื่อตารางจะเด้งขึ้นมาให้เลือกต่อ |

## ก่อนติดตั้ง (ทำครั้งเดียว)

1. **ปิด IntelliSense ของ SSMS**: Tools › Options › Text Editor › Transact-SQL › IntelliSense › เอาติ๊ก *Enable IntelliSense* ออก
2. **Uninstall หรือปิด dbForge SQL Complete** เพราะมันดักปุ่ม Tab และ IntelliSense เหมือนกัน

## ติดตั้ง

ดับเบิลคลิก **`dist\SqlAssistSetup-<version>.exe`** จะเปิดหน้าต่างติดตั้งขึ้นมา

- **ยังไม่เคยติดตั้ง:** กดปุ่ม **ติดตั้ง**
- **ติดตั้งไว้แล้ว:** เลือกได้ว่าจะ **อัปเดต** (หรือติดตั้งซ้ำถ้าเป็นเวอร์ชันเดียวกัน) หรือ **ถอนการติดตั้ง**
- ถอนการติดตั้งจาก **Control Panel › Programs and Features** (หรือ Settings › Apps) ได้ด้วย: เลือก **SqlAssist for SSMS 22** แล้วกด Uninstall
- **ถ้า SSMS เปิดอยู่:** จะขึ้นแถบเตือนสีเหลืองและกดปุ่มไม่ได้ พอปิด SSMS ครบทุกหน้าต่าง ปุ่มจะกดได้เองภายใน 1 วินาที

### ตรวจสอบเวอร์ชัน
- ตัวติดตั้งจะแสดงเวอร์ชันที่ติดตั้งอยู่เทียบกับเวอร์ชันในตัวติดตั้ง และบอกว่ามีเวอร์ชันใหม่กว่าหรือเป็นล่าสุดแล้ว
- ใน SSMS: **Tools › SqlAssist › About...**
- ใน log (Tools › SqlAssist › Open Log) ทุกครั้งที่เปิดหน้า query จะมีบรรทัด `SqlAssist x.y.z attached`
- ก่อนออกเวอร์ชันใหม่ ให้แก้เลขเวอร์ชันที่ `Directory.Build.props` ที่เดียว แล้วรัน `.\build-setup.ps1`

### อัปเดตออนไลน์ (ตั้งแต่ 1.3.0)
- ตอนเปิด SSMS ระบบจะเช็ก [GitHub Releases](https://github.com/kriangdet/SSMS22SqlAssist_Addon/releases) วันละครั้ง ถ้ามีเวอร์ชันใหม่จะขึ้นแถบแจ้งเตือนด้านบนหน้าต่าง SSMS
- เช็กเองได้ที่ **Tools › SqlAssist › Check for Updates...**
- ในแถบแจ้งเตือนมีปุ่มให้เลือก:
  - **Download and install** — ดาวน์โหลดตัวติดตั้งไว้ที่ `%LOCALAPPDATA%\SqlAssist\updates\` แล้วตรวจ SHA-256 กับค่าที่ GitHub ให้มา จากนั้นจะเปิดหน้าต่างติดตั้งขึ้นมา ให้ **ปิด SSMS แล้วกดอัปเดต**
  - **What's new** — เปิดหน้า release เพื่อดูว่ามีอะไรใหม่
  - **Skip this version** — ไม่ต้องเตือนเวอร์ชันนี้อีก
  - **Don't check automatically** — ปิดการเช็กอัตโนมัติ (เมนู Check for Updates ยังใช้ได้ตามปกติ) ถ้าอยากเปิดกลับ ให้ลบไฟล์ `%LOCALAPPDATA%\SqlAssist\update.txt`
- ออกเวอร์ชันใหม่ (สำหรับนักพัฒนา): แก้เวอร์ชัน → `.\build-setup.ps1` → commit แล้ว push → ตั้งค่า `$env:GITHUB_TOKEN` → `.\publish-release.ps1`

- ไฟล์ exe ไฟล์เดียวมี add-on อยู่ข้างในแล้ว จะก๊อปไปเครื่องอื่นที่มี SSMS 22 ก็ใช้ได้เลย ไม่ต้องลงโปรแกรมเพิ่ม
- ติดตั้งแบบ per-user ไม่ต้องใช้สิทธิ์ admin ถ้า SSMS ยังเปิดอยู่จะขอให้ปิดก่อน
- ติดตั้งซ้ำทับได้ (ใช้ตอนอัปเดตเวอร์ชัน)
- ใช้ผ่าน command line โดยไม่เปิดหน้าต่างได้: `SqlAssistSetup-<version>.exe /install` หรือ `/uninstall` (exit code: 0 สำเร็จ, 1 ล้มเหลว, 2 SSMS เปิดอยู่, 3 ไม่พบ SSMS 22)

พอเปิด SSMS ใหม่จะเห็นเมนู **Tools › SqlAssist: ...**

สร้าง exe ใหม่หลังแก้โค้ดด้วย `.\build-setup.ps1` (รัน test → build → ได้ไฟล์ใน `dist\`)
ถ้าไม่อยากใช้ exe ก็ใช้สคริปต์ `.\install.ps1` / `.\uninstall.ps1` แทนได้

## ปุ่มลัด

| ปุ่ม | ทำอะไร |
|---|---|
| Tab | ขยาย snippet → ถ้ามีรายการแนะนำเปิดอยู่จะเลือกรายการนั้น → ขยาย `*` → ถ้าไม่เข้าข้อไหนเลยจะเป็น Tab ปกติ |
| Enter | เลือกรายการแนะนำ ถ้าพิมพ์คำครบแล้ว (เช่น `from`) จะเลือกแล้วขึ้นบรรทัดใหม่ให้ด้วย |
| Space, `,`, `(` ฯลฯ | ปิดรายการแนะนำโดย **ไม่** เลือกอะไร |
| Ctrl+Space | เปิดรายการแนะนำเอง |
| Ctrl+Shift+R | โหลด schema ใหม่ (หรือใช้ Tools › SqlAssist › Refresh Schema Cache) |

schema จะถูกโหลดครั้งแรกที่ใช้งานในแต่ละ database แล้ว cache ไว้ 10 นาที
ถ้าใน query มีชื่อตารางที่ cache ยังไม่รู้จัก (เช่นเพิ่งสร้าง) ระบบจะโหลดใหม่เองโดยห่างกันอย่างน้อย 30 วินาที

## ค้นหา object

เปิดจาก **Tools › SqlAssist › Search Objects...** หรือปุ่ม **SqlAssist** (โลโก้ SqlAssist) บน toolbar **SqlAssist** (ถ้าไม่เห็น toolbar ให้คลิกขวาที่แถบ toolbar แล้วติ๊ก SqlAssist หรือไปที่ View › Toolbars) หน้าต่างจะ dock เป็นแท็บข้าง Output และลากไปวางที่อื่นได้

- **ค้นขณะพิมพ์:** พิมพ์อย่างน้อย 2 ตัวอักษรแล้วผลจะขึ้นเอง (กด Enter เพื่อค้นทันที หรือกด ↓ เพื่อไปที่ตารางผลลัพธ์)
- **ค้นจากอะไร:** ชื่อ object และข้อความใน definition ของ view / procedure / function / trigger ส่วนชื่อตารางและคอลัมน์จะค้นจากชื่ออย่างเดียว
- **ชนิด object (dropdown "All object types"):** ติ๊กเลือก Schema, Table, View, Procedure, Function, Trigger, Column
- **ตัวเลือกการค้น (ปุ่ม ⚙):** Match case, Match whole word และ Use wildcards (`*` = อักษรกี่ตัวก็ได้ในบรรทัดเดียวกัน, `?` = อักษร 1 ตัว) ถ้าไม่ติ๊กอะไรเลยจะค้นแบบ "มีคำนี้อยู่" และไม่สนตัวพิมพ์เล็กใหญ่
- **Database (dropdown):** ค่าเริ่มต้นคือ database ปัจจุบัน เลือกได้หลายตัวหรือเลือก **All databases** (ไม่รวม system database) ระบบจะโหลดทีละ database และแสดงผลทันทีที่เจอ ส่วน database ที่ offline หรือไม่มีสิทธิ์จะถูกข้ามและบันทึกไว้ใน log
- **Server (dropdown):** ค่าเริ่มต้นคือ "Active query window" (ใช้ server ของหน้าต่าง query ที่ active อยู่) หรือเลือก server อื่นที่เคยต่อในรอบนี้ก็ได้ ถ้าหน้าต่าง query ที่ active เปลี่ยนไปต่อ server อื่น ระบบจะกลับไปใช้ server ของหน้าต่างนั้นเอง
- **กระโดดไปที่ object:** ดับเบิลคลิกหรือกด **Enter** ที่ผลลัพธ์ แล้ว Object Explorer จะกาง tree ไปที่ object นั้นและเลือกไว้ให้ (ถ้า Object Explorer ยังไม่ได้ต่อ server นั้น ระบบจะต่อให้เอง) ส่วน **Ctrl+Enter** จะเปิด query window ใหม่ที่มี `USE [db]` ตามด้วย definition และเลือกคำที่เจอไว้ คลิกขวาที่ผลลัพธ์จะมีเมนู Locate in Object Explorer, Open in new query window และ Copy name (Ctrl+C)
- **ผลลัพธ์:** ตารางคอลัมน์ Name, Schema, Type, Parent, Database, Matched in คลิกที่หัวคอลัมน์เพื่อเรียงได้ แสดงสูงสุด 5,000 รายการ
- **Preview:** แสดง definition และ highlight คำที่เจอ ถ้าเป็นตารางจะแสดง `CREATE TABLE` แบบย่อ (คอลัมน์, type, NULL/NOT NULL, primary key) ส่วนคอลัมน์จะแสดงของตาราง/view ที่คอลัมน์นั้นอยู่

ข้อมูลที่ใช้ค้นจะถูกโหลดมาเก็บไว้ใน memory ทีละ database แล้ว cache ไว้ 10 นาที (เกิน 10 นาทีจะโหลดใหม่เบื้องหลัง) ถ้าต้องการโหลดใหม่ทันทีให้กดปุ่ม **Refresh** ในหน้าต่าง หรือกด Ctrl+Shift+R / **Tools › SqlAssist › Refresh Schema Cache**

ทุกครั้งที่เปิด SSMS ใหม่ หน้าต่าง Search จะเริ่มจากค่าเริ่มต้นเสมอ (ทุกชนิด object, ไม่ติ๊กตัวเลือกการค้น, database ปัจจุบัน) ค่าที่เลือกไว้จะใช้ได้จนกว่าจะปิด SSMS

## แก้ไข Snippets

ไปที่ **Tools › SqlAssist › Edit Snippets...** จะเปิดหน้าต่าง **Snippets Manager**

- **รายการด้านซ้าย:** มีช่องค้นหา และเลือกเรียงตาม Shortcut หรือตามคำอธิบายได้
- **ด้านขวา:** แก้ **Shortcut** (คำที่พิมพ์แล้วกด Tab), **คำอธิบาย** และ **ข้อความ** ของ snippet
- **Literals:** ดับเบิลคลิกเพื่อแทรกลงในข้อความ ค่าเหล่านี้จะถูกแทนตอนขยาย snippet

  | Literal | ได้อะไร |
  |---|---|
  | `$CARET$` | ตำแหน่งเคอร์เซอร์หลังขยาย snippet |
  | `$DATE$` / `$TIME$` | วันที่ / เวลาปัจจุบัน |
  | `$USER$` | ชื่อผู้ใช้ Windows |
  | `$DB$` / `$SERVER$` | database / server ของหน้า query |

- **ปุ่มด้านบน:** Snippet ใหม่, ลบ, บันทึก (Ctrl+S), ยกเลิกการแก้ไข (โหลดจากไฟล์ใหม่) และเปิดโฟลเดอร์
- **การตรวจตอนบันทึก:** Shortcut ห้ามว่าง ห้ามซ้ำ และห้ามมีช่องว่าง ส่วนข้อความห้ามว่าง
- **มีผลทันที:** บันทึกแล้วใช้ได้เลย ไม่ต้องเปิด SSMS ใหม่
- **ถ้าปิดหน้าต่างโดยยังไม่ได้บันทึก:** จะถามก่อนว่าจะบันทึกไหม

snippet เก็บอยู่ที่ไฟล์ `%APPDATA%\SqlAssist\snippets.xml` จะแก้ไฟล์นี้ด้วยมือก็ได้ ไฟล์เก่าที่ใช้ `$end$` ยังใช้ได้เหมือนเดิม (มีความหมายเดียวกับ `$CARET$`) snippet แบบหลายบรรทัดจะเยื้องตามบรรทัดที่พิมพ์คำย่อ

## ข้อจำกัด (v1)

- รองรับแค่ Windows auth กับ SQL auth ยังไม่รองรับ Entra ID / Azure AD
- database ที่ใช้มาจาก connection ของหน้าต่าง query (`AdvancedOptions["DATABASE"]` ของ SSMS) ถ้าเปลี่ยนด้วย `USE` แล้วรายการยังไม่เปลี่ยนตาม ให้เลือก database จาก dropdown แทน
- แบ่ง statement ด้วย `;`, `GO` หรือบรรทัดว่าง ถ้าเขียนหลาย query ติดกันโดยไม่มีตัวคั่น ตารางของ query อื่นอาจโผล่มาในรายการด้วย
- ไม่รู้จักคอลัมน์ของ table-valued function
- คอลัมน์ของ CTE ที่คำนวณจาก expression (เช่น `YEAR(OrderDate) AS SalesYear`) จะไม่มีชนิดข้อมูล รู้ชนิดเฉพาะคอลัมน์ที่ดึงมาจากตารางตรง ๆ
- Search: ยังไม่ค้นข้อมูลในตาราง (data search), index, constraint และ user-defined type และ preview หรือ script ของตารางเป็น `CREATE TABLE` แบบย่อ ไม่ใช่ script เต็มแบบของ SSMS ส่วน definition ที่เปิดใน query window เป็นข้อความ `CREATE` ตามที่ SQL Server เก็บไว้ ถ้าจะแก้ต้องเปลี่ยนเป็น `ALTER` เอง
- Search: ถ้าไม่มีหน้าต่าง query ที่ active อยู่ จะใช้ connection ล่าสุดที่เคยเห็น

## เมื่อมีปัญหา

- **Tools › SqlAssist › Open Log** จะเปิด `%LOCALAPPDATA%\SqlAssist\SqlAssist.log` ซึ่งเก็บ error ทุกตัวไว้ (ใน SSMS ต้องกลืน error ทั้งหมดไม่ให้ editor ล่ม)
- ถ้าไม่เห็นเมนู SqlAssist ให้ปิด SSMS แล้วรัน `.\clear-ssms-cache.ps1`

## สำหรับนักพัฒนา

```
src/SqlAssist.Core     logic ล้วน (netstandard2.0) ไม่พึ่ง VS: tokenizer, context, fuzzy, alias, JOIN, *, snippets, search matcher, CREATE TABLE preview, update policy
src/SqlAssist.Vsix     ส่วนที่เชื่อมกับ SSMS (net48): completion source, item/commit manager, Tab filter, schema cache, Search tool window + index cache, update check
tests/                 xUnit ของ Core
```

- `dotnet test tests\SqlAssist.Core.Tests`
- `dotnet build src\SqlAssist.Vsix -c Release` จะได้ `src\SqlAssist.Vsix\bin\Release\net48\SqlAssist.vsix`
- Debug: ตั้ง start program เป็น `Ssms.exe` ไว้แล้ว (SSMS 22 ไม่มี experimental instance จึงต้องติดตั้งก่อนแล้วค่อย attach หรือ F5)
- ตอน build ไม่ต้องมี SSMS เพราะ connection ของ SSMS อ่านผ่าน reflection เข้า `SQLEditors.dll` ตอน runtime เท่านั้น จึงไม่มี DLL ของ SSMS ถูกอ้างอิงตอน build
- เชื่อมต่อฐานข้อมูลด้วย `System.Data.SqlClient` ที่มากับ .NET Framework จึงไม่ต้องแนบ DLL เพิ่มใน VSIX

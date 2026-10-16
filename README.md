# SqlAssist — IntelliSense + Snippets สำหรับ SSMS 22

add-on ขนาดเล็กสำหรับ SQL Server Management Studio 22 ทำแค่ 5 อย่างที่ใช้จริงจาก dbForge SQL Complete

| ฟีเจอร์ | ใช้ยังไง |
|---|---|
| ขยาย `*` เป็นรายชื่อคอลัมน์ | `SELECT *` แล้วกด **Tab** จะได้ `c.CustomerID, c.Name, ...` (หรือ `c.*` + Tab เพื่อขยายเฉพาะตาราง `c`) |
| alias อัตโนมัติ | เลือกตารางหลัง `FROM`/`JOIN` จะได้ `Customers c`, `OrderDetails od` และถ้าซ้ำจะได้ `c2` |
| JOIN ตาม Foreign Key | พิมพ์ `JOIN ` แล้วรายการจะเริ่มด้วย `Orders o ON o.CustomerID = c.CustomerID` |
| ค้นชื่อไม่ต้องตรงตัว | `custid` → `CustomerID`, `od` → `OrderDetails` |
| Snippets | `ssf` + **Tab** → `SELECT * FROM ` แล้วรายชื่อตารางจะเด้งขึ้นมาให้เลือกต่อ |

## ก่อนติดตั้ง (ทำครั้งเดียว)

1. **ปิด IntelliSense ของ SSMS**: Tools › Options › Text Editor › Transact-SQL › IntelliSense › เอาติ๊ก *Enable IntelliSense* ออก
2. **Uninstall หรือปิด dbForge SQL Complete** เพราะมันดักปุ่ม Tab และ IntelliSense เหมือนกัน

## ติดตั้ง

ดับเบิลคลิก **`dist\SqlAssistSetup-<version>.exe`** แล้วเลือก `1` (ติดตั้ง) หรือ `2` (ถอนการติดตั้ง)

### ตรวจสอบเวอร์ชัน
- ตัวติดตั้งจะแสดงเวอร์ชันที่ติดตั้งอยู่เทียบกับเวอร์ชันในตัวติดตั้ง และบอกว่ามีเวอร์ชันใหม่กว่าหรือเป็นล่าสุดแล้ว
- ใน SSMS: **Tools › SqlAssist: About...**
- ใน log (Tools › SqlAssist: Open Log) ทุกครั้งที่เปิดหน้า query จะมีบรรทัด `SqlAssist x.y.z attached`
- ก่อนออกเวอร์ชันใหม่ ให้แก้เลขเวอร์ชันที่ `Directory.Build.props` ที่เดียว แล้วรัน `.\build-setup.ps1`

- ไฟล์ exe ไฟล์เดียวมี add-on อยู่ข้างในแล้ว จะก๊อปไปเครื่องอื่นที่มี SSMS 22 ก็ใช้ได้เลย ไม่ต้องลงโปรแกรมเพิ่ม
- ติดตั้งแบบ per-user ไม่ต้องใช้สิทธิ์ admin ถ้า SSMS ยังเปิดอยู่จะขอให้ปิดก่อน
- ติดตั้งซ้ำทับได้ (ใช้ตอนอัปเดตเวอร์ชัน)
- ใช้ผ่าน command line ได้ด้วย: `SqlAssistSetup.exe /install /quiet` หรือ `/uninstall /quiet`

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
| Ctrl+Shift+R | โหลด schema ใหม่ (หรือใช้ Tools › SqlAssist: Refresh Schema Cache) |

schema จะถูกโหลดครั้งแรกที่ใช้งานในแต่ละ database แล้ว cache ไว้ 10 นาที
ถ้าใน query มีชื่อตารางที่ cache ยังไม่รู้จัก (เช่นเพิ่งสร้าง) ระบบจะโหลดใหม่เองโดยห่างกันอย่างน้อย 30 วินาที

## แก้ไข Snippets

ไปที่ **Tools › SqlAssist: Edit Snippets...** ไฟล์ที่เปิดขึ้นมาคือ `%APPDATA%\SqlAssist\snippets.xml` บันทึกแล้วใช้ได้ทันทีโดยไม่ต้องเปิด SSMS ใหม่

```xml
<snippet key="ssf" description="SELECT * FROM">SELECT * FROM $end$</snippet>
<snippet key="cte" description="WITH cte AS (...)">
  WITH cte AS (
      $end$
  )
  SELECT * FROM cte;
</snippet>
```

`$end$` คือตำแหน่งเคอร์เซอร์หลังขยาย snippet แบบหลายบรรทัดจะเยื้องตามบรรทัดที่พิมพ์คำย่อ

## ข้อจำกัด (v1)

- รองรับแค่ Windows auth กับ SQL auth ยังไม่รองรับ Entra ID / Azure AD
- database ที่ใช้มาจาก connection ของหน้าต่าง query (`AdvancedOptions["DATABASE"]` ของ SSMS) ถ้าเปลี่ยนด้วย `USE` แล้วรายการยังไม่เปลี่ยนตาม ให้เลือก database จาก dropdown แทน
- แบ่ง statement ด้วย `;`, `GO` หรือบรรทัดว่าง ถ้าเขียนหลาย query ติดกันโดยไม่มีตัวคั่น ตารางของ query อื่นอาจโผล่มาในรายการด้วย
- ไม่รู้จักคอลัมน์ของ CTE, derived table และ table-valued function

## เมื่อมีปัญหา

- **Tools › SqlAssist: Open Log** จะเปิด `%LOCALAPPDATA%\SqlAssist\SqlAssist.log` ซึ่งเก็บ error ทุกตัวไว้ (ใน SSMS ต้องกลืน error ทั้งหมดไม่ให้ editor ล่ม)
- ถ้าไม่เห็นเมนู SqlAssist ให้ปิด SSMS แล้วรัน `.\clear-ssms-cache.ps1`

## สำหรับนักพัฒนา

```
src/SqlAssist.Core     logic ล้วน (netstandard2.0) ไม่พึ่ง VS: tokenizer, context, fuzzy, alias, JOIN, *, snippets
src/SqlAssist.Vsix     ส่วนที่เชื่อมกับ SSMS (net48): completion source, item/commit manager, Tab filter, schema cache
tests/                 xUnit ของ Core
```

- `dotnet test tests\SqlAssist.Core.Tests`
- `dotnet build src\SqlAssist.Vsix -c Release` จะได้ `src\SqlAssist.Vsix\bin\Release\net48\SqlAssist.vsix`
- Debug: ตั้ง start program เป็น `Ssms.exe` ไว้แล้ว (SSMS 22 ไม่มี experimental instance จึงต้องติดตั้งก่อนแล้วค่อย attach หรือ F5)
- ตอน build ไม่ต้องมี SSMS เพราะ connection ของ SSMS อ่านผ่าน reflection เข้า `SQLEditors.dll` ตอน runtime เท่านั้น จึงไม่มี DLL ของ SSMS ถูกอ้างอิงตอน build
- เชื่อมต่อฐานข้อมูลด้วย `System.Data.SqlClient` ที่มากับ .NET Framework จึงไม่ต้องแนบ DLL เพิ่มใน VSIX

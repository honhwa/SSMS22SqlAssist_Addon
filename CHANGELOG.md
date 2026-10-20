# Changelog

บันทึกว่าแต่ละเวอร์ชันเพิ่มหรือแก้อะไร ส่วนของเวอร์ชันไหน `publish-release.ps1` จะเอาไปแสดงเป็นรายละเอียดในหน้า
[GitHub Releases](https://github.com/kriangdet/SSMS22SqlAssist_Addon/releases) ของเวอร์ชันนั้น

ทุกครั้งที่แก้โค้ด ให้เขียนเพิ่มไว้ใต้ `## [Unreleased]` ก่อน พอปรับเวอร์ชันใน `Directory.Build.props` แล้วก็เปลี่ยนหัวข้อเป็น
`## [x.y.z] - YYYY-MM-DD` แล้วเปิด `## [Unreleased]` อันใหม่ไว้ด้านบน

## [Unreleased]

## [1.4.0] - 2026-10-20

### เพิ่ม
- **Generate Script As** จาก results grid: เลือก cell ในผลลัพธ์ แล้วคลิกขวา › **Generate Script As** › เลือกชนิด statement › ส่งไปที่ **New Query Window**, **Clipboard** หรือ **File...**
  - **INSERT**: `INSERT INTO [Table] (...) VALUES (...), (...)` (แบ่งทุก 1,000 แถว)
  - **INSERT #tmpTable**: `CREATE TABLE #tmpTable` ตามชนิดข้อมูลของคอลัมน์ + INSERT + `SELECT * FROM #tmpTable` + `DROP TABLE`
  - **WHERE IN()**: `WHERE col IN(...)` (ค่าไม่ซ้ำ, ค่า NULL กลายเป็น `IS NULL`)
  - **WHERE AND OR**: `WHERE (a = 1 AND b = N'x') OR (...)` หนึ่งกลุ่มต่อแถว
  - **UPDATE**: `UPDATE [Table] SET ... WHERE <คอลัมน์แรกของผลลัพธ์> = ...` ทีละแถว
- ปุ่มลัดในผลลัพธ์ (copy ลง clipboard): **Ctrl+J** แล้วตามด้วย **I** (INSERT), **T** (INSERT #tmpTable), **W** (WHERE IN), **O** (WHERE AND OR), **U** (UPDATE) ใช้ได้เฉพาะตอนที่ results grid ถูกเลือกอยู่ ส่วน Ctrl+J ใน editor ยังเป็น List Members เหมือนเดิม

## [1.3.3] - 2026-10-20

### เพิ่ม
- อัปเดตแบบ **Install when SSMS closes**: ดาวน์โหลดและตรวจ SHA-256 ไว้ก่อน แล้วรออยู่เบื้องหลัง พอปิด SSMS ครบทุกหน้าต่างจะติดตั้งให้เอง
  - เปิด SSMS ครั้งถัดไปจะแจ้งผล: สำเร็จจะขึ้นที่แถบสถานะ ไม่สำเร็จจะมีข้อความบอกสาเหตุ
  - ถ้า restart เครื่องก่อนได้ปิด SSMS ระบบจะตั้งรอใหม่ให้เอง
- ตัวติดตั้งรองรับคำสั่ง `SqlAssistSetup.exe /installonexit`

### เปลี่ยน
- ปุ่มในแถบแจ้งเตือนอัปเดต: **Install when SSMS closes** (ใหม่) และ **Install now** (เดิมชื่อ Download and install)
- การเช็กอัตโนมัติจะไม่เตือนซ้ำเรื่องเวอร์ชันที่ดาวน์โหลดไว้และรอติดตั้งอยู่

## [1.3.2] - 2026-10-20

### เพิ่ม
- หลัง `EXEC ` / `EXEC dbo.` / `EXEC @rc = ` รายการจะแนะนำ stored procedure และ function แทนตาราง
- ข้อมูล parameter ข้างรายการ: เช่น `dbo.ufnGetProductDealerPrice (Function) returns MONEY` พร้อม parameter ทุกตัว (ชนิดข้อมูล, INPUT / OUTPUT / READONLY) และคำอธิบาย (MS_Description)
- scalar function แนะนำในรายการทั่วไป ส่วน table-valued function แนะนำหลัง `FROM` / `JOIN`

## [1.3.1] - 2026-10-20

### เพิ่ม
- กด **Tab** หลังชื่อ object ที่ท้ายบรรทัดเพื่อขยายเป็น statement เต็ม:
  - `INSERT INTO t`: รายชื่อคอลัมน์และ `VALUES` พร้อมค่าตั้งต้นตามชนิดข้อมูล (ข้าม IDENTITY / computed / rowversion)
  - `UPDATE t`: `SET` ทุกคอลัมน์ และ `WHERE` ตาม primary key (ถ้าไม่มี PK จะเว้น `WHERE` ว่างไว้ให้กรอก)
  - `EXEC p`: `@param = ค่า` ครบทุกตัวเรียงตรงกัน ส่วน OUTPUT จะ `DECLARE` ตัวแปรให้ด้านบน
  - `ALTER PROCEDURE / FUNCTION / VIEW / TRIGGER p`: ดึงโค้ดปัจจุบันจากฐานข้อมูลมาเป็น `ALTER ...`

## [1.3.0] - 2026-10-20

### เพิ่ม
- เช็กอัปเดตออนไลน์จาก GitHub Releases วันละครั้งตอนเปิด SSMS และสั่งเองได้ที่ Tools › SqlAssist › Check for Updates
- ดาวน์โหลดตัวติดตั้ง ตรวจ SHA-256 แล้วเปิดหน้าต่างติดตั้ง

## [1.2.24] - 2026-10-20

### เพิ่ม
- Column picker: ใน `SELECT | FROM t x JOIN ...` ติ๊กคอลัมน์จากกิ่งของแต่ละตาราง แล้ว `x.Column` จะถูกเขียนลง select list ทันที

### แก้ไข
- พิมพ์ `@` ตอนที่ยังไม่มีรายการเปิดอยู่ จะเปิดรายการตัวแปรได้แล้ว

## [1.2.23] - 2026-10-20

### เพิ่ม
- พิมพ์ `@` จะแนะนำตัวแปรที่ `DECLARE` ไว้ด้านบน และ parameter ของ procedure / function พร้อมชนิดข้อมูลและ INPUT / OUTPUT

## [1.2.22] - 2026-10-20

### แก้ไข
- Snippets Manager แสดง snippet เริ่มต้นตัวใหม่ แม้จะเปิด Manager ก่อนพิมพ์ใน query

## [1.2.21] - 2026-10-20

### เปลี่ยน
- shortcut ของ snippet CASE WHEN เปลี่ยนจาก `cw` เป็น `case`

### แก้ไข
- snippet เริ่มต้นตัวใหม่จะถูกเพิ่มเข้าไปใน `snippets.xml` ที่มีอยู่แล้วให้อัตโนมัติ (ตัวที่ผู้ใช้ลบทิ้งจะไม่กลับมา)

## [1.2.20] - 2026-10-19

### เพิ่ม
- snippet เริ่มต้น CASE WHEN ... THEN ... ELSE ... END

## [1.2.19] - 2026-10-19

### เพิ่ม
- ถอนการติดตั้งได้จาก Programs and Features

## [1.2.18] - 2026-10-19

### เพิ่ม
- คอลัมน์ของ CTE และ derived table ถูกแนะนำเหมือนตารางจริง
- `SELECT |` ที่ยังไม่มี FROM: เลือกคอลัมน์จากกิ่งของตาราง แล้วสร้าง `SELECT ... FROM ... JOIN ...` ให้ทั้งก้อน
- หน้าต่างตัวติดตั้งแบบใหม่

## [1.2.15] - 2026-10-17

### เพิ่ม
- Snippets Manager, ค่าแทนที่ใน snippet (`$DATE$`, `$USER$`, `$DB$` ...) และเมนู Tools › SqlAssist

## [1.2.13] - 2026-10-17

### เพิ่ม
- panel โครงสร้างตารางข้างรายการแนะนำ และตอนชี้เมาส์ที่ชื่อตาราง

## [1.2.9] - 2026-10-17

### เพิ่ม
- หน้าต่างค้นหา object, แนะนำเงื่อนไข `JOIN ... ON` และ toolbar

## [1.1.2] - 2026-10-16

### เพิ่ม
- หน้าต่างติดตั้งแบบ GUI และไอคอนโปรแกรม

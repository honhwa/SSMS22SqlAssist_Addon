# Revision History

ประวัติการออกเวอร์ชันของ **SqlAssist** (extension สำหรับ SSMS 22) แบบสรุปสั้น ๆ ต่อเวอร์ชัน
รายละเอียดเต็มของแต่ละเวอร์ชันอยู่ใน [CHANGELOG.md](CHANGELOG.md) และไฟล์ติดตั้งอยู่ที่หน้า
[GitHub Releases](https://github.com/kriangdet-chuaboonjun/SSMS22SqlAssist_Addon/releases)

ทุกครั้งที่ออกเวอร์ชันใหม่ ให้เพิ่มแถวในตารางและหัวข้อของเวอร์ชันนั้นไว้บนสุด (`publish-release.ps1` จะไม่ publish
ถ้าไฟล์นี้ยังไม่มีเวอร์ชันนั้น)

| เวอร์ชัน | วันที่ | สรุป |
|---|---|---|
| [1.6.0](#160---2026-11-19) | 2026-11-19 | AI Assistant, IntelliSense สำหรับ `#temp` / `@table`, หน้าตา Options และ Search ใหม่ |

---

## 1.6.0 - 2026-11-19

**เพิ่ม**
- **AI Assistant** (Tools › SqlAssist › AI Assistant…): แชตด้านข้าง editor ถามเรื่อง SQL กับ OpenRouter หรือ Ollama
  ส่งเฉพาะข้อความที่พิมพ์และ SQL ที่แนบเอง, มี Explain / Review / Suggest Optimization, ปุ่ม Copy และ Insert into Editor
  (SqlAssist ไม่รัน SQL ให้) key ของ OpenRouter เก็บใน Windows Credential Manager
- **IntelliSense สำหรับ `#temp` และ `@table`** ที่ประกาศใน Query Editor: เติมชื่อคอลัมน์และขยาย `*` ด้วย Tab

**เปลี่ยน**
- หน้าต่าง **Options** ใหม่ (ตั้งค่าซ้าย ตัวอย่าง SQL ขวา สีตามธีม Light/Dark)
- หน้าต่าง **Search** ใช้ dropdown และปุ่มหน้าตาเดียวกับ Options
- ภายใน: การเชื่อมต่อฐานข้อมูลทุกจุดผ่านทางเดียวกัน เตรียมรองรับ Entra ID

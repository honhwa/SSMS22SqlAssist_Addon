# SSMS AI Assistant

SqlAssist เพิ่มแชตแบบ dockable tool window ใน SSMS 22 เปิดจาก **Tools → SqlAssist → AI Assistant…** กำหนดตำแหน่งเริ่มต้นด้านขวาของ SQL Editor ย้าย dock, pin หรือ float ได้ตามระบบหน้าต่างของ SSMS รองรับ Light/Dark ตาม theme resources ของ host

หน้าตาแชตใช้พื้นที่ต้อนรับตรงกลางและปุ่มคำถามแนะนำเมื่อยังไม่มีบทสนทนา ช่องพิมพ์ด้านล่างเป็นกรอบเดียวรวมปุ่มแนบ SQL, Provider, Model และ Settings (ไอคอนปรับค่า) ปุ่มส่งเป็นไอคอนลูกศรและเปลี่ยนเป็น Stop ระหว่างรอคำตอบ ปุ่ม New Chat อยู่มุมบนขวา เมนู “…” ข้างกันใช้เรียก Explain SQL, Review SQL และ Suggest Optimization ได้ระหว่างบทสนทนา ทุกปุ่มไอคอนมี tooltip และชื่อสำหรับ accessibility

## ตั้งค่า API และ Key

หน้า **Options** ทั้ง 5 แท็บ (จัดรูปแบบ SQL, ตรวจจุดเสี่ยง, Editor, Results, AI Assistant) และหน้า **API & Keys** ใช้ธีมตั้งค่าร่วมกัน: หัวข้อ breadcrumb, label เหนือช่องกรอก, controls กรอบมน, แท็บเส้นใต้ และ scrollbar แบบบาง สีเปลี่ยนตาม Light/Dark ของ SSMS โดยไม่เปลี่ยนตัวเลือกหรือวิธีบันทึกเดิม มี smoke test ตรวจทุกแท็บที่หน้าต่างกว้าง 560/760 DIP, สูง 520 DIP รวม scrolling และ Save/Cancel ด้วยไฟล์ settings ชั่วคราว

รองรับ OpenRouter และ Ollama โดยนำ Gemini ออกตามคำขอแล้ว Settings เดิมที่เคยเลือก Gemini จะเปลี่ยนมาใช้ OpenRouter โดยเก็บ Model ID ของ OpenRouter/Ollama เดิมไว้ ส่วน credential ของ provider เดิมใน Windows จะไม่ถูกอ่านหรือลบอัตโนมัติ

เปิด **Settings / API & Keys** ในแชต หรือ **Tools → SqlAssist → Options → AI Assistant → เปิด AI API & Keys…** ทั้งสองทางใช้หน้าจัดการเดียวกัน

| Provider | การตั้งค่า |
|---|---|
| OpenRouter | กรอก key ใหม่แล้ว Save Key หรือกำหนด `OPENROUTER_API_KEY`; โหลดรายการโมเดลแล้วเลือกหรือกรอก ID เช่นรูปแบบ `vendor/model` ตามรายการจริง |
| Ollama | ระบุ Base URL ของ local/remote instance เริ่มต้น `http://localhost:11434`; กด Load Models และใช้ ID ของโมเดลที่ติดตั้งอยู่จริง ไม่ต้องมี key สำหรับ local ปกติ |

- ช่อง key ของ OpenRouter มี Show/Hide; ไม่เติม key เดิมกลับลงช่อง บันทึกผ่าน Windows Credential Manager ที่ target `SqlAssist/Assistant/OpenRouter` ไม่มี key ใน XML
- Environment key มีผลเหนือ saved key; SSMS ต้องได้รับ environment ใหม่ เช่นเปิด process ใหม่หลังตั้งค่า Delete Saved Key ลบเฉพาะ credential ที่บันทึก ไม่ลบ environment variable
- Key ที่กรอกแต่ยังไม่บันทึกใช้ Load Models/Test Connection ได้ ยกเว้นเมื่อมี environment key ซึ่งยังมีลำดับความสำคัญสูงกว่า
- Save Key/Delete Saved Key มีผลทันที ส่วน **บันทึกการตั้งค่า** ใช้กับ model, URL และ streaming; ปิด/ยกเลิกทิ้งค่าตั้งค่าที่ยังไม่บันทึก
- **Test Connection** ส่งข้อความสั้นไปยังโมเดลที่เลือกจริงและอาจใช้โควตา รายงานสำเร็จเมื่อได้รับข้อความตอบกลับ แยกจากการบันทึก settings และโหลดรายชื่อโมเดล
- Load Models, Test Connection และ generation มี timeout 30, 60 และ 180 วินาทีตามลำดับ กดหยุดคำขอเพื่อยกเลิกได้
- ปิด streaming ใน settings หากต้องการรับคำตอบเต็ม ไม่มีการ retry generation อัตโนมัติ
- รุ่นนี้รองรับ Ollama instance ที่ไม่มี gateway authentication ไม่รองรับ direct Ollama cloud API หรือ remote gateway ที่บังคับ key

## ใช้แชตกับ SQL

เลือก Provider/Model แล้วพิมพ์ข้อความ กด Enter ส่ง หรือ Shift+Enter ขึ้นบรรทัดใหม่ ปุ่ม Stop ยกเลิก request และเก็บข้อความที่ได้รับพร้อมป้ายหยุดกลางทาง New Chat เริ่มบริบทใหม่ บทสนทนาเก็บในหน่วยความจำ ไม่บันทึกลงดิสก์

**แนบ SQL ที่เลือก** อ่านเฉพาะ selection ใน query editor แสดง preview และลบออกก่อนส่งได้ หากไม่มี selection จะไม่อ่านทั้งไฟล์ Explain SQL, Review SQL และ Suggest Optimization เตรียม draft ให้ตรวจสอบก่อนกด Send ไม่แนบหรือส่ง SQL เอง

คำตอบรองรับ Markdown พื้นฐาน ได้แก่ headings, lists, emphasis, inline code และ fenced code มี Copy สำหรับ code block และ Insert into Editor สำหรับ fence ที่ระบุ `sql`, `tsql` หรือ `t-sql` แทรกที่ caret เป็นหนึ่ง Undo transaction หาก editor มี selection ต้องยกเลิก selection ก่อน เพื่อป้องกันการแทนที่ข้อความเดิม ไม่รองรับการแทรกใน read-only editor และไม่มีการ execute SQL

เมื่อเปลี่ยน provider/model ข้อความเดิมและ SQL ที่เคยแนบในบทสนทนายังเป็นบริบทที่จะส่งให้ปลายทางใหม่ มีข้อความแจ้งปลายทางเหนือช่องพิมพ์ ใช้ New Chat หากไม่ต้องการส่งบริบทเดิม ไม่เปลี่ยน provider/model ระหว่าง generation

จำกัดบริบทขาออก 32,000 ตัวอักษร ซึ่งเป็นการประมาณ ไม่ใช่จำนวน token ของโมเดล ตัดคู่บทสนทนาเก่าสุดและแจ้งจำนวนที่ละไว้ใน composer โดยไม่ลบแชตที่แสดง หากข้อความล่าสุดรวม SQL แนบเกินขีดจำกัดจะไม่ส่งจนกว่าผู้ใช้ลดขนาดเอง คำตอบบางส่วนที่ใช้ต่อเป็นบริบทมีข้อความกำกับว่าไม่สมบูรณ์

## ขอบเขตข้อมูล

ส่งเฉพาะข้อความของบทสนทนาและ SQL ที่ผู้ใช้แนบเอง ไม่อ่าน connection string, credentials, schema, query results หรือ SQL ทั้งไฟล์อัตโนมัติ Markdown แสดงด้วย native controls ไม่ประมวลผล HTML/XAML จากคำตอบ ไม่รัน script ไม่โหลดภาพภายนอก และไม่เปิดลิงก์อัตโนมัติ ไม่บันทึก request/response หรือ raw provider exception ลง log

## การตรวจสอบวันที่ 18 พฤศจิกายน 2026

- Core suite: **1,134 tests ผ่าน** จาก baseline 1,090 ครอบคลุม settings, context, request mapping, discovery, SSE/NDJSON, UTF-8 ที่แบ่ง chunk, HTTP/in-stream errors, cancellation ก่อน headers/ระหว่างอ่าน, timeout, secret redaction, lifecycle และ settings เก่าหลังนำ provider ออก
- Windows Credential Manager integration: บันทึก อ่าน Unicode แยก provider และลบผ่าน โดยใช้ target ชั่วคราวเฉพาะ tests พร้อม cleanup ไม่ใช้ credential จริงของผู้ใช้
- Release VSIX build ผ่าน มี warning เดิม 42 รายการจากส่วนอื่นของโปรเจกต์ ไม่มี warning จาก Assistant
- Standalone net48 WPF smoke ผ่าน: provider/model, draft shortcut, SQL Copy/Insert controls, settings tabs, Dark theme, duplicate Send และ Send/Stop/New Chat โดยใช้ HTTP fixture **เฉพาะใน tests** ไม่ใช่ provider จริง
- UI ตามภาพอ้างอิง: ตรวจ welcome state, composer ที่รวม controls, Send/Stop สลับตำแหน่งเดียว และ layout ที่ความกว้าง 340, 460 และ 700 DIP พร้อมดูภาพ Light/Dark
- ตรวจด้วย agent-desktop และภาพหน้าจอของ WPF standalone รวมหน้า API & Keys, provider dropdown, ข้อความผิดพลาดเมื่อยังไม่มี key และบันทึก Model ID แล้วเปิด settings ซ้ำยังได้ค่าที่บันทึก (ใช้ไฟล์ settings ชั่วคราวของ test process)
- ตรวจ VSIX มี DLL และ pkgdef สำหรับ assistant พร้อมค่า Right/Linked/DocumentWell ไม่เพิ่ม third-party runtime dependency
- **ยังไม่ได้ทดสอบจริง:** OpenRouter network generation (ไม่มี key ใน environment), Ollama network generation (local instance ไม่ตอบ), อ่าน SQL selection และ Insert/Undo ใน SSMS จริง
- ไม่มีการ publish, ติดตั้ง, commit หรือ push และไม่เปลี่ยนเวอร์ชัน release

คำสั่งตรวจซ้ำ:

```powershell
dotnet test tests/SqlAssist.Core.Tests --nologo -v quiet
dotnet build src/SqlAssist.Vsix -c Release --nologo -v quiet
dotnet build tests/SqlAssist.Assistant.Smoke -c Release --nologo -v quiet
& tests/SqlAssist.Assistant.Smoke/bin/Release/net48/SqlAssist.Assistant.Smoke.exe
```

เพิ่ม `--preview` ให้ smoke executable เพื่อเปิดหน้าต่างทดสอบ standalone ที่ความกว้าง 460 DIP ใช้ `--light` เพิ่มเพื่อดู Light theme ค่าที่บันทึกโดย process นี้อยู่ใน temporary directory และถูกลบเมื่อจบ ไม่ใช่การเปิด extension ใน SSMS

ใช้ `--preview --options` เพื่อเปิดตัวอย่าง Options แบบ modal หรือ `--preview --options --light` สำหรับ Light theme

## เอกสาร provider ที่ใช้

- [OpenRouter models](https://openrouter.ai/docs/guides/overview/models), [authentication](https://github.com/openrouterteam/docs/blob/main/api_reference/authentication.mdx), [stream errors](https://github.com/openrouterteam/docs/blob/main/api_reference/errors-and-debugging.mdx)
- [Ollama chat](https://docs.ollama.com/api/chat), [tags](https://docs.ollama.com/api/tags), [streaming](https://docs.ollama.com/api/streaming), [authentication](https://docs.ollama.com/api/authentication)

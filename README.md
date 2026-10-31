# SqlAssist — 适用于 SSMS 22 的 IntelliSense + 代码片段

一个面向 SQL Server Management Studio 22 的小型插件，只做从 dbForge SQL Complete 里真正常用的 5 件事，另带一个类似 dbForge Search 的对象搜索窗口（见 [搜索对象](#搜索对象)）。

| 功能 | 用法 |
|---|---|
| 把 `*` 展开为列名列表 | 写 `SELECT *` 后按 **Tab**，得到 `c.CustomerID, c.Name, ...`（或 `c.*` + Tab，只展开表 `c`） |
| 自动别名 | 在 `FROM`/`JOIN` 后选择表，会得到 `Customers c`、`OrderDetails od`，重名时得到 `c2` |
| 按外键推荐 JOIN | 输入 `JOIN ` 后，列表开头就是 `Orders o ON o.CustomerID = c.CustomerID` |
| 查看表结构 | 在补全列表中把某个表/视图停留 1.5 秒，旁边会出现浮框，显示列、数据类型、PK/FK、`IDENTITY` / `NOT NULL` 以及预估行数（Est. row count） |
| 悬停查看表 | 把鼠标停在查询中的表名或别名上（例如 `Work_InitialApproval`、`wia`，或 `wr.ReqId` 中的 `wr`），会弹出同样的表结构浮框；停在存储过程/函数名上（`EXEC` 之后或 `(` 之前）可看到参数；停在列上（`wr.ReqId` 或 `ReqId`）可看到数据类型、键和说明。如果列没有限定表名，且有多张表含该列名，或查询中有 SqlAssist 不认识的表（例如 `#temp`、表变量），则不显示任何内容，因为无法确定属于哪张表 |
| CTE / 子查询 | `WITH Sales_CTE AS (...)` 和 `FROM (SELECT ...) d` 的列会像真实表一样被补全，可用于 `*` + Tab、JOIN、悬停。若还没有 FROM（`SELECT |`），会直接推荐 CTE 的列 |
| 树形选列 | 在没有 FROM 的 `SELECT |` 处，列表呈树形：CTE 和所有表。按 ▸（或 → / ←）展开查看列，按 ☐（或 Shift+Space）可多选，然后 Enter / Tab 会一次性生成 `SELECT ... FROM ... JOIN ...`（按外键或同名的 `...Id` 列生成 JOIN）。什么都不勾 = 整表或所选列 |
| 展开 INSERT / UPDATE / EXEC / ALTER | 把对象名写到行尾后按 **Tab**：`INSERT INTO t` → 列名列表 + `VALUES`，并按数据类型给出默认值（跳过 IDENTITY / computed / rowversion）· `UPDATE t` → 所有非 PK 列的 `SET` + 按 PK 生成的 `WHERE`（用 `@parameter`，复合键用 `AND` 连接，并在上方 `DECLARE ... = NULL`；没有 PK 时只留 `WHERE /* TODO */`，在补上条件前无法执行）· `EXEC p` → 全部 `@param = 值`，顺序一一对应（OUTPUT 参数会在上方 `DECLARE` 变量）· `ALTER PROCEDURE / FUNCTION / VIEW / TRIGGER p` → 从数据库取出当前代码，整段变为 `ALTER ...`。如果补全列表正打开着，第一次按 Tab 会先选中列表项，再按一次 Tab 才会展开 |
| INSERT 中的列高亮 | 把光标放在 `VALUES (…)` 的某个值上，`INSERT INTO t (a, b, …)` 中同位置的列名会加底纹，该值周围显示蓝色边框（多行 VALUES 时参照光标所在行）· 光标在列名上 = 为每一行中同位置的值加底纹 · 按文本位置顺序配对，不连接数据库，仅适用于带列名并使用 `VALUES` 的 INSERT（不含 `INSERT … SELECT` / `EXEC`）· 可在 选项 › 编辑器 中关闭 |
| 列选择器 | 在已有 FROM 的 select 列表处（`SELECT | FROM Person.PersonPhone pp JOIN Person.Person p ...`），在 `SELECT` / `,` 之后或按 Ctrl+Space，会按别名列出各表的树（已展开）。按 ☐（或 Shift+Space）后，`p.FirstName` 会立即写入 select 列表（每行列一个，逗号在行首）。取消勾选 = 删除。可输入以筛选列名，Enter / Tab / Esc 关闭列表 |
| 存储过程 / 函数 | 在 `EXEC ` 之后（或 `EXEC dbo.`、`EXEC @rc = `）列表会给出存储过程和函数（不是表）。选中后侧边显示：`dbo.ufnGetProductDealerPrice (Function) returns MONEY`，以及所有参数、数据类型、INPUT / OUTPUT / READONLY 和说明（MS_Description）· 标量函数在普通列表中推荐（用于表达式）· 表值函数在 `FROM` / `JOIN` 之后推荐 |
| 变量 / 参数 | 输入 `@` 会推荐上方 `DECLARE` 的变量，以及 `CREATE/ALTER PROCEDURE / FUNCTION` 的参数，附带数据类型和 INPUT / OUTPUT（只统计同一批次内，即最后一个 `GO` 之后） |
| 列信息 | 在补全列表中选择列，可看到数据类型、PK / IDENTITY / NOT NULL、数据库中的说明（MS_Description）以及指向哪张表的外键 |
| 自动 ON 条件 | 输入 `JOIN Work_InitialApproval wia ON ` 会得到 `wr.ReqId = wia.ReqId`，先按外键排序，再按同名（`...Id` 列优先）。在 `AND` 之后同样可用 |
| 模糊搜索 | `custid` → `CustomerID`，`od` → `OrderDetails` |
| 格式化 SQL | **工具 › SqlAssist › 格式化 SQL**、在查询编辑器中右键 › **格式化 SQL (SqlAssist)**，或 **Ctrl+K, Ctrl+Shift+F**。格式化所选内容（未选择时格式化整个查询窗口），保留注释、字符串、提示和 `GO`。可在 **工具 › SqlAssist › 选项...** 中调整，见 [格式化 SQL](#格式化-sql) |
| SQL 风险检查 | **工具 › SqlAssist › 检查 SQL 风险...** 在所选内容（或整个查询窗口）中检查没有 `WHERE` 的 `UPDATE` / `DELETE`、`TRUNCATE TABLE`、`DROP TABLE`。点击列表项可跳转到该语句，不会修改也不会执行 SQL。见 [SQL 风险检查](#sql-风险检查) |
| CSV 转 SQL | **工具 › SqlAssist › CSV 转 SQL...** 把从 Excel 复制的数据或 CSV 文本转换为 `IN (...)`、`VALUES (...)` 或 `CREATE TABLE #Temp` + `INSERT`。见 [CSV 转 SQL](#csv-转-sql) |
| 代码片段 | `ssf` + **Tab** → `SELECT * FROM `，随后弹出表名列表供继续选择 |

## 安装前（只需做一次）

1. **关闭 SSMS 自带的 IntelliSense**：工具 › 选项 › 文本编辑器 › Transact-SQL › IntelliSense › 取消勾选 *启用 IntelliSense*
2. **卸载或禁用 dbForge SQL Complete**，因为它同样会抢占 Tab 键和 IntelliSense

## 安装

双击 **`dist\SqlAssistSetup-<version>.exe`**，会打开安装窗口。

- **从未安装过：** 点击 **安装** 按钮
- **已安装：** 可选择 **更新**（版本相同时为重新安装）或 **卸载**
- 也可以在 **控制面板 › 程序和功能**（或 设置 › 应用）中卸载：选择 **SqlAssist for SSMS 22** 后点击卸载
- **如果 SSMS 正在运行：** 会显示黄色提示条且按钮不可用。关闭所有 SSMS 窗口后，按钮会在 1 秒内自动可用

### 检查版本
- 安装程序会显示已安装版本与安装包版本的对比，并说明有新版本还是已是最新
- 在 SSMS 中：**工具 › SqlAssist › 关于...**
- 在日志中（工具 › SqlAssist › 选项... › 打开日志文件），每次打开查询窗口都会有一行 `SqlAssist x.y.z attached`
- 发布新版本前，只需修改 `Directory.Build.props` 中的版本号，然后运行 `.\build-setup.ps1`

### 在线更新（自 1.3.0 起）
- 启动 SSMS 时每天检查一次 [GitHub Releases](https://github.com/kriangdet-chuaboonjun/SSMS22SqlAssist_Addon/releases)，有新版本时会在 SSMS 窗口顶部显示提示条
- 也可在 **工具 › SqlAssist › 检查更新...** 中手动检查
- 提示条中的按钮：
  - **关闭 SSMS 时安装**（自 1.3.3 起）—— 先下载并校验 SHA-256，然后静默等待；关闭所有 SSMS 窗口后自动安装，下次启动 SSMS 即为新版本（状态栏显示 “SqlAssist updated to ...”）。安装失败会在下次启动 SSMS 时提示。若在关闭 SSMS 前重启了电脑，会在下次启动 SSMS 时重新排队
  - **立即安装** —— 把安装包下载到 `%LOCALAPPDATA%\SqlAssist\updates\`，并与 GitHub 提供的值校验 SHA-256，然后打开安装窗口，请 **关闭 SSMS 后点击更新**
  - **更新内容** —— 打开 release 页面查看新内容
  - **跳过此版本** —— 不再提示该版本
  - **不再自动检查** —— 关闭自动检查（检查更新菜单仍可正常使用）。如需恢复，删除 `%LOCALAPPDATA%\SqlAssist\update.txt`
- 各版本的新增与修复见 [CHANGELOG.md](CHANGELOG.md) 或 [Releases](https://github.com/kriangdet-chuaboonjun/SSMS22SqlAssist_Addon/releases) 页面（提示条中的 **更新内容** 链接）
- 发布新版本（开发者）：把改动写进 `CHANGELOG.md` → 修改版本号 → `.\build-setup.ps1` → commit 并 push → 设置 `$env:GITHUB_TOKEN` → `.\publish-release.ps1`（release 说明取自该版本的 CHANGELOG）

- exe 中已内嵌插件，复制到其他装有 SSMS 22 的机器即可使用，无需额外安装
- 按用户安装，无需管理员权限；若 SSMS 正在运行会先请求关闭
- 可覆盖安装（用于升级版本）
- 可用命令行无界面运行：`SqlAssistSetup-<version>.exe /install` 或 `/uninstall`（退出码：0 成功，1 失败，2 SSMS 正在运行，3 未找到 SSMS 22）

重新打开 SSMS 后会看到 **工具 › SqlAssist: ...** 菜单

修改代码后可用 `.\build-setup.ps1` 重新生成 exe（运行测试 → 构建 → 输出到 `dist\`）
如果不想用 exe，也可以改用 `.\install.ps1` / `.\uninstall.ps1` 脚本

## 快捷键

| 按键 | 作用 |
|---|---|
| Tab | 展开代码片段 → 若补全列表已打开则选中该项 → 展开 `*` → 都不满足时为普通 Tab |
| Enter | 选中补全列表项；若已输入完整关键字（如 `from`），选中后还会换行 |
| Space、`,`、`(` 等 | 关闭补全列表，且 **不** 选中任何项 |
| Ctrl+Space | 手动打开补全列表 |
| Ctrl+Shift+R | 重新加载架构（或使用 工具 › SqlAssist › 刷新架构缓存） |
| F12 | 跳转到光标处名称的定义（或右键 › 转到定义）：表会在对象资源管理器中打开，视图 / 存储过程 / 函数会在新的查询窗口中打开脚本（SQL Server 保存的 `CREATE ...`），不会执行。仅适用于 SqlAssist 已加载架构中的对象（当前数据库的表、视图、存储过程、函数）。其他情况，例如关键字、变量、列、CTE、内置函数、`#temp`、指定了其他数据库的名称（`OtherDb.dbo.T`）或架构尚未加载完成，会按原样交给 SSMS 处理 |
| Ctrl+K, Ctrl+Shift+F | 格式化所选 SQL（或整个查询窗口），见 格式化 SQL |

| Ctrl+J 然后 I / T / W / O / U | （在结果网格中）把所选单元格复制为 INSERT / INSERT #tmpTable / WHERE IN() / WHERE AND OR / UPDATE 脚本（UPDATE 会先打开窗口选择表和 Key），见下方 生成脚本 |

架构会在每个数据库首次使用时加载，然后缓存 10 分钟。
若查询中出现缓存还不认识的表名（例如刚创建的），会自动重新加载，两次加载至少间隔 30 秒。

## 从结果网格生成脚本

在查询结果中选择单元格（可拖动选择多行多列，或按住 Ctrl 选择多个区域），然后右键 ›
**生成脚本** › 语句类型 › **新建查询窗口**（在相同服务器上打开新查询窗口）、
**剪贴板** 或 **文件...**（保存为 `.sql`）

| 类型 | 生成内容 |
|---|---|
| INSERT | `INSERT INTO [Table] (所选列) VALUES (...), (...)`，每 1,000 行一条语句 |
| INSERT #tmpTable | 按列的数据类型 `CREATE TABLE #tmpTable`，然后 INSERT、`SELECT * FROM #tmpTable` 和 `DROP TABLE` |
| WHERE IN() | `WHERE col IN(...)`，值不重复；选择多列时用 `AND` 连接 |
| WHERE AND OR | `WHERE (a = ... AND b = ...) OR (...)`，每行一个括号 |
| UPDATE | 打开窗口指定目标表并选择 SET / Key 列，生成逐行的 `UPDATE 表 SET ... WHERE <Key> = ...` 后跟 `GO`（见下方 [UPDATE](#update选择表和-key)） |

- INSERT 的表名是 `[Table]`，请自行改成真实表名（SSMS 的结果集不会说明列来自哪张表）
- NULL 值写成 `NULL` / `IS NULL`，文本写成 `N'...'`（varchar 为 `'...'`），日期写成 `'yyyy-MM-dd HH:mm:ss.fff'`（脚本开头带 `SET DATEFORMAT ymd`），二进制写成 `0x...`
- 每次最多 100,000 行
- 快捷键（直接复制到剪贴板）：在网格中点击后按 **Ctrl+J**，再按 **I** / **T** / **W** / **O** / **U**（**U** 会先打开 UPDATE 窗口选择表和 Key，目标默认为剪贴板）

### UPDATE：选择表和 Key

**UPDATE** 菜单（以及 Ctrl+J 然后 U）会打开窗口让你自行指定，SqlAssist **不会** 从结果集推测表名或 key：

- **目标表名称：** 输入框默认为空，例如输入 `dbo.Orders` 或 `[Sales].[Order Line]`（最多可加 database 前缀，共 3 段）。系统会校验名称并在需要时加上 `[ ]`。含空格或特殊字符却没有用 `[ ]` 括起来的名称会被拒绝
- **SET / Key：** 结果集的每一列都可勾选为 SET（要修改的列）或 Key（`WHERE` 中用于定位行的列）。网格中选中的列默认为 SET，Key 默认为空。把某列勾为 Key 时会先从 SET 中移除，但可以再勾回 SET
- **多个 Key：** 用 `AND` 连接；Key 值为 NULL 时写成 `IS NULL`
- **预览：** 发送前显示脚本的前 10 行，目标可选 新建查询窗口 / 剪贴板 / 文件
- **信息不完整时无法生成：** 必须有有效的表名、至少 1 个 SET 列和至少 1 个 Key
- **重复 Key 警告：** 若所选行中存在相同的 Key 值，会提示多行使用相同的 `WHERE` 条件（按将要写入脚本的值比较）
- **所选 Key 不保证在真实数据库中唯一：** SqlAssist 不会与真实表校验。若 Key 在表中重复，UPDATE 会影响多行，请在执行脚本前检查 PRIMARY KEY / UNIQUE

## 格式化 SQL

**工具 › SqlAssist › 格式化 SQL** 或查询窗口中的 **Ctrl+K, Ctrl+Shift+F**（若当前窗口不是 T-SQL，命令会显示为灰色）

- **格式化范围：** 有选中文本 → 只格式化该部分（后续行按选中起始行缩进）；没有选中 → 格式化整个文档（不支持列块选择）
- **撤销：** 按一次 **Ctrl+Z** 即可撤销整批格式化，光标或选区保持在原文本上，且只改动真正不同的片段（其他位置的书签/断点不会丢失）
- **完整格式化的语句：** SELECT（DISTINCT / TOP / INTO）、所有 JOIN 以及 CROSS / OUTER APPLY、WHERE / HAVING（AND / OR 换行，`BETWEEN … AND` 不会被拆开）、GROUP BY / ORDER BY / OFFSET … FETCH、UNION / EXCEPT / INTERSECT、CASE（2 个及以上 WHEN 时换行）、子查询和派生表、CTE（`WITH … AS (…)`）、INSERT / UPDATE / DELETE / MERGE（含 OUTPUT、OPTION），以及决定内部语句缩进的 `IF / ELSE / WHILE / BEGIN … END / TRY … CATCH`
- **其他语句**（DECLARE、SET、EXEC、CREATE TABLE / PROCEDURE …）：只调整空格、缩进和关键字大小写，保留你原有的 **换行**
- **保证不变的内容：** 注释（`--` 和 `/* */`）、`'...'` 中的文本、`[...]` / `"..."` 中的名称、提示（`WITH (NOLOCK)`、`OPTION (...)`）、`GO`（每行一条）、变量 `@x`、临时表 `#t`、数字 / 金额 / 十六进制。只有 **空白、关键字大小写，以及移到行另一侧的 `,`** 会改变
- **关键字大小写：** 只改变 T-SQL 保留字，以及位置明确表明不是标识符的函数名/数据类型名（例如 `COUNT(`、`CAST(x AS INT)`、`@a INT`、`OFFSET … ROWS`）。表名、列名和别名绝不会被改动（因此区分大小写的数据库不会出问题）。点号后的名称，如 `t.Key`，也不会改
- **设置** 位于 **工具 › SqlAssist › 选项...**（附随选项变化的示例）：关键字大小写（大写 / 小写 / 保持原样）、缩进 2–8 个空格、逗号放在行首或行尾。配置保存在 `%APPDATA%\SqlAssist\settings.xml`
- **可重复：** 执行多少次结果都相同
- **无法格式化时：** 若 SQL 不完整（`'`、`[`、`/*` 或括号未闭合），会提示出问题的行和列，并 **不修改文本**。每次格式化后都会校验所有标记和注释仍按原顺序存在（`GO` 分批也一致），不一致时同样不修改
- **快捷键：** 选择 Ctrl+K, Ctrl+Shift+F，是因为已确认不冲突：Visual Studio 的默认快捷键（Ctrl+K, Ctrl+F / Ctrl+D 是 VS 的格式化）、SSMS 22 的配置文件（SSMS 自带的格式化 SQL 是 Ctrl+K, Ctrl+Q，仍可照常使用；Copilot 使用 Ctrl+Alt+E/R/D/F；演示者模式使用 Ctrl+Shift+F5）、本机快捷键设置文件，以及 SqlAssist 自身的按键（Tab、Ctrl+Space、Ctrl+Shift+R、Ctrl+J 然后 I/T/W/O/U）。如果你有其他扩展使用了该组合，可在 工具 › 选项 › 环境 › 键盘 中修改（搜索 `SqlAssist.FormatSql`）

## SQL 风险检查

**工具 › SqlAssist › 检查 SQL 风险...**（在 T-SQL 查询窗口中）检查所选内容，或未选择时的整个查询窗口，并在 **结果窗口** 中列出结果，且不会遮挡编辑器：命中的规则、位置（行、列）、语句和原因。**点击某行**（或按 Enter）即可在编辑器中选中该语句，位置会随之更新，即使检查后修改了文本

| 规则 | 触发条件 |
|---|---|
| UPDATE 无 WHERE | 自身没有 `WHERE` 的 `UPDATE`（含 `UPDATE a SET … FROM t a JOIN …` 形式） |
| DELETE 无 WHERE | 自身没有 `WHERE` 的 `DELETE`（含 `DELETE a FROM t a JOIN …` 和 `DELETE TOP (n)`） |
| TRUNCATE TABLE | 所有 `TRUNCATE TABLE` 语句 |
| DROP TABLE | 所有 `DROP TABLE` 语句（含 `IF EXISTS` 和多表），不含 `DROP VIEW / INDEX / PROCEDURE` 和 `ALTER TABLE … DROP COLUMN` |

- **按语句解析，而不是搜关键字：** 子查询、CTE、`'...'` 文本、注释或 `[where]` 名称中的 `WHERE` 不会被算作外层语句的 `WHERE`。`IF`、`BEGIN … END`、`TRY … CATCH` 中的语句会按 `GO` 分批检查
- **不会修改 SQL，也不会补上 WHERE**，更不会执行任何内容
- **有 WHERE 不代表语句安全**（WHERE 可能范围过大或条件写错），未发现问题也不代表安全。该说明始终显示在结果窗口中
- **无法检查的内容：** 在 `EXEC (...)` / `sp_executesql` 中拼接的 SQL、被调用的存储过程、触发器、视图，以及 `MERGE`。若 SQL 不完整（字符串、注释、括号未闭合），会检查到可解析的位置并注明
- **设置** 位于 **工具 › SqlAssist › 选项... › 风险检查：** 可逐条启用/禁用规则，并选择跳过临时表（`#t`、`##t`）和表变量（`@t`）（默认全部检查）

### 执行前提示（实验性，默认关闭）

在 **选项 › 风险检查 › 执行前提示** 中开启。开启后，每次执行时若将要运行的脚本（所选内容或整个查询窗口）命中规则，会弹出窗口显示 **服务器、数据库** 和风险语句，并提供 **取消**（默认：Enter、Esc 和关闭窗口都会取消）和 **仍然执行**。没有命中时照常运行，不做询问，且 **取消查询不受影响**

- **机制：** 注册为 SSMS 的优先级命令目标，从而先于实际目标收到所有命令。执行命令的名称在启用时探测（尝试 `Query.Execute`、`Query.ExecuteQuery`、`Query.ExecuteSelection`），并记录到日志（工具 › SqlAssist › 选项... › 打开日志文件，行 `Execute guard hooked: …`；在测试过的 SSMS 22 中只找到 `Query.Execute`）。若找不到，会在选项页面说明尚未拦截
- **已知限制：** 仍属实验性。已在 SSMS 22 中测试（测试机、SQL Express 服务器、仅使用临时表），对 F5、Ctrl+E、工具栏 Execute 按钮、查询 › 执行 菜单以及仅执行选区，都会弹出提示；取消/Enter/Esc 不会执行任何内容，仍然执行只运行一次，取消查询仍正常。但 **尚未验证 SSMS 中所有执行查询的入口**，也不能保证没有风险语句被漏过。若需要确定性，请先用 检查 SQL 风险 自行检查。它完全不修改 SSMS 的快捷键，关闭该选项后不会注册任何内容（不影响性能）
- **自行验证（不触碰真实数据）：** 开启该选项后，在连接到测试服务器的查询窗口中输入 `CREATE TABLE #t (a INT); DELETE FROM #t; DROP TABLE #t;`，然后用所有方式执行 —— **F5**、**Ctrl+E**、工具栏 **Execute** 按钮、**查询 › 执行** 菜单 —— 分别在不选择和只选中 `DELETE FROM #t` 的情况下测试。每次都应弹出提示，点击 **取消** 后不应有任何执行（该表仅为会话级临时表）

## CSV 转 SQL

通过 **工具 › SqlAssist › CSV 转 SQL...** 打开，把从 Excel 复制的数据或 CSV 文本转换为 SQL

- **文本框：** 打开窗口时会粘贴剪贴板中的内容（**从剪贴板粘贴** 按钮可再次读取），可在此框中修改，预览会自动更新。支持按 Excel/CSV 规则用 `"` 包裹的单元格（内含分隔符、换行或 `""`）
- **单元格分隔符：** 默认为 **Tab**（Excel 放入剪贴板的内容）。若是 CSV 文本，请自行选择 **逗号 `,`**、**分号 `;`** 或 **竖线 `|`**。系统不会从文本推测分隔符，因为只有一列且文本中含 `,`（例如 `Smith, John`）时会被错误拆分。使用 `,` 作小数点的数字（欧洲式 CSV）按下列规则无法读取，会报错
- **首行为列名：** 需自行勾选。不勾选时所有行都是数据，列名为 `Column1`、`Column2`、...
- **三种输出形式**

  | 形式 | 生成内容 |
  |---|---|
  | `IN (...)` | 所选单列的 `IN (N'00123', N'00456')`。可勾选 **去除重复值**。NULL 值会被跳过（IN 无法匹配 NULL）并提示跳过数量。超过 10 个值时每 10 个换行 |
  | `VALUES (...), (...)` | `VALUES` 后每行一条，按 SQL Server 限制每批不超过 1,000 行 |
  | `CREATE TABLE #Temp + INSERT` | 按所选数据类型创建 `#Temp`（可自行命名为 `#...` / `##...`），`nvarchar(n)` / `decimal(p,s)` 的大小按实际数据计算，然后 `INSERT`，每 1,000 行一批（复用 生成脚本 的 INSERT 写入器） |

- **各列的数据类型：** 默认全部为 *文本 (nvarchar)*，以免 `00123` 变成 `123`。可选择 varchar、int、bigint、decimal、float、bit、date、datetime、datetime2、uniqueidentifier。若数据与所选类型不符，会提示 **第 N 行，第 M 列** 及错误的值（行号包含表头行），并在修正前不生成 SQL，绝不会静默转换
- **NULL 规则（明确分为三种）：**
  - *空单元格*（分隔符之间没有任何内容）→ 可选择为 `NULL`（默认）或空字符串 `''`
  - *文本 NULL*（全大写的 `NULL`，例如 SSMS 从网格复制时显示的 NULL）→ 可选择为 `NULL`（默认）或文本 `'NULL'`。`Null`、`null`、` NULL` 一律视为普通文本（避免把 Null 这样的人名当成 NULL）
  - 若选择存为空字符串/文本 `NULL`，但该列是数字或日期类型而无法存储，会报错
- **数字和日期不受本机区域设置影响：** 数字必须是阿拉伯数字并用 `.` 作小数点（不能有千分位 `,`，decimal 中不能用 `1e5`）。日期必须符合所选格式（`yyyy-MM-dd`、`dd/MM/yyyy` 或 `MM/dd/yyyy`），且 `date` 不能带时间，`datetime` 小数位不超过 3 位（超过请用 `datetime2`）。看起来像佛历年份（2400–2700）的值会给出警告。结果中的日期写成 ISO 格式（`'2024-01-31T13:05:09.000'`），因此不需要 `SET DATEFORMAT`
- **预览与输出：** 预览显示前 50 行（但已校验所有行），可发送到 **剪贴板** 或 **新建查询窗口**。SqlAssist 不会执行 SQL
- **限制（避免 SSMS 卡死）：** 文本不超过 2,000,000 个字符、100,000 行、256 列，且每行的列数必须与首行相同（不会自动补齐）
- **隐私：** 剪贴板内容不会被写入日志

## 搜索对象

通过 **工具 › SqlAssist › 搜索对象...** 或工具栏 **SqlAssist** 上的 **SqlAssist** 按钮（Logo）打开（若看不到工具栏，右键工具栏区域勾选 SqlAssist，或前往 视图 › 工具栏）。窗口会停靠在输出窗口旁成为标签页，也可拖到别处

- **边输边搜：** 输入至少 2 个字符后自动出结果（按 Enter 立即搜索，按 ↓ 跳到结果表格）
- **搜索范围：** 对象名称，以及视图 / 存储过程 / 函数 / 触发器的定义文本；表名和列名只按名称搜索
- **对象类型（下拉框 “所有对象类型”）：** 可勾选 Schema、Table、View、Procedure、Function、Trigger、Column、Index、Constraint（primary key、unique、foreign key、check、default）和 User-defined type（别名类型和表类型）。后三种只按名称搜索，预览为简化脚本（`CREATE INDEX`、`ALTER TABLE ... ADD CONSTRAINT`、`CREATE TYPE`）
- **搜索选项（⚙ 按钮）：** 区分大小写、全字匹配、使用通配符（`*` = 同一行内任意多个字符，`?` = 单个字符）。全部不勾选时为“包含该词”，且不区分大小写
- **数据库（下拉框）：** 默认为当前数据库。可选择多个，或选择 **所有数据库**（不含系统数据库）。系统逐个数据库加载，一有结果就显示；离线或无权限的数据库会被跳过并记入日志
- **服务器（下拉框）：** 默认为“当前查询窗口”（使用活动查询窗口的服务器），也可选择本会话中连接过的其他服务器。若活动查询窗口切换到其他服务器，会自动改回使用该窗口的服务器
- **跳转到对象：** 双击或按 **Enter**，对象资源管理器会展开到该对象并选中它（若尚未连接该服务器会自动连接）。**Ctrl+Enter** 会打开新查询窗口，包含 `USE [db]` 和定义，并选中匹配的词。右键结果有 在对象资源管理器中定位、在新查询窗口中打开 和 复制名称（Ctrl+C）
- **结果：** 表格列为 名称、架构、类型、所属对象、数据库、匹配位置。点击列头可排序，最多显示 5,000 条
- **预览：** 显示定义并高亮匹配词。表会显示简化的 `CREATE TABLE`（列、类型、NULL/NOT NULL、主键）；列会显示其所在表/视图的内容

用于搜索的数据按数据库加载到内存并缓存 10 分钟（超过 10 分钟后在后台重新加载）。如需立即重新加载，点击窗口中的 **刷新** 按钮，或按 Ctrl+Shift+R / **工具 › SqlAssist › 刷新架构缓存**

每次重新启动 SSMS，搜索窗口都会回到默认状态（所有对象类型、不勾选搜索选项、当前数据库）。所选设置仅在本次 SSMS 会话中有效

## 编辑代码片段

前往 **工具 › SqlAssist › 编辑代码片段...**，会打开 **代码片段管理器**

- **左侧列表：** 有搜索框，可按快捷键或按说明排序
- **右侧：** 编辑代码片段的 **快捷键**（输入后按 Tab 的词）、**说明** 和 **内容**
- **占位符：** 双击可插入到内容中，展开代码片段时会被替换

  | 占位符 | 替换为 |
  |---|---|
  | `$CARET$` | 展开代码片段后的光标位置 |
  | `$DATE$` / `$TIME$` | 当前日期 / 时间 |
  | `$USER$` | Windows 用户名 |
  | `$DB$` / `$SERVER$` | 查询窗口的数据库 / 服务器 |

- **顶部按钮：** 新建代码片段、删除、保存 (Ctrl+S)、撤销修改（重新从文件加载）和 打开文件夹
- **保存时的校验：** 快捷键不能为空、不能重复、不能含空格；内容不能为空
- **立即生效：** 保存后即可使用，无需重启 SSMS
- **未保存就关闭窗口：** 会先询问是否保存

代码片段保存在 `%APPDATA%\SqlAssist\snippets.xml`，也可以手动编辑该文件。使用 `$end$` 的旧文件仍然可用（含义与 `$CARET$` 相同）。多行代码片段会按输入快捷键的那一行缩进

## 限制（v1）

- 仅支持 Windows 身份验证和 SQL 身份验证，尚不支持 Entra ID / Azure AD
- 数据库取自查询窗口的连接（SSMS 的 `AdvancedOptions["DATABASE"]`）。若脚本中光标上方有 `USE 数据库名`，则改用该数据库（光标上方最近的一个），但只能识别距光标约 20,000 个字符以内的 `USE`，更远时会回到窗口的数据库
- 按 `;`、`GO` 或空行分隔语句。若多个查询连写且没有分隔符，其他查询的表也可能出现在列表中
- 表值函数的列仅来自数据库中的函数。SQL Server 的内置函数（`STRING_SPLIT`、`OPENJSON` 等）尚不支持，新建的 TVF 需要先按 **Ctrl+Shift+R**
- 由表达式计算出的 CTE 列（例如 `YEAR(OrderDate) AS SalesYear`）没有数据类型，只有直接从表取出的列才有类型
- 搜索：尚不支持表内数据搜索，也不含 columnstore / XML / spatial 索引。约束按名称搜索，不搜索内部条件；表和约束的预览/脚本是简化的 `CREATE TABLE`，不是 SSMS 的完整脚本。在查询窗口中打开的定义是 SQL Server 保存的 `CREATE` 原文，如需修改请自行改成 `ALTER`
- 搜索：若没有活动查询窗口，会使用最近一次见到的连接

## 遇到问题时

- **工具 › SqlAssist › 选项... › 打开日志文件** 会打开 `%LOCALAPPDATA%\SqlAssist\SqlAssist.log`，其中记录所有错误（在 SSMS 中必须吞掉所有错误，以免编辑器崩溃）
- 若看不到 SqlAssist 菜单，请关闭 SSMS 后运行 `.\clear-ssms-cache.ps1`

## 开发者

```
src/SqlAssist.Core     纯逻辑（netstandard2.0），不依赖 VS：tokenizer、上下文、语句扫描、SQL 格式化、剪贴板 → SQL、模糊匹配、别名、JOIN、*、代码片段、搜索匹配、CREATE TABLE 预览、设置、更新策略
src/SqlAssist.Vsix     与 SSMS 集成的部分（net48）：补全源、项/提交管理器、Tab 过滤器、架构缓存、搜索工具窗口 + 索引缓存、更新检查
tests/                 Core 的 xUnit 测试
```

- `dotnet test tests\SqlAssist.Core.Tests`
- `dotnet build src\SqlAssist.Vsix -c Release` 会生成 `src\SqlAssist.Vsix\bin\Release\net48\SqlAssist.vsix`
- 调试：启动程序已设为 `Ssms.exe`（SSMS 22 没有实验实例，因此需先安装再附加调试或按 F5）
- 构建时不需要 SSMS：SSMS 的连接是通过反射读取 `SQLEditors.dll`，仅在运行时需要，因此构建时不引用 SSMS 的 DLL
- 数据库连接使用 .NET Framework 自带的 `System.Data.SqlClient`，无需在 VSIX 中附带额外 DLL

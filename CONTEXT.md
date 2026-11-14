# SqlAssist

An SSMS 22 add-in with editor completion, snippets and a database-object search window, aimed at people who are replacing dbForge SQL Complete and dbForge Search.

## Connection

**Active Connection**:
The server, database and login of the query window SSMS considers active. Everything SqlAssist reads from a database goes through it.
_Avoid_: Current connection, session

**Access Token**:
For a query window connected with Microsoft Entra ID, the token SSMS holds for that connection. SqlAssist asks SSMS for it each time it opens a connection and never stores, renews or logs it.
_Avoid_: Password, credential (an Entra connection has neither in its connection string)

**Schema**:
For completion, the tables, views, columns and foreign keys of one database. In Search, a database schema (dbo, Sales) that is itself an object you can find.
_Avoid_: Metadata, model (when talking to users)

## Search

**Search Index**:
The in-memory snapshot of one database's searchable objects, with names and module definitions. There is one per server, login and database, and it goes stale.
_Avoid_: Cache (when you mean what is searched), catalog

**Search Item**:
One object in a Search Index: a schema, table, view, procedure, function, trigger, column, index, constraint or user-defined type, with its database, schema, owning object and definition.
_Avoid_: Result, entry, row

**Object Type**:
The kind of Search Item (Schema, Table, View, Procedure, Function, Trigger, Column, Index, Constraint, User-defined type). The user filters the search by it. A constraint or user-defined type also has a sub-kind (Check, Default, ForeignKey, PrimaryKey, Unique; Alias, Table).
_Avoid_: Data type (that is a column's SQL type), type

**Definition**:
The source of a view, procedure, function or trigger as SQL Server stores it. It is searched and shown in the preview. Tables, columns and schemas have no Definition.
_Avoid_: DDL, body, code

**Table Script**:
A short CREATE TABLE that SqlAssist generates to preview a table, or a column's table. It is shown but never searched.
_Avoid_: Definition (for tables)

**Search Query**:
What the user asks for: the text, the match options (match case, whole word, wildcards) and the Object Types to include.
_Avoid_: Filter, search string

**Match**:
A Search Item that a Search Query hit, with where it hit (Name, Definition or both) and the ranges to highlight.
_Avoid_: Result, hit (for the whole item)

**Usage**:
An object that refers to another one (the Usage Target): a view, procedure, function, trigger, table (by foreign key, computed column or constraint) or synonym. It carries how SqlAssist knows (Dependency, Foreign key, Trigger, Synonym, Name only, Text match) and where the target's name appears in its Definition.
_Avoid_: Reference, dependency (that is only one kind of evidence)

**Usage Target**:
The object whose Usages are wanted: a table, view, procedure, function, user-defined type, or a column (found through its owner, by text). Usages cover its own database only.
_Avoid_: Referenced object

**CRUD Procedure Set**:
The Select, Insert, Update and Delete stored procedures generated for one table from its columns and primary key. Only generated and delivered as text, never executed; a table without a primary key gets no Update or Delete.
_Avoid_: CRUD script, stored procedure template

## Explain SQL

**Explain Request**:
What Explain SQL sends to the AI service: the selected SQL (literals masked unless turned off, lines numbered) and the Schema of the tables it names, if already loaded. Never data, results, server, database or login. The same text is shown to the user before the first send.
_Avoid_: Prompt (that is only part of it), query (that is the SQL)

**Finding**:
One point in the answer to an Explain Request: a line (or the whole script), a severity (Warning or Info), a title and a detail. Judged from the text alone, so it is advice to check, not a measured fact.
_Avoid_: Risk (that is Check SQL Risks), error, issue

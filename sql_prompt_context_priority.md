# Redgate SQL Prompt — Bağlama Göre Öncelik Sıralaması

SQL Prompt, önerilerin listelenme sırasını sorgunuzun **bağlamına (context)** göre değiştirir.
Nerede olduğunuzu parse ederek o konuma en mantıklı nesne tipini öne çıkarır.

---

## Bağlam → Öncelik Tablosu

| Konum | Öncelikli Öneriler |
|---|---|
| `USE` sonrası | Databases |
| `SELECT ... FROM` sonrası | Tables → Views → Schemas |
| `JOIN ... ON` sonrası | İlişkili kolonlar (FK/PK önce) |
| `WHERE` sonrası | Kolonlar → Variables → Keywords |
| `ORDER BY` sonrası | SELECT'teki kolonlar |
| `EXEC` / `EXECUTE` sonrası | Stored Procedures |
| Batch başlangıcı | Keywords (SELECT, INSERT, UPDATE...) |

---

## Detaylı Açıklamalar

### `USE` Sonrası
Veritabanı adı beklenen bu konumda öneri listesi **yalnızca Databases** ile başlar.

```sql
USE AdventureWorks  -- AdventureWorks, Sales, Northwind... listesi gelir
```

---

### `SELECT ... FROM` Sonrası
Tablo beklenen bağlamda öncelik sırası şöyledir:

1. **Tables** (kullanıcı tabloları)
2. **Views**
3. **Schemas**
4. **Database adları**

```sql
SELECT * FROM  -- Tables önce, Views sonra gelir
```

---

### `JOIN ... ON` Sonrası
SQL Prompt FK/PK ilişkisini tanır; **Foreign Key ve Primary Key kolonları** listenin üstüne taşınır.

```sql
SELECT *
FROM orders o
INNER JOIN customers c ON o.  -- o.CustomerId (FK) en üstte çıkar
```

---

### `WHERE` Sonrası
Filtre koşulu beklenen bu konumda:

1. **Kolonlar** (FROM / JOIN'deki tablolara ait)
2. **Variables / Parameters** (`@variable`)
3. **Keywords** (AND, OR, NOT, EXISTS...)

```sql
SELECT * FROM employees WHERE  -- salary, department_id, first_name... gelir
```

---

### `ORDER BY` Sonrası
`SELECT` listesinde zaten yazılmış kolonlar **öncelikli** olarak gösterilir; sonrasında tablonun diğer kolonları listelenir.

```sql
SELECT first_name, salary FROM employees ORDER BY  -- first_name, salary üstte
```

---

### `EXEC` / `EXECUTE` Sonrası
Prosedür adı beklenen bu konumda **Stored Procedures** listesi öncelikli gelir.

```sql
EXEC  -- sp_helpdb, usp_GetEmployees... listesi gelir
```

---

### Batch Başlangıcı
Henüz hiçbir şey yazılmamış veya yeni bir batch'e geçilmişse SQL **Keywords** önceliklidir.

```sql
-- Boş satırda yazmaya başlayınca:
-- SELECT, INSERT, UPDATE, DELETE, CREATE, ALTER, DROP... gelir
```

---

## Özet

```
Context Parse Akışı:

  SQL Prompt sorgu pozisyonunu okur
        ↓
  Beklenen nesne tipini belirler
        ↓
  O tipteki nesneleri listenin üstüne taşır
        ↓
  Geri kalanlar Ranked Suggestions + alfabetik ile sıralanır

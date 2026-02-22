# SQLsense - Intelligent SQL Assistant for SSMS 22

SQLsense, SQL Server Management Studio (SSMS) 22 için geliştirilmiş, geliştirici verimliliğini artırmayı hedefleyen akıllı bir sorgu yardımcısıdır. Redgate SQL Prompt benzeri bir deneyim sunarak kod yazım sürecini hızlandırır ve standartlaştırır.

## 🚀 Mevcut Özellikler (Phase 1 & 2)

### 1. Real-Time Keyword Casing (Canlı Yazım Desteği)
- **Anlık Dönüşüm:** Siz kod yazarken `select`, `from`, `truncate` gibi 180'den fazla T-SQL anahtar kelimesi, Boşluk (Space), Tab veya Enter tuşuna bastığınız anda otomatik olarak büyük harfe çevrilir. (KeywordManager desteğiyle ultra hızlı).
- **Kültürel Uyumluluk:** Her zaman standart `JOIN` çıktısı üretilir, Türkçe karakter çakışmaları (JOİN) giderilmiştir.

### 2. Akıllı SQL Formatlama
- **ScriptDom Entegrasyonu:** Microsoft'un resmi SQL Server 2022 (v160) parser motoru kullanılarak kodlarınız profesyonelce hizalanır.
- **Tools Menüsü:** `Tools -> SQLsense -> Format SQL` yolu ile tüm pencereyi formatlar.

### 3. Profesyonel Altyapı & Tanı (Phase 2)
- **Output Window:** SSMS içinde "SQLsense" özel çıktı penceresi üzerinden anlık loglama ve hata takibi.
- **Modern Mimari:** Temiz kod ve sürdürülebilir katmanlı mimari (Core & Infrastructure).

### 4 Akıllı Kısayollar & Snippets (Phase 3)
- **Özel Kısayollar:** Örneğin `ssf` yazıp Tab'a bastığınızda otomatik olarak `SELECT * FROM ` bloğunun gelmesi.

---

## 🗺️ Gelecek Özellikler & Yol Haritası (Roadmap)

### ⚙️ Phase 4: Gelişmiş Ayarlar Ekranı (Settings UI)
- **Özelleştirilebilir Kurallar:** Anahtar kelime casing tercihi, virgül yerleşimi vb.
- **Seçenek Yönetimi:** `Tools -> Options` altına entegre ayar paneli.

### 🔍 Phase 5: Gelişmiş Intellisense & Analiz
- Kod analizi ve performans uyarıları (Implicit conversion, Missing index vs.).

---

## 🛠️ Kurulum ve Geliştirme

Proje MSBuild 17.0+ ve .NET Framework 4.8 gerektirir. 
Derleme sonrası `Deploy-To-SSMS.ps1` betiği ile yerel SSMS kurulumunuza hızlıca dağıtım yapabilirsiniz.

---
**SQLsense Team** - *SQL yazmak artık daha keyifli.*

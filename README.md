# SQLsense - Intelligent SQL Assistant for SSMS 22

SQLsense, SQL Server Management Studio (SSMS) 22 için geliştirilmiş, geliştirici verimliliğini artırmayı hedefleyen akıllı bir sorgu yardımcısıdır. Redgate SQL Prompt benzeri bir deneyim sunarak kod yazım sürecini hızlandırır ve standartlaştırır.

## 🚀 Mevcut Özellikler (Phase 1)

### 1. Real-Time Keyword Casing (Canlı Yazım Desteği)
- **Anlık Dönüşüm:** Siz kod yazarken `select`, `from`, `truncate` gibi 180'den fazla T-SQL anahtar kelimesi, Boşluk (Space), Tab veya Enter tuşuna bastığınız anda otomatik olarak büyük harfe çevrilir.
- **Kültürel Uyumluluk:** `ToUpperInvariant` mimarisi sayesinde Türkçe işletim sistemlerindeki `join -> JOİN` hatası giderilmiştir; her zaman standart `JOIN` çıktısı üretilir.

### 2. Akıllı SQL Formatlama
- **ScriptDom Entegrasyonu:** Microsoft'un resmi SQL Server 2022 (v160) parser motoru kullanılarak kodlarınız profesyonelce hizalanır.
- **Tools Menüsü:** `Tools -> SQLsense -> Format SQL` yolunu kullanarak tüm sorgu penceresini tek tuşla formatlayabilirsiniz.

### 3. SSMS 22 (x64) Uyumluluğu
- Modern Visual Studio 2022 kabuğu üzerine inşa edilmiş SSMS 22 ile tam uyumlu, 64-bit VSIX mimarisi.

---

## 🗺️ Gelecek Özellikler & Yol Haritası (Roadmap)

### ⚡ Phase 2: Akıllı Kısayollar & Snippets
- **Özel Kısayollar:** Örneğin `ssf` yazıp Tab'a bastığınızda otomatik olarak `SELECT * FROM ` bloğunun gelmesi.
- **Snippet Manager:** Kullanıcının kendi sık kullandığı kod bloklarını tanımlayabileceği ve kısayol atayabileceği bir yapı.

### ⚙️ Phase 3: Gelişmiş Ayarlar Ekranı (Settings UI)
- **Özelleştirilebilir Kurallar:** 
  - Anahtar kelimeler büyük mü (UPPER) yoksa küçük mü (lower) olsun?
  - Virgüller satır başında mı yoksa sonunda mı yer alsın?
  - Girintileme (Indentation) kaç karakter olsun?
- **Seçenek Yönetimi:** `Tools -> Options` altına entegre edilmiş bir ayar paneli üzerinden tüm formatlama kurallarının yönetilmesi.

### 🔍 Phase 4: Gelişmiş Intellisense & Analiz
- Tablo ve kolon önerilerinin daha akıllı ve performanslı hale getirilmesi.
- Kod analizi yaparak olası performans sorunlarının (Implicit conversion, Missing index vs.) uyarılması.

---

## 🛠️ Kurulum ve Geliştirme

Proje MSBuild 17.0+ ve .NET Framework 4.8 gerektirir. 
Derleme sonrası `Deploy-To-SSMS.ps1` betiği ile yerel SSMS kurulumunuza hızlıca dağıtım yapabilirsiniz.

---
**SQLsense Team** - *SQL yazmak artık daha keyifli.*
